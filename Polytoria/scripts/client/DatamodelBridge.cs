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
	private const float ChunkBaseSize = 128f;
	private const float CoarseChunkSize = 1024f;
	private const int SplitGroupSize = 512;

	private World Root = null!;
	public long SeparatedPartCount = 0;

	private readonly Dictionary<Part, PartHandle> _handles = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<ChunkKey, ChunkBatch> _batches = [];
	private readonly Dictionary<(Part.PartMaterialEnum, Part.ShapeEnum), int> _groupCounts = [];
	private readonly HashSet<Part> _dirty = new(ReferenceEqualityComparer.Instance);
	private readonly HashSet<Part> _recheck = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<Part, System.Action<object>> _handlers = new(ReferenceEqualityComparer.Instance);
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
				foreach (Part item in new List<Part>(_handlers.Keys))
				{
					DisconnectHandler(item);
				}

				foreach (Part item in new List<Part>(_handles.Keys))
				{
					RemoveFromBatch(item);
				}

				_dirty.Clear();
				_recheck.Clear();
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
		if (_dirty.Count == 0) return;

		foreach (Part part in _dirty)
		{
			if (!_recheck.Remove(part))
			{
				if (part.IsDeleted || !IsInstanceValid(part.GDNode3D)) continue;

				if (_handles.TryGetValue(part, out PartHandle? moved))
				{
					ChunkKey movedKey = GetKeyForPart(part);
					if (!movedKey.Equals(moved.Key))
					{
						RemoveFromBatch(part);
						AddToBatch(part, movedKey);
						continue;
					}

					moved.Batch.MultiMesh.SetInstanceTransform(moved.Index, part.GetGlobalTransform());
				}
				continue;
			}

			bool inBatch = _handles.TryGetValue(part, out PartHandle? handle);
			bool shouldBatch = IsPartEligible(part);
			ChunkKey newKey = shouldBatch ? GetKeyForPart(part) : default;

			if (shouldBatch)
			{
				if (!inBatch)
				{
					AddToBatch(part, newKey);
					ConnectHandler(part);
				}
				else if (!newKey.Equals(handle!.Key))
				{
					RemoveFromBatch(part);
					AddToBatch(part, newKey);
				}
				else
				{
					handle!.Batch.MultiMesh.SetInstanceTransform(handle.Index, part.GetGlobalTransform());
					handle.Batch.MultiMesh.SetInstanceColor(handle.Index, part.Color.SrgbToLinear());
				}
			}
			else
			{
				DisconnectHandler(part);

				if (inBatch)
				{
					RemoveFromBatch(part);
				}

				if (!part.IsMeshSeparated && !part.IsDeleted)
				{
					part.CreateSeparateMesh();
				}
			}
		}

		_dirty.Clear();
	}

	private ChunkKey GetKeyForPart(Part part)
	{
		bool isDynamic = !part.Anchored;
		bool split = !isDynamic && _groupCounts.GetValueOrDefault((part.Material, part.Shape)) >= SplitGroupSize;
		float size = split ? ChunkBaseSize : CoarseChunkSize;

		Vector3 pos = part.Position + new Vector3(size * 0.5f, size * 0.5f, size * 0.5f);
		Vector3I coord = new(
			Mathf.FloorToInt(pos.X / size),
			Mathf.FloorToInt(pos.Y / size),
			Mathf.FloorToInt(pos.Z / size));

		return new ChunkKey(coord, part.Material, part.Shape, part.Color.A < 1f, part.CastShadows, isDynamic);
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

	private void AddToBatch(Part part, ChunkKey key)
	{
		if (!_batches.TryGetValue(key, out var batch))
		{
			(Godot.Mesh mesh, _) = Globals.LoadShape(part.Shape.ToString());

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

			Material mat = GetMaterial(key.Material, key.IsTransparent);
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

		part.RemoveSeparateMesh();

		int index = batch.Count;
		ResizeBatch(batch, index + 1);

		batch.Parts.Add(part);
		batch.Count++;
		batch.MultiMesh.VisibleInstanceCount = batch.Count;

		batch.MultiMesh.SetInstanceTransform(index, part.GetGlobalTransform());
		batch.MultiMesh.SetInstanceColor(index, part.Color.SrgbToLinear());

		_handles[part] = new PartHandle { Key = key, Batch = batch, Index = index };

		UpdateGroupCount(key.Material, key.Shape, key.IsDynamic, 1);
	}

	private void UpdateGroupCount(Part.PartMaterialEnum material, Part.ShapeEnum shape, bool isDynamic, int delta)
	{
		if (isDynamic) return;

		(Part.PartMaterialEnum, Part.ShapeEnum) group = (material, shape);
		int count = _groupCounts.GetValueOrDefault(group) + delta;

		if (count <= 0)
		{
			_groupCounts.Remove(group);
		}
		else
		{
			_groupCounts[group] = count;
		}
	}

	private void RemoveFromBatch(Part part)
	{
		if (!_handles.TryGetValue(part, out PartHandle? handle)) return;
		if (!_batches.TryGetValue(handle.Key, out var batch)) return;

		int index = handle.Index;
		int lastIndex = batch.Count - 1;

		if (index != lastIndex)
		{
			Part lastPart = batch.Parts[lastIndex];
			batch.Parts[index] = lastPart;

			_handles[lastPart].Index = index;

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

		UpdateGroupCount(batch.Key.Material, batch.Key.Shape, batch.Key.IsDynamic, -1);

		if (batch.Count == 0)
		{
			RenderingServer.FreeRid(batch.Rid);
			_batches.Remove(handle.Key);
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

		AddToBatch(part, GetKeyForPart(part));
		ConnectHandler(part);

		_dirty.Add(part);
		_recheck.Add(part);
	}

	private void ConnectHandler(Part part)
	{
		if (_handlers.ContainsKey(part)) return;

		void propertyChangedHandler(object name)
		{
			if (!isGameReady) return;

			_dirty.Add(part);

			if (name is not (nameof(Dynamic.Position) or nameof(Dynamic.Rotation) or nameof(Dynamic.Size)
				or nameof(Dynamic.LocalPosition) or nameof(Dynamic.LocalRotation) or nameof(Dynamic.LocalSize)
				or nameof(Dynamic.Quaternion) or nameof(Dynamic.LocalQuaternion)))
			{
				_recheck.Add(part);
			}
		}

		_handlers[part] = propertyChangedHandler;
		part.PropertyChanged.Connect(propertyChangedHandler);
	}

	private void DisconnectHandler(Part part)
	{
		if (!_handlers.Remove(part, out System.Action<object>? handler)) return;

		part.PropertyChanged.Disconnect(handler);
	}

	public void MarkMoved(Part part)
	{
		if (!isGameReady) return;
		MarkMovedTree(part);
	}

	private void MarkMovedTree(Instance instance)
	{
		if (instance is Part part && _handles.ContainsKey(part))
		{
			_dirty.Add(part);
		}

		foreach (Instance child in instance.Children)
		{
			MarkMovedTree(child);
		}
	}

	public void MarkDirty(Part part)
	{
		if (!isGameReady) return;

		_dirty.Add(part);
		_recheck.Add(part);
	}

	public void RemovePart(Part part)
	{
		if (!_renderingEnabled) return;

		DisconnectHandler(part);

		if (!part.IsDeleted)
		{
			part.CreateSeparateMesh();
		}

		RemoveFromBatch(part);
	}

	public static bool IsPartEligible(Part part)
	{
		if (part.IsHidden || part.IsInTemporary) return false;
		if (part.OverrideNoMultiMesh) return false;
		if (!IsInstanceValid(part.GDNode3D) || !part.GDNode3D.IsInsideTree()) return false;
		if (part.IsDeleted) return false;
		if (part.IsDescendantOfClass<Camera>()) return false;
		return true;
	}

	private class PartHandle
	{
		public ChunkKey Key;
		public ChunkBatch Batch = null!;
		public int Index;
	}

	private record struct ChunkKey(
		Vector3I Coord,
		Part.PartMaterialEnum Material,
		Part.ShapeEnum Shape,
		bool IsTransparent,
		bool CastShadows,
		bool IsDynamic);

	private class ChunkBatch
	{
		public ChunkKey Key;
		public MultiMesh MultiMesh = null!;
		public Rid Rid;
		public List<Part> Parts = [];
		public int Count;
	}
}
