// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Datamodel.Resources;

[Abstract]
public partial class BaseAsset : NetworkedObject
{
	private const int DeleteTimeoutSec = 60;
	public List<NetworkedObject> LinkedTo = [];

	private static readonly List<Type> _allDerivedTypes = [];
	internal bool PendingDeletion = false;

	[Export]
	public int LinkCount = 0;

	private Timer? _timer;

	protected static void RegisterType<T>() where T : BaseAsset
	{
		_allDerivedTypes.Add(typeof(T));
	}

	public void LinkTo(NetworkedObject obj)
	{
		if (LinkedTo.Contains(obj)) return;

		InvalidateTimer();
		PendingDeletion = false;

		Root = obj.Root;
		LinkedTo.Add(obj);
		LinkCount++;

		obj.Deleted += () =>
		{
			UnlinkFrom(obj);
		};

		if (Root != null && Root.Network != null)
		{
			if (Root.Network.IsServer || !ExistInNetwork)
			{
				Name = ClassName;
				NetworkParent = Root.Assets;
			}
		}

	}

	public async void UnlinkFrom(NetworkedObject obj)
	{
		Root = obj.Root;
		LinkedTo.Remove(obj);
		LinkCount--;
		if (LinkedTo.Count == 0)
		{
			PendingDeletion = true;

			InvalidateTimer();

			if ((_timer == null || !Node.IsInstanceValid(_timer)) && Node.IsInstanceValid(GDNode))
			{
				_timer = new();
				GDNode.AddChild(_timer, @internal: Node.InternalMode.Back);
				_timer.OneShot = true;
				_timer.Timeout += DeleteTimerTimeout;
				_timer.Start(DeleteTimeoutSec);
			}
		}
	}

	private void InvalidateTimer()
	{
		if (_timer != null && Node.IsInstanceValid(_timer) && !_timer.IsQueuedForDeletion())
		{
			_timer.Stop();
			_timer.Timeout -= DeleteTimerTimeout;
			_timer.QueueFree();
		}
	}

	private void DeleteTimerTimeout()
	{
		if (PendingDeletion)
			Delete();
	}

	public static IReadOnlyList<Type> GetAllDerivedTypesOf(Type baseType)
	{
		if (!typeof(BaseAsset).IsAssignableFrom(baseType))
			throw new ArgumentException("Type must inherit from BaseAsset", nameof(baseType));

		return [.. _allDerivedTypes.Where(t => baseType.IsAssignableFrom(t))];
	}
}
