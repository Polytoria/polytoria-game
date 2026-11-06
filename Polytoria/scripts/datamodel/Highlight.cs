// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Highlight : Instance
{
	private static Shader? _fillShader;
	private static Shader FillShader => _fillShader ??= GD.Load<Shader>("res://resources/shaders/effects/highlight_fill.gdshader");

	private static Shader? _fillOnTopShader;
	private static Shader FillOnTopShader => _fillOnTopShader ??= GD.Load<Shader>("res://resources/shaders/effects/highlight_fill_ontop.gdshader");

	private static Shader? _outlineShader;
	private static Shader OutlineShader => _outlineShader ??= GD.Load<Shader>("res://resources/shaders/effects/highlight_outline.gdshader");

	private static Shader? _outlineOnTopShader;
	private static Shader OutlineOnTopShader => _outlineOnTopShader ??= GD.Load<Shader>("res://resources/shaders/effects/highlight_outline_ontop.gdshader");

	private static readonly HashSet<Highlight> _allHighlights = [];
	public static IReadOnlyCollection<Highlight> ActiveHighlights => _allHighlights;
	private static bool _hierarchyRefreshScheduled;

	internal static void ScheduleHierarchyRefresh()
	{
		if (_allHighlights.Count == 0 || Globals.IsHeadless || !Globals.UseNodes)
		{
			return;
		}

		if (_hierarchyRefreshScheduled)
		{
			return;
		}

		_hierarchyRefreshScheduled = true;
		Callable.From(BatchedRefresh).CallDeferred();
	}

	private static void BatchedRefresh()
	{
		_hierarchyRefreshScheduled = false;
		NotifyHierarchyChanged();
	}

	public static Highlight? GetHighlightFor(Instance? instance)
	{
		if (instance == null || instance.IsDeleted) return null;

		Instance? current = instance;
		while (current != null && !current.IsDeleted)
		{
			foreach (Instance child in current.GetChildren())
			{
				if (child is Highlight h && h.Enabled && !h.IsDeleted && !h.IsHidden)
				{
					return h;
				}
			}

			foreach (Highlight h in _allHighlights)
			{
				if (h.Parent == current && h.Enabled && !h.IsDeleted && !h.IsHidden)
				{
					return h;
				}
			}

			current = current.Parent;
		}

		return null;
	}

	internal static void NotifyHierarchyChanged()
	{
		_hierarchyRefreshScheduled = false;

		if (_allHighlights.Count == 0 || Globals.IsHeadless || !Globals.UseNodes)
		{
			return;
		}

		List<Highlight> highlights = [.. _allHighlights];
		highlights.Sort((a, b) =>
		{
			int depthA = GetHierarchyDepth(a.Parent);
			int depthB = GetHierarchyDepth(b.Parent);
			int cmp = depthB.CompareTo(depthA);
			return cmp != 0 ? cmp : string.CompareOrdinal(a.ObjectID, b.ObjectID);
		});

		foreach (Highlight highlight in highlights)
		{
			if (!highlight.IsDeleted)
			{
				highlight.RefreshTargets();
			}
		}
	}

	public static int GetHierarchyDepth(Instance? instance)
	{
		int depth = 0;
		Instance? cur = instance;
		while (cur != null)
		{
			depth++;
			cur = cur.Parent;
		}
		return depth;
	}

	private Color _outlineColor = new(1, 1, 1, 1);
	private Color _fillColor = new(1, 1, 1, 0.5f);

	private float _outlineSize = 2.0f;
	private bool _alwaysOnTop = false;
	private bool _enabled = true;
	private bool _cameraInside = false;

	private ShaderMaterial? _fillMaterialPart;
	private ShaderMaterial? _maskMaterialPart;
	private ShaderMaterial? _outlineMaterialPart;

	private ShaderMaterial? _fillMaterialMesh;
	private ShaderMaterial? _maskMaterialMesh;
	private ShaderMaterial? _outlineMaterialMesh;
	private Material? _activeMaterialMesh;

	private Instance? _currentTarget;
	private readonly HashSet<MeshInstance3D> _trackedMeshes = [];
	private readonly Dictionary<Instance, List<MeshInstance3D>> _trackedEntityMeshes = [];
	private readonly Dictionary<Part, PartProxy> _partProxies = [];
	private readonly HashSet<Mesh> _trackedMeshInstances = [];

	private sealed class PartProxy(MeshInstance3D fillProxy, MeshInstance3D outlineProxy)
	{
		public MeshInstance3D FillProxy { get; } = fillProxy;
		public MeshInstance3D OutlineProxy { get; } = outlineProxy;
	}

	[Editable, ScriptProperty]
	public Color OutlineColor
	{
		get => _outlineColor;
		set
		{
			if (_outlineColor == value) return;
			_outlineColor = value;
			UpdateMaterials();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color FillColor
	{
		get => _fillColor;
		set
		{
			if (_fillColor == value) return;
			_fillColor = value;
			UpdateMaterials();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(2.0f)]
	public float OutlineSize
	{
		get => _outlineSize;
		set
		{
			value = Mathf.Clamp(value, 0.0f, 15.0f);
			if (Mathf.IsEqualApprox(_outlineSize, value)) return;
			_outlineSize = value;
			UpdateAllPartProxies();
			UpdateMaterials();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool AlwaysOnTop
	{
		get => _alwaysOnTop;
		set
		{
			if (_alwaysOnTop == value) return;
			_alwaysOnTop = value;
			UpdateAllPartProxies();
			UpdateMaterials();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			if (_enabled == value) return;
			_enabled = value;
			UpdateMaterials();
			ScheduleHierarchyRefresh();
			OnPropertyChanged();
		}
	}

	public override void EnterTree()
	{
		base.EnterTree();
		_allHighlights.Add(this);

		if (Root != null)
		{
			Root.InstanceEnteredTree += OnWorldInstanceEnteredTree;
			Root.InstanceExitingTree += OnWorldInstanceExitingTree;
		}

		SetProcess(true);
		ScheduleHierarchyRefresh();
	}

	public override void ExitTree()
	{
		SetProcess(false);
		_allHighlights.Remove(this);

		if (Root != null)
		{
			Root.InstanceEnteredTree -= OnWorldInstanceEnteredTree;
			Root.InstanceExitingTree -= OnWorldInstanceExitingTree;
		}

		ClearTargetBindings();
		ScheduleHierarchyRefresh();
		base.ExitTree();
	}

	public override void PostReparent()
	{
		base.PostReparent();
		if (Parent != null && !IsDeleted)
		{
			_allHighlights.Add(this);
		}
		else
		{
			_allHighlights.Remove(this);
			ClearTargetBindings();
		}
		ScheduleHierarchyRefresh();
	}

	public override void PreDelete()
	{
		SetProcess(false);
		_allHighlights.Remove(this);

		if (Root != null)
		{
			Root.InstanceEnteredTree -= OnWorldInstanceEnteredTree;
			Root.InstanceExitingTree -= OnWorldInstanceExitingTree;
		}

		ClearTargetBindings();
		DisposeMaterials();
		ScheduleHierarchyRefresh();
		base.PreDelete();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (Globals.IsHeadless || !Globals.UseNodes) return;
		UpdateCameraInsideCheck();
	}

	private void UpdateCameraInsideCheck()
	{
		if (_currentTarget == null || _currentTarget.IsDeleted) return;

		Camera3D? camera = GDNode?.GetViewport()?.GetCamera3D();
		bool inside = camera != null && IsCameraInsideTarget(camera.GlobalPosition);

		if (inside != _cameraInside)
		{
			_cameraInside = inside;
			UpdateMaterials();
		}
	}

	private bool IsCameraInsideTarget(Vector3 camPos)
	{
		if (_currentTarget is Part part && !part.IsDeleted && Node.IsInstanceValid(part.GDNode3D))
		{
			if (GetHighlightFor(part) == this)
				return IsPointInsidePart(part, camPos);
		}
		else if (_currentTarget != null)
		{
			foreach (Instance desc in _currentTarget.GetDescendants())
			{
				if (desc is Part p && !p.IsDeleted && Node.IsInstanceValid(p.GDNode3D))
				{
					if (GetHighlightFor(p) == this && IsPointInsidePart(p, camPos)) return true;
				}
			}
		}
		return false;
	}

	private static bool IsPointInsidePart(Part part, Vector3 worldPos)
	{
		Vector3 localPos = part.GDNode3D.ToLocal(worldPos);
		Vector3 half = part.NodeSize * 0.5f;
		return Math.Abs(localPos.X) < half.X &&
			   Math.Abs(localPos.Y) < half.Y &&
			   Math.Abs(localPos.Z) < half.Z;
	}

	public override void HiddenChanged(bool to)
	{
		base.HiddenChanged(to);
		UpdateMaterials();
		ScheduleHierarchyRefresh();
	}

	private void RefreshTarget()
	{
		_currentTarget = Parent;
		RefreshTargets();
	}

	private void RefreshTargets()
	{
		_currentTarget = Parent;

		if (_currentTarget == null || _currentTarget.IsDeleted || !_enabled || IsHidden)
		{
			ClearTargetBindings();
			UpdateMaterials();
			return;
		}

		if (Globals.IsHeadless || !Globals.UseNodes)
		{
			return;
		}

		HashSet<Instance> validTargets = [];
		CollectValidTargets(_currentTarget, validTargets);

		List<Part> partsToRemove = [];
		foreach (Part part in _partProxies.Keys)
		{
			if (!validTargets.Contains(part))
			{
				partsToRemove.Add(part);
			}
		}
		foreach (Part part in partsToRemove)
		{
			RemovePartTarget(part);
		}

		List<Instance> entitiesToRemove = [];
		foreach (Instance ent in _trackedEntityMeshes.Keys)
		{
			if (!validTargets.Contains(ent))
			{
				entitiesToRemove.Add(ent);
			}
		}
		foreach (Instance ent in entitiesToRemove)
		{
			RemoveTargetInstance(ent);
		}

		foreach (Instance target in validTargets)
		{
			AddTargetInstance(target);
		}

		UpdateMaterials();
	}

	private void CollectValidTargets(Instance root, HashSet<Instance> result)
	{
		if (root.IsDeleted) return;

		if (root is not Highlight && GetHighlightFor(root) == this)
		{
			result.Add(root);
		}

		foreach (Instance child in root.GetChildren())
		{
			CollectValidTargets(child, result);
		}
	}

	private void BindTarget(Instance target)
	{
		RefreshTargets();
	}

	private void ClearTargetBindings()
	{
		foreach (Instance entity in _trackedEntityMeshes.Keys)
		{
			if (entity is Mesh mesh && !mesh.IsDeleted)
			{
				mesh.Loaded.Disconnect(OnMeshAssetLoaded);
			}
		}

		foreach (var (_, list) in _trackedEntityMeshes)
		{
			foreach (MeshInstance3D m3d in list)
			{
				if (Node.IsInstanceValid(m3d))
				{
					if (m3d.MaterialOverlay == _activeMaterialMesh ||
						m3d.MaterialOverlay == _fillMaterialMesh ||
						m3d.MaterialOverlay == _maskMaterialMesh ||
						m3d.MaterialOverlay == _outlineMaterialMesh)
					{
						m3d.MaterialOverlay = null;
					}
				}
			}
		}
		_trackedEntityMeshes.Clear();
		_trackedMeshes.Clear();
		_trackedMeshInstances.Clear();

		foreach ((Part part, PartProxy proxies) in _partProxies)
		{
			if (!part.IsDeleted)
			{
				part.ShapeChanged -= OnPartShapeChanged;
			}
			if (Node.IsInstanceValid(proxies.FillProxy))
			{
				proxies.FillProxy.MaterialOverride = null;
				proxies.FillProxy.MaterialOverlay = null;
				proxies.FillProxy.GetParent()?.RemoveChild(proxies.FillProxy);
				proxies.FillProxy.QueueFree();
			}
			if (Node.IsInstanceValid(proxies.OutlineProxy))
			{
				proxies.OutlineProxy.MaterialOverride = null;
				proxies.OutlineProxy.MaterialOverlay = null;
				proxies.OutlineProxy.GetParent()?.RemoveChild(proxies.OutlineProxy);
				proxies.OutlineProxy.QueueFree();
			}
		}
		_partProxies.Clear();

		_currentTarget = null;
	}

	private void AddTargetInstance(Instance instance)
	{
		if (instance.IsDeleted) return;

		if (instance is Part part)
		{
			AddPartTarget(part);
		}
		else if (instance is Mesh mesh)
		{
			AddMeshTarget(mesh);
		}
		else if (instance is not Model && instance.GDNode != null)
		{
			AddEntityMeshTarget(instance);
		}
	}

	private void RemoveTargetInstance(Instance instance)
	{
		if (instance is Part part)
		{
			RemovePartTarget(part);
		}
		else if (instance is Mesh mesh)
		{
			RemoveMeshTarget(mesh);
		}
		else if (instance is not Model)
		{
			RemoveEntityMeshTarget(instance);
		}
	}

	private void AddPartTarget(Part part)
	{
		if (_partProxies.ContainsKey(part)) return;
		if (Globals.IsHeadless || !Globals.UseNodes) return;
		if (!Node.IsInstanceValid(part.GDNode3D)) return;

		MeshInstance3D fillProxy = new()
		{
			Name = "HighlightFillProxy",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			ExtraCullMargin = 100.0f,
			Visible = false
		};
		fillProxy.SetMeta("HighlightProxy", true);

		MeshInstance3D outlineProxy = new()
		{
			Name = "HighlightOutlineProxy",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			ExtraCullMargin = 100.0f,
			Visible = false
		};
		outlineProxy.SetMeta("HighlightProxy", true);

		PartProxy proxies = new(fillProxy, outlineProxy);
		UpdatePartProxyMeshes(part, proxies);

		part.GDNode3D.AddChild(fillProxy, @internal: Node.InternalMode.Back);
		part.GDNode3D.AddChild(outlineProxy, @internal: Node.InternalMode.Back);

		part.ShapeChanged += OnPartShapeChanged;
		_partProxies[part] = proxies;

		ApplyPartProxyMaterials(proxies);
	}

	private void RemovePartTarget(Part part)
	{
		if (!_partProxies.Remove(part, out PartProxy? proxies)) return;

		part.ShapeChanged -= OnPartShapeChanged;
		if (Node.IsInstanceValid(proxies.FillProxy))
		{
			proxies.FillProxy.MaterialOverride = null;
			proxies.FillProxy.MaterialOverlay = null;
			proxies.FillProxy.GetParent()?.RemoveChild(proxies.FillProxy);
			proxies.FillProxy.QueueFree();
		}
		if (Node.IsInstanceValid(proxies.OutlineProxy))
		{
			proxies.OutlineProxy.MaterialOverride = null;
			proxies.OutlineProxy.MaterialOverlay = null;
			proxies.OutlineProxy.GetParent()?.RemoveChild(proxies.OutlineProxy);
			proxies.OutlineProxy.QueueFree();
		}
	}

	private void OnPartShapeChanged()
	{
		UpdateAllPartProxies();
	}

	private void UpdateAllPartProxies()
	{
		foreach ((Part part, PartProxy proxies) in _partProxies)
		{
			if (!part.IsDeleted && Node.IsInstanceValid(proxies.FillProxy) && Node.IsInstanceValid(proxies.OutlineProxy))
			{
				UpdatePartProxyMeshes(part, proxies);
			}
		}
	}

	private void UpdatePartProxyMeshes(Part part, PartProxy proxies)
	{
		float thickness = Math.Max(_outlineSize * 0.025f, 0.001f);

		Godot.Mesh shapeMesh = Globals.LoadShape(part.Shape.ToString()).Item1;

		proxies.FillProxy.Mesh = shapeMesh;
		proxies.FillProxy.Scale = part.NodeSize;

		proxies.OutlineProxy.Mesh = shapeMesh;
		proxies.OutlineProxy.Scale = part.NodeSize + Vector3.One * (thickness * 2.0f);
	}

	private void AddMeshTarget(Mesh mesh)
	{
		if (_trackedMeshInstances.Add(mesh))
		{
			mesh.Loaded.Connect(OnMeshAssetLoaded);
		}

		AddEntityMeshTarget(mesh);
	}

	private void RemoveMeshTarget(Mesh mesh)
	{
		if (_trackedMeshInstances.Remove(mesh))
		{
			mesh.Loaded.Disconnect(OnMeshAssetLoaded);
		}

		RemoveEntityMeshTarget(mesh);
	}

	private void AddEntityMeshTarget(Instance entity)
	{
		if (Globals.IsHeadless || !Globals.UseNodes) return;
		if (entity.GDNode == null || !Node.IsInstanceValid(entity.GDNode)) return;

		RemoveEntityMeshTarget(entity);

		List<MeshInstance3D> scanned = ScanMeshInstances(entity.GDNode);
		_trackedEntityMeshes[entity] = scanned;
		foreach (MeshInstance3D m3d in scanned)
		{
			_trackedMeshes.Add(m3d);
			m3d.ExtraCullMargin = 100.0f;
			m3d.MaterialOverlay = _activeMaterialMesh;
		}
	}

	private void RemoveEntityMeshTarget(Instance entity)
	{
		if (!_trackedEntityMeshes.Remove(entity, out List<MeshInstance3D>? list)) return;

		foreach (MeshInstance3D m3d in list)
		{
			_trackedMeshes.Remove(m3d);
			if (Node.IsInstanceValid(m3d))
			{
				if (m3d.MaterialOverlay == _activeMaterialMesh ||
					m3d.MaterialOverlay == _fillMaterialMesh ||
					m3d.MaterialOverlay == _maskMaterialMesh ||
					m3d.MaterialOverlay == _outlineMaterialMesh)
				{
					m3d.MaterialOverlay = null;
				}
			}
		}
	}

	private void OnMeshAssetLoaded()
	{
		if (_currentTarget != null)
		{
			RefreshMeshInstancesForTarget();
		}
	}

	private void RefreshMeshInstancesForTarget()
	{
		if (_currentTarget == null || _currentTarget.IsDeleted) return;

		foreach (Mesh mesh in _trackedMeshInstances)
		{
			if (!mesh.IsDeleted && mesh.GDNode != null && Node.IsInstanceValid(mesh.GDNode))
			{
				AddEntityMeshTarget(mesh);
			}
		}
	}

	private List<MeshInstance3D> ScanMeshInstances(Node rootNode)
	{
		List<MeshInstance3D> result = [];
		if (Globals.IsHeadless || !Globals.UseNodes) return result;
		if (!Node.IsInstanceValid(rootNode)) return result;

		void CheckNode(Node node)
		{
			if (node is MeshInstance3D m3d)
			{
				if (m3d.IsQueuedForDeletion()) return;
				if (m3d.HasMeta("HighlightProxy")) return;
				if (m3d.Name.ToString().Contains("Highlight", StringComparison.OrdinalIgnoreCase)) return;

				Node? parent = m3d.GetParent();
				bool isPartOrProxy = false;
				while (parent != null && parent != rootNode)
				{
					if (parent.Name.ToString().Contains("Highlight", StringComparison.OrdinalIgnoreCase) || parent.HasMeta("HighlightProxy"))
					{
						isPartOrProxy = true;
						break;
					}
					parent = parent.GetParent();
				}
				if (!isPartOrProxy)
				{
					result.Add(m3d);
				}
			}
		}

		CheckNode(rootNode);
		foreach (Node item in GetDescendantsInternal(rootNode))
		{
			CheckNode(item);
		}
		return result;
	}

	private void OnWorldInstanceEnteredTree(Instance instance)
	{
		if (instance is Highlight)
		{
			ScheduleHierarchyRefresh();
		}
		else if (_currentTarget != null && !_currentTarget.IsDeleted && (instance.IsDescendantOf(_currentTarget) || instance == _currentTarget))
		{
			ScheduleHierarchyRefresh();
		}
	}

	private void OnWorldInstanceExitingTree(Instance instance)
	{
		if (instance is Highlight)
		{
			ScheduleHierarchyRefresh();
		}
		else if (instance == _currentTarget)
		{
			ClearTargetBindings();
		}
		else if (_currentTarget != null && !_currentTarget.IsDeleted && instance.IsDescendantOf(_currentTarget))
		{
			RemoveTargetInstance(instance);
			ScheduleHierarchyRefresh();
		}
	}

	private void UpdateMaterials()
	{
		if (Globals.IsHeadless || !Globals.UseNodes) return;

		bool hasFill = _enabled && !IsHidden && _currentTarget != null && _fillColor.A > 0.001f;
		bool hasOutline = _enabled && !IsHidden && !_cameraInside && _currentTarget != null && _outlineColor.A > 0.001f && _outlineSize > 0.0001f;

		if (!hasFill && !hasOutline)
		{
			_activeMaterialMesh = null;
			ApplyActiveMaterials(false, false);
			return;
		}

		int depth = GetHierarchyDepth(_currentTarget);
		int clampedDepth = Math.Clamp(depth, 0, 20);
		int depthOffset = (20 - clampedDepth) * 2;
		int basePriority = (_alwaysOnTop ? 60 : 0) + depthOffset;
		Shader currentFillShader = _alwaysOnTop ? FillOnTopShader : FillShader;
		Shader currentOutlineShader = _alwaysOnTop ? OutlineOnTopShader : OutlineShader;
		float meshOutlineThickness = Math.Max(_outlineSize * 0.025f, 0.001f);

		// Part Materials
		if (hasFill)
		{
			_fillMaterialPart ??= new ShaderMaterial();
			_fillMaterialPart.Shader = currentFillShader;
			_fillMaterialPart.SetShaderParameter("fill_color", _fillColor);
			_fillMaterialPart.RenderPriority = basePriority + 1;
			_fillMaterialPart.NextPass = null;
		}
		else if (hasOutline)
		{
			_maskMaterialPart ??= new ShaderMaterial();
			_maskMaterialPart.Shader = currentFillShader;
			_maskMaterialPart.SetShaderParameter("fill_color", new Color(0, 0, 0, 0));
			_maskMaterialPart.RenderPriority = basePriority + 1;
			_maskMaterialPart.NextPass = null;
		}

		if (hasOutline)
		{
			_outlineMaterialPart ??= new ShaderMaterial();
			_outlineMaterialPart.Shader = currentOutlineShader;
			_outlineMaterialPart.SetShaderParameter("outline_color", _outlineColor);
			_outlineMaterialPart.SetShaderParameter("outline_size", 0.0f); // Pre-expanded in mesh
			_outlineMaterialPart.SetShaderParameter("camera_inside", _cameraInside);
			_outlineMaterialPart.RenderPriority = basePriority + 2;
			_outlineMaterialPart.NextPass = null;
		}

		if (hasOutline)
		{
			_outlineMaterialMesh ??= new ShaderMaterial();
			_outlineMaterialMesh.Shader = currentOutlineShader;
			_outlineMaterialMesh.SetShaderParameter("outline_color", _outlineColor);
			_outlineMaterialMesh.SetShaderParameter("outline_size", meshOutlineThickness);
			_outlineMaterialMesh.SetShaderParameter("camera_inside", _cameraInside);
			_outlineMaterialMesh.RenderPriority = basePriority + 2;
			_outlineMaterialMesh.NextPass = null;
		}

		if (hasFill)
		{
			_fillMaterialMesh ??= new ShaderMaterial();
			_fillMaterialMesh.Shader = currentFillShader;
			_fillMaterialMesh.SetShaderParameter("fill_color", _fillColor);
			_fillMaterialMesh.RenderPriority = basePriority + 1;
			_fillMaterialMesh.NextPass = hasOutline ? _outlineMaterialMesh : null;
			_activeMaterialMesh = _fillMaterialMesh;
		}
		else if (hasOutline)
		{
			_maskMaterialMesh ??= new ShaderMaterial();
			_maskMaterialMesh.Shader = currentFillShader;
			_maskMaterialMesh.SetShaderParameter("fill_color", new Color(0, 0, 0, 0));
			_maskMaterialMesh.RenderPriority = basePriority + 1;
			_maskMaterialMesh.NextPass = hasOutline ? _outlineMaterialMesh : null;
			_activeMaterialMesh = _maskMaterialMesh;
		}
		else
		{
			_activeMaterialMesh = null;
		}

		ApplyActiveMaterials(hasFill, hasOutline);
	}

	private void ApplyActiveMaterials(bool hasFill, bool hasOutline)
	{
		foreach (MeshInstance3D mesh in _trackedMeshes)
		{
			if (Node.IsInstanceValid(mesh))
			{
				mesh.MaterialOverlay = _activeMaterialMesh;
			}
		}

		foreach ((_, PartProxy proxies) in _partProxies)
		{
			ApplyPartProxyMaterials(proxies);
		}
	}

	private void ApplyPartProxyMaterials(PartProxy proxies)
	{
		bool hasFill = _enabled && !IsHidden && _currentTarget != null && _fillColor.A > 0.001f;
		bool hasOutline = _enabled && !IsHidden && _currentTarget != null && _outlineColor.A > 0.001f && _outlineSize > 0.0001f;

		if (Node.IsInstanceValid(proxies.FillProxy))
		{
			if (hasFill)
			{
				proxies.FillProxy.Visible = true;
				proxies.FillProxy.MaterialOverride = _fillMaterialPart;
			}
			else if (hasOutline)
			{
				proxies.FillProxy.Visible = true;
				proxies.FillProxy.MaterialOverride = _maskMaterialPart;
			}
			else
			{
				proxies.FillProxy.Visible = false;
				proxies.FillProxy.MaterialOverride = null;
			}
		}

		if (Node.IsInstanceValid(proxies.OutlineProxy))
		{
			bool needOutlineProxy = hasOutline;
			if (needOutlineProxy)
			{
				proxies.OutlineProxy.Visible = true;
				proxies.OutlineProxy.MaterialOverride = _outlineMaterialPart;
			}
			else
			{
				proxies.OutlineProxy.Visible = false;
				proxies.OutlineProxy.MaterialOverride = null;
			}
		}
	}

	private void DisposeMaterials()
	{
		_activeMaterialMesh = null;

		_fillMaterialPart?.Dispose();
		_fillMaterialPart = null;

		_maskMaterialPart?.Dispose();
		_maskMaterialPart = null;

		_outlineMaterialPart?.Dispose();
		_outlineMaterialPart = null;

		_fillMaterialMesh?.Dispose();
		_fillMaterialMesh = null;

		_maskMaterialMesh?.Dispose();
		_maskMaterialMesh = null;

		_outlineMaterialMesh?.Dispose();
		_outlineMaterialMesh = null;
	}
}
