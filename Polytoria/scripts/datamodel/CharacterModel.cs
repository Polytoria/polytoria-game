// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Networking;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class CharacterModel : Dynamic
{
	private static readonly int BlendCount = Enum.GetValues<CharacterModelBlendEnum>().Length;

	private CharacterModelStateEnum _currentState = CharacterModelStateEnum.Idle;
	private float _currentSpeed = 1;
	private Animator? _animator = null;

	[ScriptEnum]
	public enum CharacterModelStateEnum
	{
		Idle,
		Walking,
		Running,
		Jumping,
		Climbing
	}

	public enum CharacterModelBlendEnum
	{
		Sitting,
		ToolHoldLeft,
		ToolHoldRight,
		LookX,
		LookY,
	}

	[ScriptProperty, SyncVar(AllowAuthorWrite = true)]
	public CharacterModelStateEnum CurrentState
	{
		get => _currentState;
		set
		{
			_currentState = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty, SyncVar(AllowAuthorWrite = true, Unreliable = true)]
	public float CurrentSpeed
	{
		get => _currentSpeed;
		set
		{
			_currentSpeed = value;
			RecvSpeedValue(value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Animator? Animator
	{
		get
		{
			if (_animator != null && _animator.IsDeleted)
			{
				_animator = null;
			}
			return _animator;
		}
		set => _animator = value;
	}

	public override void Init()
	{
		base.Init();
		// if (HasAuthority)
		// {
		// 	Root.Network.PeerPreInit += OnPeerPreInit;
		// }
	}

	public override void PreDelete()
	{
		// if (HasAuthority)
		// {
		// 	Root.Network.PeerPreInit -= OnPeerPreInit;
		// }
		base.PreDelete();
	}

	// private void OnPeerPreInit(int id)
	// {
	// 	for (int i = 0; i < BlendCount; i++)
	// 	{
	// 		RpcId(id, nameof(NetSetBlendValue), i, BlendValues[i]);
	// 	}
	// }

	[ScriptMethod]
	public void PlayIdle()
	{
		SetState(CharacterModelStateEnum.Idle);
	}

	[ScriptMethod]
	public void PlayWalk()
	{
		SetState(CharacterModelStateEnum.Walking);
	}

	[ScriptMethod]
	public void PlayRun()
	{
		SetState(CharacterModelStateEnum.Running);
	}

	[ScriptMethod]
	public void PlayJump()
	{
		SetState(CharacterModelStateEnum.Jumping);
	}

	[ScriptMethod]
	public void PlayClimb()
	{
		SetState(CharacterModelStateEnum.Climbing);
	}

	[ScriptMethod]
	public void SetAnimSpeed(float speed)
	{
		CurrentSpeed = speed;
	}

	[ScriptMethod]
	public void SetState(CharacterModelStateEnum newState)
	{
		if (newState != CurrentState)
		{
			CurrentState = newState;
		}
	}

	public void SetBlendValue(CharacterModelBlendEnum blend, float value)
	{
		int blendId = (int)blend;
		InternalSetBlendValue(blendId, value);
		if (HasAuthority)
		{
			Rpc(nameof(NetSetBlendValue), blendId, value);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSetBlendValue(int blendId, float value)
	{
		if (blendId >= 0 && blendId < BlendCount)
		{
			InternalSetBlendValue(blendId, value);
		}
	}

	private void InternalSetBlendValue(int blendId, float value)
	{
		RecvBlendValue((CharacterModelBlendEnum)blendId, value);
	}

	public virtual void RecvBlendValue(CharacterModelBlendEnum blendName, float blendValue) { }
	public virtual void RecvSpeedValue(float speedValue) { }

	[ScriptMethod]
	public virtual Dynamic GetAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		throw new NotImplementedException();
	}

	public virtual void ApplyCameraModifier(Camera camera) { }

	[ScriptEnum]
	public enum CharacterAttachmentEnum
	{
		Head,
		UpperTorso,
		LowerTorso,
		ShoulderLeft,
		ShoulderRight,
		ElbowLeft,
		ElbowRight,
		HandLeft,
		HandRight,
		LegLeft,
		LegRight,
		KneeLeft,
		KneeRight,
		FootLeft,
		FootRight
	}
}
