// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Datamodel.Services;
using Polytoria.Networking;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class CharacterModel : Dynamic
{
	private CharacterModelStateEnum _currentState = CharacterModelStateEnum.Idle;
	private float _currentSpeed = 1;
	private readonly Dictionary<CharacterModelBlendEnum, float> _blendValues = [];
	private Animator? _animator = null!;

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
				_animator = null!;
			}
			return _animator;
		}
		set => _animator = value;
	}

	private bool _peerReadySubscribed = false;

	public override void Init()
	{
		base.Init();
		if (Root != null && Root.Network != null && NetworkService.CheckAuthority(Root.Network.LocalPeerID, NetworkAuthority))
		{
			_peerReadySubscribed = true;
			Root.Network.PeerPreInit += OnPeerPreInit;
		}
	}

	public override void PreDelete()
	{
		if (_peerReadySubscribed)
		{
			Root.Network.PeerPreInit -= OnPeerPreInit;
		}
		base.PreDelete();
	}

	private void OnPeerPreInit(int id)
	{
		foreach ((CharacterModelBlendEnum blend, float val) in _blendValues)
		{
			RpcId(id, nameof(NetSetBlendValue), (int)blend, val);
		}
	}

	public void PlayIdle()
	{
		SetState(CharacterModelStateEnum.Idle);
	}

	public void PlayWalk()
	{
		SetState(CharacterModelStateEnum.Walking);
	}

	public void PlayRun()
	{
		SetState(CharacterModelStateEnum.Running);
	}

	public void PlayJump()
	{
		SetState(CharacterModelStateEnum.Jumping);
	}

	public void PlayClimb()
	{
		SetState(CharacterModelStateEnum.Climbing);
	}

	public void SetAnimSpeed(float speed)
	{
		CurrentSpeed = speed;
	}

	public void SetState(CharacterModelStateEnum newState)
	{
		if (newState != CurrentState)
		{
			CurrentState = newState;
		}
	}

	public void SetBlendValue(CharacterModelBlendEnum blend, float value)
	{
		InternalSetBlendValue(blend, value);
		if (HasAuthority)
		{
			Rpc(nameof(NetSetBlendValue), (int)blend, value);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSetBlendValue(int blendName, float blendValue)
	{
		InternalSetBlendValue((CharacterModelBlendEnum)blendName, blendValue);
	}

	private void InternalSetBlendValue(CharacterModelBlendEnum blendName, float blendValue)
	{
		_blendValues[blendName] = blendValue;
		RecvBlendValue(blendName, blendValue);
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
