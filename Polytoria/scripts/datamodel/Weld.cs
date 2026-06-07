// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Weld : Instance
{
	static readonly Dictionary<Instance, List<Weld>> _connections = new();
	static readonly Dictionary<Instance, System.Action> _handlers = new();

	Instance? _part0;
	Instance? _part1;
	Transform3D _c0 = Transform3D.Identity;
	Transform3D _c1 = Transform3D.Identity;
	bool _enabled = true;
	bool _needsRebuild;
	Generic6DofJoint3D? _joint;

	public static IEnumerable<Weld> GetWeldsFor(Instance part)
	{
		if (part == null || part.IsDeleted)
			return System.Array.Empty<Weld>();
		if (_connections.TryGetValue(part, out List<Weld>? list))
			return list.ToArray();
		return System.Array.Empty<Weld>();
	}

	[Editable, ScriptProperty]
	public Instance? Part0
	{
		get => _part0;
		set
		{
			if (_part0 == value) return;
			if (value != null && value == _part1) return;
			Unregister(_part0);
			_part0 = value;
			Register(_part0);
			OnPropertyChanged();
			RequestRebuild();
		}
	}

	[Editable, ScriptProperty]
	public Instance? Part1
	{
		get => _part1;
		set
		{
			if (_part1 == value) return;
			if (value != null && value == _part0) return;
			Unregister(_part1);
			_part1 = value;
			Register(_part1);
			OnPropertyChanged();
			RequestRebuild();
		}
	}

	[SyncVar]
	public Transform3D C0
	{
		get => _c0;
		set
		{
			if (_c0 == value) return;
			_c0 = value;
			OnPropertyChanged();
			RequestRebuild();
		}
	}

	[SyncVar]
	public Transform3D C1
	{
		get => _c1;
		set
		{
			if (_c1 == value) return;
			_c1 = value;
			OnPropertyChanged();
			RequestRebuild();
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
			OnPropertyChanged();
			RequestRebuild();
		}
	}

	[ScriptMethod]
	public void Break()
	{
		Enabled = false;
		Part0 = null;
		Part1 = null;
	}

	public override void EnterTree()
	{
		base.EnterTree();

		if (_part0 == null && Parent is Physical)
		{
			Part0 = Parent;
		}

		if (_needsRebuild)
		{
			RebuildJoint();
		}
	}

	public override void ExitTree()
	{
		DestroyJoint();
		_needsRebuild = true;
		base.ExitTree();
	}

	public override void PreDelete()
	{
		DestroyJoint();
		Unregister(_part0);
		Unregister(_part1);
		base.PreDelete();
	}


	void Register(Instance? part)
	{
		if (part == null || part.IsDeleted) return;
		if (!_connections.TryGetValue(part, out List<Weld>? list))
		{
			list = new List<Weld>();
			_connections[part] = list;
			System.Action handler = () => OnPartDeleted(part);
			part.Deleted += handler;
			_handlers[part] = handler;
		}
		if (!list.Contains(this))
			list.Add(this);
	}

	void Unregister(Instance? part)
	{
		if (part == null) return;
		if (_connections.TryGetValue(part, out List<Weld>? list))
		{
			list.Remove(this);
			if (list.Count == 0)
			{
				_connections.Remove(part);
				if (_handlers.TryGetValue(part, out var handler))
				{
					part.Deleted -= handler;
					_handlers.Remove(part);
				}
			}
		}
	}

	static void OnPartDeleted(Instance part)
	{
		if (_handlers.TryGetValue(part, out var handler))
		{
			part.Deleted -= handler;
			_handlers.Remove(part);
		}

		if (_connections.TryGetValue(part, out var list))
		{
			foreach (var weld in list.ToArray())
			{
				var other = weld._part0 == part ? weld._part1 : weld._part0;
				if (other != null && _connections.TryGetValue(other, out var otherList))
				{
					otherList.Remove(weld);
					if (otherList.Count == 0)
					{
						_connections.Remove(other);
						if (_handlers.TryGetValue(other, out var otherHandler))
						{
							other.Deleted -= otherHandler;
							_handlers.Remove(other);
						}
					}
				}

				weld._enabled = false;
				weld._part0 = null;
				weld._part1 = null;
				weld.DestroyJoint();
			}
			_connections.Remove(part);
		}
	}

	void RequestRebuild()
	{
		if (GDNode.IsInsideTree())
		{
			RebuildJoint();
		}
		else
		{
			_needsRebuild = true;
		}
	}

	void RebuildJoint()
	{
		_needsRebuild = false;
		DestroyJoint();

		if (!_enabled) return;
		if (_part0 == null || _part1 == null) return;
		if (_part0 == _part1) return;
		if (_part0.IsDeleted || _part1.IsDeleted) return;
		if (_part0 is not Dynamic dyn0 || _part1 is not Dynamic dyn1) return;

		Node3D node0 = dyn0.GDNode3D;
		Node3D node1 = dyn1.GDNode3D;

		if (node0 == null || node1 == null) return;
		if (!node0.IsInsideTree() || !node1.IsInsideTree())
		{
			_needsRebuild = true;
			return;
		}

		if (node0 is not PhysicsBody3D body0 || node1 is not PhysicsBody3D body1) return;

		Transform3D part0Transform = node0.GlobalTransform;
		TryAutoComputeC0(part0Transform, node1.GlobalTransform);

		Transform3D targetPart1 = part0Transform * _c0 * _c1.AffineInverse();

		Vector3 part1Size = dyn1.Size;
		dyn1.SetGlobalTransform(new Transform3D(
			targetPart1.Basis.Orthonormalized().Scaled(part1Size),
			targetPart1.Origin));

		_joint = new Generic6DofJoint3D();
		GDNode.AddChild(_joint);
		_joint.GlobalTransform = part0Transform * _c0;
		_joint.NodeA = body0.GetPath();
		_joint.NodeB = body1.GetPath();

		LockLinearAxis("linear_limit_x");
		LockLinearAxis("linear_limit_y");
		LockLinearAxis("linear_limit_z");
		LockAngularAxis("angular_limit_x");
		LockAngularAxis("angular_limit_y");
		LockAngularAxis("angular_limit_z");
	}

	void TryAutoComputeC0(Transform3D part0Transform, Transform3D part1Transform)
	{
		if (_c0 == Transform3D.Identity && _c1 == Transform3D.Identity)
		{
			_c0 = part0Transform.AffineInverse() * part1Transform;
			OnPropertyChanged("C0");
		}
	}

	void LockLinearAxis(string axis)
	{
		if (_joint == null) return;
		_joint.Set($"{axis}/enabled", true);
		_joint.Set($"{axis}/lower_distance", 0f);
		_joint.Set($"{axis}/upper_distance", 0f);
	}

	void LockAngularAxis(string axis)
	{
		if (_joint == null) return;
		_joint.Set($"{axis}/enabled", true);
		_joint.Set($"{axis}/lower_angle", 0f);
		_joint.Set($"{axis}/upper_angle", 0f);
	}

	void DestroyJoint()
	{
		if (_joint != null)
		{
			if (Node.IsInstanceValid(_joint))
			{
				_joint.QueueFree();
			}

			_joint = null;
		}
	}
}
