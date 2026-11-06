// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Client.Networking;

internal sealed class AutoOwnership
{
	private const float OwnershipRadius = 100;
	private const float OwnershipKeepRadius = OwnershipRadius * 1.2f;
	private const float OwnershipSwitchRatio = 0.75f;
	private const double OwnershipMinHold = 1;
	private const double ServerPushHold = 5;
	private const float OwnershipRestRadius = 20;
	private static readonly int MaxAutoOwned = (NetworkTransformSync.UploadBudgetBytes - TransformPacket.SnapshotHeaderSize) /
		TransformPacket.SnapshotEntrySize * 3 / 4;

	private readonly HashSet<Physical> _ownables = [];
	private readonly HashSet<RigidBody> _autoOwnables = [];
	private readonly List<(Physical Physical, int Owner)> _ownerChanges = [];
	private readonly Dictionary<int, int> _autoOwned = [];

	public void Add(Physical physical)
	{
		_ownables.Add(physical);
		Update(physical);
	}

	public void Update(Physical physical)
	{
		if (physical is not RigidBody body) return;
		if (!physical.Anchored && _ownables.Contains(physical))
		{
			_autoOwnables.Add(body);
		}
		else
		{
			_autoOwnables.Remove(body);
		}
	}

	public void Remove(Physical physical)
	{
		_ownables.Remove(physical);
		if (physical is RigidBody body) _autoOwnables.Remove(body);
	}

	public void ForgetPeer(int peerID)
	{
		foreach (Physical physical in _ownables.ToArray())
		{
			if (physical.NetTransformAuthority == peerID && physical is not Player)
			{
				physical.NetworkOwnershipAuto = true;
				physical.AssignNetworkOwner(1);
			}
		}
	}

	public void UpdateOwners(World root)
	{
		bool auto = root.Players.AutoNetworkOwnership;
		Player[] players = auto ? root.Players.GetPlayers() : [];
		double now = World.LocalClock;
		var owners = new (int PeerID, Vector3 Position)[players.Length];
		for (int i = 0; i < players.Length; i++)
		{
			owners[i] = (players[i].PeerID, players[i].GetGlobalPosition());
		}

		_autoOwned.Clear();
		foreach (RigidBody body in _autoOwnables)
		{
			if (body.NetworkOwnershipAuto && body.NetTransformAuthority != 1)
			{
				_autoOwned[body.NetTransformAuthority] = _autoOwned.GetValueOrDefault(body.NetTransformAuthority) + 1;
			}
		}

		foreach (RigidBody body in _autoOwnables)
		{
			if (!body.NetworkOwnershipAuto || body.Anchored || body.PhysicalRoot != null || body.IsHidden || !body.ExistInNetwork) continue;
			if (now - body.NetTransformAuthorityChangedAt < OwnershipMinHold || now - body.LastServerPush < ServerPushHold) continue;
			if (body.HasConstantForce) continue;

			Vector3 position = body.GetGlobalPosition();
			int owner = 1;
			float ownerDistance = OwnershipRadius;
			float currentDistance = float.MaxValue;
			foreach (var p in owners)
			{
				float distance = p.Position.DistanceTo(position);
				if (p.PeerID == body.NetTransformAuthority)
				{
					currentDistance = distance;
				}
				if (distance < ownerDistance && (p.PeerID == body.NetTransformAuthority || _autoOwned.GetValueOrDefault(p.PeerID) < MaxAutoOwned))
				{
					owner = p.PeerID;
					ownerDistance = distance;
				}
			}

			if (currentDistance < OwnershipKeepRadius && ownerDistance > currentDistance * OwnershipSwitchRatio)
			{
				owner = body.NetTransformAuthority;
			}
			if (owner != body.NetTransformAuthority &&
				(!auto || ownerDistance < OwnershipRestRadius || !(body.NetTransformAuthority == 1 ? body.IsAsleep : body.IsNetResting)))
			{
				_ownerChanges.Add((body, owner));
				if (body.NetTransformAuthority != 1) _autoOwned[body.NetTransformAuthority]--;
				if (owner != 1) _autoOwned[owner] = _autoOwned.GetValueOrDefault(owner) + 1;
			}
		}

		foreach (var change in _ownerChanges)
		{
			change.Physical.AssignNetworkOwner(change.Owner);
		}
		_ownerChanges.Clear();
	}
}
