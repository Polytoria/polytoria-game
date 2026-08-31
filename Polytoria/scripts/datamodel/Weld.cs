// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Godot;
using Polytoria.Attributes;
using Polytoria.Physics;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Weld : Instance
{
	private Instance? _part0;
	private Instance? _part1;

	private RigidBody? _registered0;
	private RigidBody? _registered1;

	private bool _refreshQueued;
	private bool _waitQueued;

	[Editable, ScriptProperty]
	public Instance? Part0
	{
		get => _part0;
		set
		{
			if (_part0 == value) return;
			if (value != null && value == _part1) return;

			_part0 = value;
			OnPropertyChanged();

			RefreshRegistration();
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

			_part1 = value;
			OnPropertyChanged();

			RefreshRegistration();
		}
	}

	[ScriptMethod]
	public void Break()
	{
		_part0 = null;
		_part1 = null;

		OnPropertyChanged(nameof(Part0));
		OnPropertyChanged(nameof(Part1));

		RefreshRegistration();
	}

	public override void EnterTree()
	{
		base.EnterTree();

		if (_part0 == null && Parent is Physical)
		{
			_part0 = Parent;
			OnPropertyChanged(nameof(Part0));
		}

		RefreshRegistration();
	}

	public override void PostReparent()
	{
		base.PostReparent();
		RefreshRegistration();
	}

	public override void PreDelete()
	{
		Unregister();
		base.PreDelete();
	}

	public override void Ready()
	{
		base.Ready();
		QueueRefresh();
	}

	private void RefreshRegistration()
	{
		RigidBody? active0 = null;
		RigidBody? active1 = null;

		if (IsActiveWeld())
		{
			active0 = _part0 as RigidBody;
			active1 = _part1 as RigidBody;
		}

		if (_registered0 == active0 && _registered1 == active1)
		{
			return;
		}

		Unregister();

		if (active0 != null && active1 != null)
		{
			_registered0 = active0;
			_registered1 = active1;
			WeldAssemblyManager.OnWeldAdded(this, active0, active1);
		}
	}

	private void Unregister()
	{
		if (_registered0 == null || _registered1 == null)
		{
			_registered0 = null;
			_registered1 = null;
			return;
		}

		RigidBody old0 = _registered0;
		RigidBody old1 = _registered1;

		_registered0 = null;
		_registered1 = null;

		WeldAssemblyManager.OnWeldRemoved(this, old0, old1);
	}

	private bool IsActiveWeld()
	{
		if (IsDeleted) return false;
		if (Parent == null) return false;
		if (IsInTemporary) return false;

		if (Root == null) return false;

		if (Root.SessionType != World.SessionTypeEnum.Creator && !Root.IsLoaded)
		{
			WaitForLoad();
			return false;
		}

		if (Root.Network != null && Root.SessionType == World.SessionTypeEnum.Client && !Root.Network.IsServer && !Root.Network.IsTransformReplicateDone)
		{
			QueueRefresh();
			return false;
		}

		if (!IsPropReady)
		{
			QueueRefresh();
			return false;
		}

		if (_part0 is not RigidBody p0) return false;
		if (_part1 is not RigidBody p1) return false;
		if (p0 == p1) return false;

		if (!p0.IsPropReady || !p1.IsPropReady)
		{
			QueueRefresh();
			return false;
		}

		if (!p0.GDNode3D.IsInsideTree() || !p1.GDNode3D.IsInsideTree())
		{
			QueueRefresh();
			return false;
		}

		if (p0.IsDeleted || p1.IsDeleted) return false;
		if (p0.IsInTemporary || p1.IsInTemporary) return false;

		return true;
	}

	private void QueueRefresh()
	{
		if (_refreshQueued) return;
		_refreshQueued = true;

		Callable.From(() =>
		{
			_refreshQueued = false;
			RefreshRegistration();
		}).CallDeferred();
	}


	private void WaitForLoad()
	{
		if (_waitQueued) return;
		_waitQueued = true;

		Root.Loaded.Once(() =>
		{
			Callable.From(() =>
			{
				_waitQueued = false;
				RefreshRegistration();
			}).CallDeferred();
		});
	}
}
