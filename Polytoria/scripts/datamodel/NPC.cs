// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Godot.Collections;
using Polytoria.Attributes;
using Polytoria.Client;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class NPC : Instance
{
	private const float NavigationDistance = 2f;
	public const float BodyRotateLerp = 10f;
	internal CharacterModel? _character;
	private Dynamic? _moveTarget;

	private string _displayName = "";
	private Node3D? _navAgentContainer;
	private NavigationAgent3D? _navAgent;
	private Physical? _grabbing;

	// Pending properties to apply to character
	private Color? _pendingHeadColor;
	private Color? _pendingTorsoColor;
	private Color? _pendingLeftArmColor;
	private Color? _pendingRightArmColor;
	private Color? _pendingLeftLegColor;
	private Color? _pendingRightLegColor;
	private int? _pendingFaceID;

	[ScriptProperty]
	public PTSignal<Physical> Grabbed { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<Physical> Ungrabbed { get; private set; } = new();

	[ScriptProperty]
	public Physical? Grabbing => _grabbing;

	public void SetGrabbing(Physical phy)
	{
		_grabbing = phy;
		Grabbed.Invoke(phy);
	}

	public void ReleaseGrabbing()
	{
		Ungrabbed.Invoke(_grabbing);
		_grabbing = null;
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character"), CloneIgnore]
	public Color HeadColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.HeadColor : _pendingHeadColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.HeadColor = value;
				_pendingHeadColor = null;
			}
			else
			{
				_pendingHeadColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color TorsoColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.TorsoColor : _pendingTorsoColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.TorsoColor = value;
				_pendingTorsoColor = null;
			}
			else
			{
				_pendingTorsoColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color LeftArmColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.LeftArmColor : _pendingLeftArmColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.LeftArmColor = value;
				_pendingLeftArmColor = null;
			}
			else
			{
				_pendingLeftArmColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color RightArmColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.RightArmColor : _pendingRightArmColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.RightArmColor = value;
				_pendingRightArmColor = null;
			}
			else
			{
				_pendingRightArmColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color LeftLegColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.LeftLegColor : _pendingLeftLegColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.LeftLegColor = value;
				_pendingLeftLegColor = null;
			}
			else
			{
				_pendingLeftLegColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color RightLegColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.RightLegColor : _pendingRightLegColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.RightLegColor = value;
				_pendingRightLegColor = null;
			}
			else
			{
				_pendingRightLegColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public int FaceID
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.FaceID : _pendingFaceID ?? 0;
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.FaceID = value;
				_pendingFaceID = null;
			}
			else
			{
				_pendingFaceID = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.SeatOffset instead"), CloneIgnore]
	public Vector3 SeatOffset
	{
		get => Character?.SeatOffset ?? Vector3.Zero;
		set
		{
			Character?.SeatOffset = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Health instead"), CloneIgnore]
	public float Health
	{
		get => Character?.Health ?? 0;
		set
		{
			Character?.Health = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.MaxHealth instead"), CloneIgnore]
	public float MaxHealth
	{
		get => Character?.MaxHealth ?? 0;
		set
		{
			Character?.MaxHealth = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.JumpPower instead"), CloneIgnore]
	public float JumpPower
	{
		get => Character?.JumpPower ?? 0;
		set
		{
			Character?.JumpPower = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.WalkSpeed instead"), CloneIgnore]
	public float WalkSpeed
	{
		get => Character?.WalkSpeed ?? 0;
		set
		{
			Character?.WalkSpeed = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.JumpSound instead"), CloneIgnore]
	public Sound? JumpSound
	{
		get => Character?.JumpSound;
		set
		{
			Character?.JumpSound = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.IsDead instead"), CloneIgnore]
	public bool IsDead
	{
		get => Character?.IsDead ?? true;
		set
		{
			Character?.IsDead = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.SittingIn instead"), CloneIgnore]
	public Seat? SittingIn
	{
		get => Character?.SittingIn;
		set
		{
			Character?.SittingIn = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.HoldingTool instead"), CloneIgnore]
	public Tool? HoldingTool
	{
		get => Character?.HoldingTool;
		set
		{
			Character?.HoldingTool = value;
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.Sit() instead")]
	public void Sit(Seat value)
	{
		Character?.Sit(value);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.Unsit() instead")]
	public void Unsit(bool value = true)
	{
		Character?.Unsit(value);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.EquipTool() instead")]
	public void EquipTool(Tool value)
	{
		Character?.EquipTool(value);
	}

	[ScriptProperty, Attributes.Obsolete("Use Character.CollisionLayers instead")]
	public uint CollisionLayers
	{
		get => Character?.CollisionLayers ?? 0;
		set
		{
			Character?.CollisionLayers = value;
		}
	}

	[ScriptProperty, Attributes.Obsolete("Use Character.CollisionMask instead")]
	public uint CollisionMask
	{
		get => Character?.CollisionMask ?? 0;
		set
		{
			Character?.CollisionMask = value;
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.SetCollisionLayer() instead")]
	public void SetCollisionLayer(int layer, bool value)
	{
		Character?.SetCollisionLayer(layer, value);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.SetCollisionMask() instead")]
	public void SetCollisionMask(int layer, bool value)
	{
		Character?.SetCollisionMask(layer, value);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.GetCollisionLayer() instead")]
	public bool GetCollisionLayer(int layer)
	{
		return Character?.GetCollisionLayer(layer) ?? false;
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.GetCollisionMask() instead")]
	public bool GetCollisionMask(int layer)
	{
		return Character?.GetCollisionMask(layer) ?? false;
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.GetTouching() instead")]
	public Physical[] GetTouching()
	{
		return Character?.GetTouching() ?? [];
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.AddForce() instead")]
	public void AddForce(Vector3 force, Physical.ForceModeEnum mode = Physical.ForceModeEnum.Force)
	{
		Character?.AddForce(force, mode);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.AddTorque() instead")]
	public void AddTorque(Vector3 force, Physical.ForceModeEnum mode = Physical.ForceModeEnum.Force)
	{
		Character?.AddTorque(force, mode);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.AddForceAtPosition() instead")]
	public void AddForceAtPosition(Vector3 force, Vector3 position, Physical.ForceModeEnum mode = Physical.ForceModeEnum.Force)
	{
		Character?.AddForceAtPosition(force, position, mode);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.AddRelativeForce() instead")]
	public void AddRelativeForce(Vector3 force, Physical.ForceModeEnum mode = Physical.ForceModeEnum.Force)
	{
		Character?.AddRelativeForce(force, mode);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.AddRelativeTorque() instead")]
	public void AddRelativeTorque(Vector3 torque, Physical.ForceModeEnum mode = Physical.ForceModeEnum.Force)
	{
		Character?.AddRelativeTorque(torque, mode);
	}

	[ScriptProperty, Attributes.Obsolete("Use Character.Forward instead")] public Vector3 Forward => Character?.Forward ?? Vector3.Zero;
	[ScriptProperty, Attributes.Obsolete("Use Character.Right instead")] public Vector3 Right => Character?.Right ?? Vector3.Zero;
	[ScriptProperty, Attributes.Obsolete("Use Character.Up instead")] public Vector3 Up => Character?.Up ?? Vector3.Zero;

	[ScriptMethod, Attributes.Obsolete("Use Character.GetBounds() instead")]
	public Aabb? GetBounds()
	{
		return Character?.GetBounds();
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.RotateAround() instead")]
	public void RotateAround(Vector3 point, Vector3 axis, float angle)
	{
		Character?.RotateAround(point, axis, angle);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.LookAt() instead")]
	public void LookAt(object target)
	{
		Character?.LookAt(target);
	}

	[ScriptMethod, Attributes.Obsolete("Use Character.LookAt() instead")]
	public void LookAt(object target, Vector3 up)
	{
		Character?.LookAt(target, up);
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.UseNametag instead"), CloneIgnore]
	public bool UseNametag
	{
		get => Character?.UseNametag ?? false;
		set
		{
			Character?.UseNametag = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.NametagOffset instead"), CloneIgnore]
	public Vector3 NametagOffset
	{
		get => Character?.NametagOffset ?? Vector3.Zero;
		set
		{
			Character?.NametagOffset = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.NametagVisibleRadius instead"), CloneIgnore]
	public float NametagVisibleRadius
	{
		get => Character?.NametagVisibleRadius ?? 0;
		set
		{
			Character?.NametagVisibleRadius = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.IsSitting instead"), CloneIgnore]
	public bool IsSitting
	{
		get => Character?.IsSitting ?? false;
		set
		{
			Character?.IsSitting = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.IsOnGround instead"), CloneIgnore]
	public bool IsOnGround => Character?.IsOnGround ?? false;


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.IsOnCeiling instead"), CloneIgnore]
	public bool IsOnCeiling => Character?.IsOnCeiling ?? false;


	[ScriptMethod, Attributes.Obsolete("Use Character.Kill() instead")]
	public void Kill()
	{
		Character?.Kill();
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.TryStepUp() instead")]
	public bool TryStepUp()
	{
		return Character?.TryStepUp() ?? false;
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.Jump() instead")]
	public void Jump()
	{
		Character?.Jump();
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.DropTool() instead")]
	public void DropTool()
	{
		Character?.DropTool();
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.LoadAppearance() instead, only if it's a PolytorianModel")]
	public void LoadAppearance(int value)
	{
		if (Character is PolytorianModel ptm)
		{
			ptm.LoadAppearance(value);
		}
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.ClearAppearance() instead, only if it's a PolytorianModel")]
	public void ClearAppearance()
	{
		if (Character is PolytorianModel ptm)
		{
			ptm.ClearAppearance();
		}
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.Respawn() instead")]
	public virtual void Respawn()
	{
		Character?.Respawn();
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.TakeDamage() instead")]
	public void TakeDamage(float value)
	{
		Character?.TakeDamage(value);
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.Heal() instead")]
	public void Heal(float value)
	{
		Character?.Heal(value);
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.Died instead")]
	public PTSignal? Died
	{
		get => Character?.Died;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.Landed instead")]
	public PTSignal? Landed
	{
		get => Character?.Landed;
		private set;
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Anchored instead"), CloneIgnore]
	public bool Anchored
	{
		get => Character?.Anchored ?? false;
		set
		{
			Character?.Anchored = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.CanCollide instead"), CloneIgnore]
	public bool CanCollide
	{
		get => Character?.CanCollide ?? false;
		set
		{
			Character?.CanCollide = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Velocity instead"), CloneIgnore]
	public Vector3 Velocity
	{
		get => Character?.Velocity ?? Vector3.Zero;
		set
		{
			Character?.Velocity = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.AngularVelocity instead"), CloneIgnore]
	public Vector3 AngularVelocity
	{
		get => Character?.AngularVelocity ?? Vector3.Zero;
		set
		{
			Character?.AngularVelocity = value;
		}
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.MovePosition() instead")]
	public void MovePosition(Vector3 value)
	{
		Character?.MovePosition(value);
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.MoveRotation() instead")]
	public void MoveRotation(Vector3 value)
	{
		Character?.MoveRotation(value);
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.Touched instead")]
	public PTSignal<Physical>? Touched
	{
		get => Character?.Touched;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.TouchEnded instead")]
	public PTSignal<Physical>? TouchEnded
	{
		get => Character?.TouchEnded;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.Clicked instead")]
	public PTSignal<Player>? Clicked
	{
		get => Character?.Clicked;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.MouseEnter instead")]
	public PTSignal? MouseEnter
	{
		get => Character?.MouseEnter;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.MouseExit instead")]
	public PTSignal? MouseExit
	{
		get => Character?.MouseExit;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.HealthChanged instead")]
	public PTSignal<float, float> HealthChanged
	{
		get => Character?.HealthChanged;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.Jumped instead")]
	public PTSignal Jumped
	{
		get => Character?.Jumped;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.LeftGround instead")]
	public PTSignal LeftGround
	{
		get => Character?.LeftGround;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.Seated instead")]
	public PTSignal<Seat> Seated
	{
		get => Character?.Seated;
		private set;
	}


	[ScriptProperty, Attributes.Obsolete("Use Character.Unseated instead")]
	public PTSignal<Seat> Unseated
	{
		get => Character?.Unseated;
		private set;
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Position instead"), CloneIgnore]
	public Vector3 Position
	{
		get => Character?.Position ?? Vector3.Zero;
		set
		{
			Character?.Position = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Rotation instead"), CloneIgnore]
	public Vector3 Rotation
	{
		get => Character?.Rotation ?? Vector3.Zero;
		set
		{
			Character?.Rotation = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Size instead"), CloneIgnore]
	public Vector3 Size
	{
		get => Character?.Size ?? Vector3.Zero;
		set
		{
			Character?.Size = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.LocalPosition instead"), CloneIgnore]
	public Vector3 LocalPosition
	{
		get => Character?.LocalPosition ?? Vector3.Zero;
		set
		{
			Character?.LocalPosition = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.LocalRotation instead"), CloneIgnore]
	public Vector3 LocalRotation
	{
		get => Character?.LocalRotation ?? Vector3.Zero;
		set
		{
			Character?.LocalRotation = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.LocalSize instead"), CloneIgnore]
	public Vector3 LocalSize
	{
		get => Character?.LocalSize ?? Vector3.Zero;
		set
		{
			Character?.LocalSize = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Quaternion instead"), CloneIgnore]
	public Quaternion Quaternion
	{
		get => Character?.Quaternion ?? Godot.Quaternion.Identity;
		set
		{
			Character?.Quaternion = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.LocalQuaternion instead"), CloneIgnore]
	public Quaternion LocalQuaternion
	{
		get => Character?.LocalQuaternion ?? Godot.Quaternion.Identity;
		set
		{
			Character?.LocalQuaternion = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Locked instead"), CloneIgnore]
	public bool Locked
	{
		get => Character?.Locked ?? false;
		set
		{
			Character?.Locked = value;
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.IsClimbing instead"), CloneIgnore]
	public bool IsClimbing => Character?.IsClimbing ?? false;

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.ClimbingTruss instead"), CloneIgnore]
	public Truss? ClimbingTruss => Character?.ClimbingTruss;

	[ScriptMethod, Attributes.Obsolete("Use Character.Translate() instead")]
	public void Translate(Vector3 value)
	{
		Character?.Translate(value);
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.Rotate() instead")]
	public void Rotate(Vector3 value)
	{
		Character?.Rotate(value);
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.CanMove instead"), CloneIgnore]
	public bool CanMove
	{
		get => Character?.CanMove ?? false;
		set
		{
			Character?.CanMove = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.SprintSpeed instead"), CloneIgnore]
	public float SprintSpeed
	{
		get => Character?.SprintSpeed ?? 0;
		set
		{
			Character?.SprintSpeed = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.Stamina instead"), CloneIgnore]
	public float Stamina
	{
		get => Character?.Stamina ?? 0;
		set
		{
			Character?.Stamina = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.MaxStamina instead"), CloneIgnore]
	public float MaxStamina
	{
		get => Character?.MaxStamina ?? 0;
		set
		{
			Character?.MaxStamina = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.UseStamina instead"), CloneIgnore]
	public bool UseStamina
	{
		get => Character?.UseStamina ?? false;
		set
		{
			Character?.UseStamina = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.StaminaRegen instead"), CloneIgnore]
	public float StaminaRegen
	{
		get => Character?.StaminaRegen ?? 0;
		set
		{
			Character?.StaminaRegen = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.StaminaBurn instead"), CloneIgnore]
	public float StaminaBurn
	{
		get => Character?.StaminaBurn ?? 0;
		set
		{
			Character?.StaminaBurn = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.KeepInventory instead"), CloneIgnore]
	public bool KeepInventory
	{
		get => Character?.KeepInventory ?? false;
		set
		{
			Character?.KeepInventory = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.UseHeadTurning instead"), CloneIgnore]
	public bool UseHeadTurning
	{
		get => Character?.UseHeadTurning ?? false;
		set
		{
			Character?.UseHeadTurning = value;
		}
	}


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Character.AllowAnimationWhileMoving instead"), CloneIgnore]
	public bool AllowAnimationWhileMoving
	{
		get => Character?.AllowAnimationWhileMoving ?? false;
		set
		{
			Character?.AllowAnimationWhileMoving = value;
		}
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.UnequipTool() instead")]
	public void UnequipTool()
	{
		Character?.UnequipTool();
	}


	[ScriptMethod, Attributes.Obsolete("Use Character.ResetAppearance() instead, only if it's a PolytorianModel")]
	public void ResetAppearance()
	{
		if (Character is PolytorianModel ptm)
		{
			ptm.ResetAppearance();
		}
	}

	[Editable, ScriptProperty]
	public string DisplayName
	{
		get => _displayName;
		set
		{
			_displayName = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public CharacterModel? Character
	{
		get
		{
			if (_character != null && _character.IsDeleted)
			{
				_character = null;
			}
			return _character;
		}
		set
		{
			bool isServer = Root.Network.IsServer;
			CharacterModel? oldChar = Character;
			if (isServer)
			{
				if (this is Player plr)
				{
					oldChar?.SetNetworkAuthority(null);
					oldChar?.SetNetworkAuthority(1, true);
					value?.SetNetworkAuthority(plr);
					value?.SetNetworkAuthority(plr.PeerID, true);
					value?.SetPhysicsProcess(true);
				}
				else if (((value?.NetworkAuthority ?? 1) != 1))
				{
					value?.SetNetworkAuthority(null);
					value?.SetNetworkAuthority(1, true);
				}
			}
			Character?._controller = null;
			Character?.OnPropertyChanged();
			_character = value;
			OnPropertyChanged();
			value?.Controller?._character = null;
			value?.Controller?.OnPropertyChanged();
			value?._controller = this;
			value?.OnPropertyChanged();
			if (this is Player mplr)
			{
				mplr.CharacterChanged.Invoke(oldChar);
			}
		}
	}

	[SyncVar, ScriptProperty]
	public Dynamic? MoveTarget
	{
		get
		{
			if (_moveTarget != null && _moveTarget.IsDeleted)
			{
				_moveTarget = null;
			}
			return _moveTarget;
		}
		set => _moveTarget = value;
	}

	[ScriptProperty] public float NavDestinationDistance => _navAgent == null ? Mathf.Inf : _navAgent.DistanceToTarget();

	[ScriptProperty]
	public bool NavDestinationReached { get; private set; } = false;

	[ScriptProperty] public bool NavDestinationValid => _navAgent != null && _navAgent.IsTargetReachable();

	[ScriptProperty]
	public PTSignal NavFinished { get; private set; } = new();

	public override void PreDelete()
	{
		_navAgent?.NavigationFinished -= OnNavFinished;
		base.PreDelete();
	}

	public void Navigate(double delta)
	{
		if (Character == null) return;

		bool isOnFloor = Character.IsOnGround;
		bool isOnCeiling = Character.IsOnCeiling;
		bool playerNPCOverride = Character != null && !Character.CanMove;

		CharacterModel.CharacterModelStateEnum finalState = CharacterModel.CharacterModelStateEnum.Idle;
		Vector3? walkTarget = null;
		float animSpeed = 1;

		if (MoveTarget != null)
		{
			walkTarget = MoveTarget.GetGlobalPosition();
		}

		if (_navAgent != null)
		{
			walkTarget = _navAgent.GetNextPathPosition();

			// Adjust Nav agent position in-case of unstable Y position changes
			_navAgentContainer?.GlobalPosition = _navAgentContainer.GlobalPosition with { Y = walkTarget.Value.Y };
		}

		if (walkTarget.HasValue)
		{
			Vector3 velo = Character!.GetGlobalPosition().DirectionTo(walkTarget.Value with { Y = Character.Position.Y });
			Character.CharacterVelocity = new(velo.X * Character.WalkSpeed, Character.CharacterVelocity.Y, velo.Z * Character.WalkSpeed);
			Character.GDNode3D.GlobalRotationDegrees = new Vector3(Character.Rotation.X, Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(Character.Rotation.Y), Mathf.Atan2(Character.CharacterVelocity.X, Character.CharacterVelocity.Z), MathUtils.ExpDecay((float)delta, BodyRotateLerp))), Character.Rotation.Z);

			float distanceToTarget = Character.GetGlobalPosition().DistanceTo(walkTarget.Value);

			if (distanceToTarget > 0.5f)
			{
				finalState = CharacterModel.CharacterModelStateEnum.Walking;
				animSpeed = Character.WalkSpeed / 8;
				Character.TryStepUp();
			}
		}
		else if (this is not Player || playerNPCOverride)
		{
			Character!.CharacterVelocity = new(0, Character.CharacterVelocity.Y, 0);
		}

		if (!isOnFloor)
		{
			finalState = CharacterModel.CharacterModelStateEnum.Jumping;
		}

		if (this is not Player || playerNPCOverride)
		{
			Character!.SetState(finalState);
			Character!.SetAnimSpeed(animSpeed);
		}
	}

	public override void Ready()
	{
		if (Root.IsLegacyWorld && Character == null && !PendingProps.Contains(nameof(Character)))
		{
			// Create default character on legacy world. If character is not set
			Root.Insert.InitializeDefaultNPC(this);

			if (Character is PolytorianModel polytorian)
			{
				if (_pendingHeadColor.HasValue)
				{
					polytorian.HeadColor = _pendingHeadColor.Value;
					_pendingHeadColor = null;
				}
				if (_pendingTorsoColor.HasValue)
				{
					polytorian.TorsoColor = _pendingTorsoColor.Value;
					_pendingTorsoColor = null;
				}
				if (_pendingLeftArmColor.HasValue)
				{
					polytorian.LeftArmColor = _pendingLeftArmColor.Value;
					_pendingLeftArmColor = null;
				}
				if (_pendingRightArmColor.HasValue)
				{
					polytorian.RightArmColor = _pendingRightArmColor.Value;
					_pendingRightArmColor = null;
				}
				if (_pendingLeftLegColor.HasValue)
				{
					polytorian.LeftLegColor = _pendingLeftLegColor.Value;
					_pendingLeftLegColor = null;
				}
				if (_pendingRightLegColor.HasValue)
				{
					polytorian.RightLegColor = _pendingRightLegColor.Value;
					_pendingRightLegColor = null;
				}
				if (_pendingFaceID.HasValue)
				{
					polytorian.FaceID = _pendingFaceID.Value;
					_pendingFaceID = null;
				}
			}
		}

		if (Character != null) Character.Ready();
		base.Ready();
	}
	[ScriptMethod]
	public void SetNavDestination(Vector3 pos)
	{
		MoveTarget = null;
		if (Character == null) return;
		if (_navAgent == null)
		{
			_navAgentContainer = new();
			_navAgent = new()
			{
				PathDesiredDistance = NavigationDistance,
				TargetDesiredDistance = 0.5f,
				PathHeightOffset = -(Character.CalculateBounds().Size.Y / 2),
				PathMaxDistance = 3f
			};

			_navAgentContainer.AddChild(_navAgent);
			Character.GDNode3D.AddChild(_navAgentContainer);
			if (Globals.IsInGDEditor)
			{
				_navAgent.DebugEnabled = true;
			}

			_navAgent.NavigationFinished += OnNavFinished;
			NavDestinationReached = false;
		}
		_navAgent.TargetPosition = pos;
	}

	private void OnNavFinished()
	{
		_navAgentContainer?.QueueFree();
		_navAgent = null;
		NavDestinationReached = true;
		NavFinished.Invoke();
	}
}
