// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Client;

/// <summary>
/// Multimesh bridge for Datamodel
/// </summary>
public partial class DatamodelBridge : Node3D
{
	private const float ChunkBaseSize = 64f;
	private World Root = null!;
	public long SeparatedPartCount = 0;

	private readonly Dictionary<Part, PartHandle> _handles = [];
	private readonly Dictionary<ChunkKey, ChunkBatch> _batches = [];
	private readonly HashSet<Part> _dirty = [];
	private readonly Dictionary<Part, System.Action> _propertyHandlers = [];
	private readonly List<Part> _dirtyScratch = [];
	private readonly Dictionary<ChunkKey, ulong> _emptyBatchExpiry = [];
	private readonly List<ChunkKey> _expiredBatchScratch = [];
	private const ulong EmptyBatchKeepAliveMsec = 5000;
	private Rid _scenario;

	private readonly Dictionary<(Part.PartMaterialEnum, bool), Material> _materials = [];

	private bool isGameReady = false;
	private bool _renderingEnabled;

	public void Attach(World root, bool manualRebuild = false)
	{
		if (Root != null)
		{
			Root.InstanceEnteredTree -= OnInstanceAdded;
			Root.InstanceExitingTree -= OnInstanceRemoving;
		}

		Root = root;
		root.Bridge = this;
		_renderingEnabled = DisplayServer.GetName() != "headless";
		SetProcess(_renderingEnabled);

		if (!_renderingEnabled)
		{
			return;
		}

		_scenario = Root.World3D.Scenario;

		root.InstanceEnteredTree += OnInstanceAdded;
		root.InstanceExitingTree += OnInstanceRemoving;
		root.Loaded.Once(OnGameReady);

		if (manualRebuild)
		{
			foreach (var item in Root.Environment.GetDescendants())
			{
				if (item is Part p)
				{
					AddPart(p);
				}
			}
		}
	}

	public override void _ExitTree()
	{
		if (Root != null)
		{
			if (_renderingEnabled)
			{
				Root.InstanceEnteredTree -= OnInstanceAdded;
				Root.InstanceExitingTree -= OnInstanceRemoving;
				Root.Loaded.Disconnect(OnGameReady);

				// Cleanup parts
				foreach (Part item in new List<Part>(_handles.Keys))
				{
					RemovePart(item);
				}

				foreach (ChunkBatch batch in _batches.Values)
				{
					RenderingServer.FreeRid(batch.Rid);
				}
				_batches.Clear();
				_emptyBatchExpiry.Clear();
			}

			Root.Bridge = null!;
		}
		base._ExitTree();
	}

	private Material GetMaterial(Part.PartMaterialEnum partMaterial, bool isTransparent)
	{
		if (_materials.TryGetValue((partMaterial, isTransparent), out Material? mat))
		{
			return mat;
		}

		mat = Globals.LoadMaterial(partMaterial, isTransparent ? 0f : 1f) ?? throw new System.Exception("Unknown material: " + partMaterial.ToString());
		if (mat is StandardMaterial3D sm)
		{
			sm.VertexColorUseAsAlbedo = true;
			sm.VertexColorIsSrgb = true;
			sm.Uv1WorldTriplanar = true;

			if (isTransparent)
			{
				sm.Transparency = isTransparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
			}

			sm.RoughnessTexture = null;

			// Disable some property for mobile for performance
#if GODOT_MOBILE
			sm.NormalTexture = null;
			sm.DetailEnabled = false;
			sm.AOTexture = null;
#endif
		}

		_materials.Add((partMaterial, isTransparent), mat);

		return mat;
	}

	public override void _Process(double delta)
	{
		if (!_renderingEnabled || !isGameReady) return;

		SweepEmptyBatches();

		if (_dirty.Count == 0) return;

		_dirtyScratch.AddRange(_dirty);
		_dirty.Clear();

		foreach (Part part in _dirtyScratch)
		{
			bool inBatch = _handles.TryGetValue(part, out PartHandle? handle);
			bool shouldBatch = IsPartEligible(part);

			if (shouldBatch)
			{
				ChunkKey newKey = GetKeyForPart(part);

				if (!inBatch)
				{
					AddToBatch(part, newKey);
				}
				else if (!newKey.Equals(handle!.Key))
				{
					RemoveFromBatch(part);
					AddToBatch(part, newKey);
				}
				else
				{
					ChunkBatch batch = _batches[handle.Key];
					batch.MultiMesh.SetInstanceTransform(handle.Index, part.GetGlobalTransform());
					Color color = part.Color;
					if (handle.LastColor != color)
					{
						handle.LastColor = color;
						batch.MultiMesh.SetInstanceColor(handle.Index, color.SrgbToLinear());
					}
				}
			}
			else
			{
				if (inBatch)
				{
					RemoveFromBatch(part);
				}

				if (!part.IsMeshSeparated)
				{
					part.CreateSeparateMesh();
				}
			}
		}

		_dirtyScratch.Clear();
	}

	private static ChunkKey GetKeyForPart(Part part)
	{
		uint scaleLevel = 1;
		float size = ChunkBaseSize;
		Vector3 partSize = part.Size;

		while (partSize.X > size || partSize.Y > size || partSize.Z > size)
		{
			size *= 2;
			scaleLevel++;

			if (scaleLevel > 10) break;
		}

		Vector3I coord = GetChunkCoord(part.Position, scaleLevel);
		return new ChunkKey { Coord = coord, Material = part.Material, Shape = part.Shape, IsTransparent = part.Color.A < 1f, CastShadows = part.CastShadows, ScaleLevel = scaleLevel };
	}

	private static Vector3I GetChunkCoord(Vector3 pos, uint scaleLevel = 1)
	{
		float size = ChunkBaseSize * Mathf.Pow(2, scaleLevel - 1);

		int cx = Mathf.FloorToInt(pos.X / size);
		int cy = Mathf.FloorToInt(pos.Y / size);
		int cz = Mathf.FloorToInt(pos.Z / size);

		return new Vector3I(cx, cy, cz);
	}

	private void OnInstanceAdded(Instance instance)
	{
		if (instance is Part part)
		{
			AddPart(part);
		}
	}

	private void OnInstanceRemoving(Instance instance)
	{
		if (instance is Part part)
		{
			RemovePart(part);
		}
	}

	private void OnGameReady()
	{
		isGameReady = true;
	}

	private void SweepEmptyBatches()
	{
		if (_emptyBatchExpiry.Count == 0) return;

		ulong now = Time.GetTicksMsec();
		_expiredBatchScratch.Clear();
		foreach ((ChunkKey key, ulong expireAt) in _emptyBatchExpiry)
		{
			if (now >= expireAt)
			{
				_expiredBatchScratch.Add(key);
			}
		}

		foreach (ChunkKey key in _expiredBatchScratch)
		{
			_emptyBatchExpiry.Remove(key);
			if (_batches.TryGetValue(key, out ChunkBatch? batch) && batch.Count == 0)
			{
				_batches.Remove(key);
				RenderingServer.FreeRid(batch.Rid);
			}
		}
	}

	private void AddToBatch(Part part, ChunkKey key)
	{
		if (!_batches.TryGetValue(key, out var batch))
		{
			(Godot.Mesh mesh, _) = Globals.LoadShape(part.Shape);

			MultiMesh mm = new()
			{
				Mesh = mesh,
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = true,
				InstanceCount = 64,
				VisibleInstanceCount = 0
			};

			Rid rid = RenderingServer.InstanceCreate();
			RenderingServer.InstanceSetScenario(rid, _scenario);
			RenderingServer.InstanceSetBase(rid, mm.GetRid());
			RenderingServer.InstanceSetTransform(rid, Transform3D.Identity);
			RenderingServer.InstanceGeometrySetCastShadowsSetting(rid, key.CastShadows ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off);

			Material mat = GetMaterial(part.Material, part.Color.A < 1f);
			RenderingServer.InstanceGeometrySetMaterialOverride(rid, mat.GetRid());

			batch = new ChunkBatch
			{
				Key = key,
				MultiMesh = mm,
				Rid = rid,
				Parts = new List<Part>(64),
				Count = 0
			};

			_batches.Add(key, batch);
		}
		else
		{
			_emptyBatchExpiry.Remove(key);
		}

		part.RemoveSeparateMesh();

		int index = batch.Count;
		ResizeBatch(batch, index + 1);

		batch.Parts.Add(part);
		batch.Count++;
		batch.MultiMesh.VisibleInstanceCount = batch.Count;

		batch.MultiMesh.SetInstanceTransform(index, part.GetGlobalTransform());
		Color addColor = part.Color;
		batch.MultiMesh.SetInstanceColor(index, addColor.SrgbToLinear());

		_handles[part] = new PartHandle { Key = key, Index = index, LastColor = addColor };
	}

	private void RemoveFromBatch(Part part)
	{
		if (!_handles.TryGetValue(part, out PartHandle? handle)) return;
		if (!_batches.TryGetValue(handle.Key, out var batch))
		{
			return;
		}

		int index = handle.Index;
		int lastIndex = batch.Count - 1;

		if (index != lastIndex)
		{
			var lastPart = batch.Parts[lastIndex];
			batch.Parts[index] = lastPart;

			_handles[lastPart] = new PartHandle { Key = handle.Key, Index = index };

			// prevents a bunch of error spam. idk why these nodes often arent in the tree but this kept spamming errors
			if (lastPart.GDNode3D.IsInsideTree())
			{
				batch.MultiMesh.SetInstanceTransform(index, lastPart.GetGlobalTransform());
			}
			batch.MultiMesh.SetInstanceColor(index, lastPart.Color.SrgbToLinear());
		}

		batch.Parts.RemoveAt(lastIndex);
		batch.Count--;
		batch.MultiMesh.VisibleInstanceCount = batch.Count;

		if (batch.Count == 0)
		{
			_emptyBatchExpiry[handle.Key] = Time.GetTicksMsec() + EmptyBatchKeepAliveMsec;
		}

		_handles.Remove(part);
	}

	private static void ResizeBatch(ChunkBatch batch, int target)
	{
		if (target <= batch.MultiMesh.InstanceCount) return;

		int oldUsedCount = batch.Count;
		int newCap = batch.MultiMesh.InstanceCount;

		while (newCap < target)
		{
			newCap *= 2;
		}

		batch.MultiMesh.InstanceCount = newCap;

		// changing instancecount wipes multimesh data
		for (int i = 0; i < oldUsedCount; i++)
		{
			var p = batch.Parts[i];
			batch.MultiMesh.SetInstanceTransform(i, p.GetGlobalTransform());
			batch.MultiMesh.SetInstanceColor(i, p.Color.SrgbToLinear());
		}
	}

	public void AddPart(Part part)
	{
		if (!_renderingEnabled) return;
		if (_handles.ContainsKey(part)) return;
		if (!IsPartEligible(part))
		{
			part.CreateSeparateMesh();
			return;
		}

		void propertyChangedHandler() { if (isGameReady) _dirty.Add(part); }

		part.PropertyChanged.Connect(propertyChangedHandler);
		_propertyHandlers[part] = propertyChangedHandler;

		var key = GetKeyForPart(part);
		AddToBatch(part, key);

		_dirty.Add(part);
	}

	public void RemovePart(Part part)
	{
		if (!_renderingEnabled) return;
		if (_propertyHandlers.Remove(part, out System.Action? handler))
		{
			part.PropertyChanged.Disconnect(handler);
		}

		part.CreateSeparateMesh();
		RemoveFromBatch(part);
	}

	public static bool IsPartEligible(Part part)
	{
		if (part.IsHidden || part.IsInTemporary) return false;
		if (part.Anchored && !part.OverrideNoMultiMesh)
		{
			if (!IsInstanceValid(part.GDNode3D) || !part.GDNode3D.IsInsideTree()) return false;
			if (part.IsDeleted) return false;
			if (part.IsDescendantOfClass<Camera>()) return false;
			return true;
		}
		return false;
	}

	private class PartHandle
	{
		public ChunkKey Key;
		public int Index;
		public Color LastColor;
	}

	private readonly record struct ChunkKey
	{
		public Vector3I Coord { get; init; }
		public Part.PartMaterialEnum Material { get; init; }
		public Part.ShapeEnum Shape { get; init; }
		public bool IsTransparent { get; init; }
		public bool CastShadows { get; init; }
		public uint ScaleLevel { get; init; }
	}

	private class ChunkBatch
	{
		public ChunkKey Key;
		public MultiMesh MultiMesh = null!;
		public Rid Rid;
		public List<Part> Parts = [];
		public int Count;
	}
}
