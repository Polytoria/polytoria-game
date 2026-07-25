// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Physics;
using Polytoria.Scripting;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Explosion : Dynamic
{
	private const float ExplosionParticleTimeSec = 10f;

	private GpuParticles3D _particle = null!;
	private float _radius = 10;
	private float _force = 5000;
	private bool _affectAnchored = false;
	private float _damage = 100000;
	private bool _affectWelds;

	[Editable, ScriptProperty]
	public float Radius
	{
		get => _radius;
		set
		{
			_radius = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Force
	{
		get => _force;
		set
		{
			_force = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AffectAnchored
	{
		get => _affectAnchored;
		set
		{
			_affectAnchored = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Damage
	{
		get => _damage;
		set
		{
			_damage = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AffectWelds
	{
		get => _affectWelds;
		set
		{
			_affectWelds = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public PTFunction? AffectPredicate { get; set; }

	[ScriptProperty] public PTSignal<Instance> Touched { get; private set; } = new();

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void Init()
	{
		base.Init();
		_particle = GDNode.GetNode<GpuParticles3D>("Particles");
		_particle.Visible = false;
	}

	public override void Ready()
	{
		TryIgnite();
		base.Ready();
	}

	public override void EnterTree()
	{
		TryIgnite();
		base.EnterTree();
	}

	private async void TryIgnite()
	{
		if (!IsNetworkReady || IsHidden) return;
		_particle.Scale = Vector3.One * _radius / 15;
		_particle.Visible = true;
		_particle.Emitting = true;

		BuiltInAudioAsset audio = New<BuiltInAudioAsset>();
		audio.AudioPreset = BuiltInAudioAsset.BuiltInAudioPresetEnum.Explosion;

		Sound? s = null;

		if (!Root.Network.IsServer)
		{
			s = New<Sound>();
			s.Audio = audio;
			s.PlayInWorld = true;
			s.Parent = this;
			s.LocalPosition = Vector3.Zero;
		}

		HashSet<Instance> processed = [];
		HashSet<RigidBody> toCheck = new(ReferenceEqualityComparer.Instance);
		HashSet<Weld> breakWelds = new(ReferenceEqualityComparer.Instance);
		List<Entity> forceTargets = [];
		List<Player> playerTargets = [];

		Instance[] overlaps = Root.Environment.OverlapSphere(Position, Radius);

		foreach (Instance item in overlaps)
		{
			if (!processed.Add(item))
			{
				continue;
			}

			Touched.Invoke(item);

			if (AffectPredicate != null)
			{
				object?[] res = await AffectPredicate.Call(item);
				if (!(res.Length == 1 && res[0] is bool b && b))
				{
					continue;
				}
			}

			if (item is RigidBody e && !item.IsDescendantOfClass("Accessory"))
			{
				bool skipForce = e.Anchored && !AffectAnchored && AffectPredicate == null;

				if (_affectWelds && item is RigidBody p)
				{
					if (p.Assembly != null && p.Assembly.Physicalized)
					{
						foreach (RigidBody assemblyPart in p.Assembly.Parts)
						{
							toCheck.Add(assemblyPart);
						}
					}
					else
					{
						toCheck.Add(p);
					}
				}

				if (skipForce)
				{
					continue;
				}

				RigidBody3D body = e.GDRigidBody;
				Vector3 direction = body.GlobalTransform.Origin - GetGlobalTransform().Origin;
				float distance = direction.Length();
				bool unanchor = true;

				direction = direction.Normalized();

				if ((e.Size.X > Radius * 1.3 || e.Size.Y > Radius * 1.3 || e.Size.Z > Radius * 1.3) && AffectPredicate == null)
				{
					unanchor = false;
				}

				if (unanchor)
				{
					e.Anchored = false;
				}

				float forceMagnitude = Force * (1 - (distance / Radius));
				Vector3 force = direction * forceMagnitude / 100;

				e.ApplyAddForce(force);

			}
			else if (item is Player plr)
			{
				if (plr.IsDead) continue;

				plr.TakeDamage(Damage);
				AddPlrExplosionForce(plr);
			}
		}

		if (_affectWelds)
		{
			foreach (RigidBody part in toCheck)
			{
				foreach (Weld weld in WeldGraph.GetWelds(part).ToArray())
				{
					if (!WeldGraph.TryGetParts(weld, out RigidBody a, out RigidBody b))
					{
						continue;
					}

					if (IsWeldAffected(a, b))
					{
						breakWelds.Add(weld);
					}
				}
			}

			WeldAssemblyManager.BreakWelds(breakWelds);
		}

		// Play sound on next frame, needed to be loaded
		if (s != null)
		{
			Callable.From(s.Play).CallDeferred();
		}

		await Globals.Singleton.WaitAsync(ExplosionParticleTimeSec);

		Delete();
	}

	private bool IsWeldAffected(RigidBody a, RigidBody b)
	{
		Aabb aa = a.GetSelfBound();
		Aabb bb = b.GetSelfBound();

		Vector3 point = Position;

		Vector3 closestOnContact = new(
			ClosestContactAxis(point.X, aa.Position.X, aa.End.X, bb.Position.X, bb.End.X),
			ClosestContactAxis(point.Y, aa.Position.Y, aa.End.Y, bb.Position.Y, bb.End.Y),
			ClosestContactAxis(point.Z, aa.Position.Z, aa.End.Z, bb.Position.Z, bb.End.Z)
		);

		return closestOnContact.DistanceTo(point) <= Radius;
	}

	private static float ClosestContactAxis(float point, float aMin, float aMax, float bMin, float bMax)
	{
		if (aMax < bMin)
		{
			return (aMax + bMin) * 0.5f;
		}

		if (bMax < aMin)
		{
			return (bMax + aMin) * 0.5f;
		}

		float overlapMin = Mathf.Max(aMin, bMin);
		float overlapMax = Mathf.Min(aMax, bMax);

		return Mathf.Clamp(point, overlapMin, overlapMax);
	}

	private void AddPlrExplosionForce(Player player)
	{
		float force = Force * 0.02f;
		Vector3 dir = player.GetGlobalTransform().Origin - GetGlobalTransform().Origin;
		float wearoff = 1 - (dir.Length() / (Radius * 2f));
		wearoff = Mathf.Max(Mathf.Clamp(wearoff, 0, 1), 0.1f);
		Vector3 f = dir.Normalized() * force;
		f.X *= 1.5f;
		f.Z *= 1.5f;

		player.CharacterVelocity = f * wearoff;
	}
}
