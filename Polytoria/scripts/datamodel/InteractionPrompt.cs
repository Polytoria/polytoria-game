// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Godot;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;


namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class InteractionPrompt : Physical
{
	public const string PromptScenePath = "res://scenes/datamodel/InteractionPrompt.tscn";

	private Physical? _parent = null!;

	private bool _hiddenByServer = false;

	private bool _enabled = true;
	private float _maxDistance = 10.0f;
	private bool _requireFacing = true;
	private float _losThreshold = 0.5f;
	private float _activationTime = 0.5f;
	private string _title = "Interact";
	private string _subtitle = "[color=#a3a3a3]Subtitle[/color]";


	private float _scale = 1f;

	private RichTextLabel _titleNode = null!;
	private RichTextLabel _subtitleNode = null!;

	private bool _useParentForVisibility = false;

	private Node3D _prompt = null!;
	private AnimationPlayer _animPlayer = null!;
	private TextureProgressBar _progressBar = null!;

	private float _progress = 0.0f;

	private bool _inRange = false;
	private bool _isMouseOverParent = false;
	private float _timeSpentActivating = 0.0f;

	private List<Player> _hiddenFor = [];

	private bool _hideByDefault = false;

	private UIModeEnum _uiMode = UIModeEnum.Default;

	private List<GUI3D> _gui3Ds = new();

	[Editable, ScriptProperty]
	public UIModeEnum UIMode
	{
		get => _uiMode;
		set
		{
			_uiMode = value;
			_prompt.GetNode<Sprite3D>("Sprite3D").Visible = _uiMode == UIModeEnum.Default;
			_gui3Ds.Clear();
			foreach (var child in GetChildren())
			{
				if (child is GUI3D)
				{
					_gui3Ds.Add((GUI3D)child);
				}
			}
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool HideByDefault
	{
		get => _hideByDefault;
		set
		{
			_hideByDefault = value;
			_hiddenByServer = _hideByDefault;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue("Interact")]
	public string Title
	{
		get => _title;
		set
		{
			_title = value;
			_titleNode.Text = _title;
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty, DefaultValue("[color=#a3a3a3]Subtitle[/color]")]
	public string Subtitle
	{
		get => _subtitle;
		set
		{
			_subtitle = value;
			_subtitleNode.Text = _subtitle;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Scale
	{
		get => _scale;
		set
		{
			_scale = value;
			_prompt.Scale = new Vector3(_scale, _scale, _scale);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(10.0f)]
	public float MaxDistance
	{
		get => _maxDistance;
		set
		{
			_maxDistance = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float LineOfSightThreshold
	{
		get => _losThreshold;
		set
		{
			_losThreshold = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float ActivationTime
	{
		get => _activationTime;
		set
		{
			_activationTime = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool UseParentForVisibility
	{
		get => _useParentForVisibility;
		set
		{
			_useParentForVisibility = value;
			OnPropertyChanged();
		}
	}



	[Editable, ScriptProperty, DefaultValue(true)]
	public bool RequireFacing
	{
		get => _requireFacing;
		set
		{
			_requireFacing = value;
			OnPropertyChanged();
		}
	}


	[ScriptProperty]
	public Player[] HiddenFor
	{
		get => _hiddenFor.ToArray();
		set
		{
			_hiddenFor = [.. value];
			OnPropertyChanged();
		}
	}

	[ScriptProperty, NoSync]
	public float Progress
	{
		get => _progress;
		set
		{
			_progress = value;
			//OnPropertyChanged();
		}
	}


	public bool CheckCanInteract()
	{
		if (_hiddenByServer)
		{
			return false;
		}
		if (_inRange)
		{
			if (_requireFacing)
			{
				return IsFacingPrompt();
			}
			else
			{
				return true;
			}
		}
		return false;
	}

	public bool IsFacingPrompt()
	{
		if (Root.Environment.CurrentCamera == null)
		{
			return false;
		}
		var playerTransform = Root.Environment.CurrentCamera.GetGlobalTransform();
		var targetF = -playerTransform.Basis.Z;
		var direction = (_prompt.GlobalTransform.Origin - playerTransform.Origin).Normalized();
		return targetF.Dot(direction) >= _losThreshold;
	}


	public void OnMouseEnterParent()
	{
		_isMouseOverParent = true;
	}

	public void OnMouseExitParent()
	{
		_isMouseOverParent = false;
	}

	public override void EnterTree()
	{
		if (Parent is Physical phy)
		{
			_parent = phy;
			phy.MouseEnter.Connect(OnMouseEnterParent);
			phy.MouseExit.Connect(OnMouseExitParent);
		}
		base.EnterTree();
	}

	public override void ExitTree()
	{
		_parent?.MouseEnter.Disconnect(OnMouseEnterParent);
		_parent?.MouseExit.Disconnect(OnMouseExitParent);
		_parent = null;
		base.ExitTree();
	}

	public override Node CreateGDNode()
	{
		_prompt = Globals.CreateInstanceFromScene<Node3D>(PromptScenePath);
		_animPlayer = _prompt.GetNode<AnimationPlayer>("AnimPlay");
		_progressBar = _prompt.GetNode<TextureProgressBar>("SV/Control/Pivot/Key/TextureProgressBar");
		return _prompt;
	}

	public override void Init()
	{
		_titleNode = _prompt.GetNode<RichTextLabel>("SV/Control/Pivot/Text/Layout/Title");
		_subtitleNode = _prompt.GetNode<RichTextLabel>("SV/Control/Pivot/Text/Layout/Subtitle");
		base.Init();
		_prompt.Scale = new Vector3(_scale, _scale, _scale);
		SetProcess(true); // played around with this alot, process seems to work better in general I've found
	}

	public override void Process(double delta)
	{
		_prompt.Scale = new Vector3(_scale, _scale, _scale);
		if (Root.SessionType != World.SessionTypeEnum.Client) { return; }
		if (!Root.IsLoaded) return;
		var distance = Root.Players.LocalPlayer?.GetGlobalPosition().DistanceTo(GetGlobalPosition());
		_inRange = distance <= _maxDistance;
		_prompt.Visible = false;
		if (_inRange && _enabled && !_hiddenByServer)
		{
			if (_useParentForVisibility)
			{
				if (_isMouseOverParent)
				{
					_prompt.Visible = true;
				}
			}
			else
			{
				_prompt.Visible = true;
			}
		}
		if (_uiMode == UIModeEnum.GUI3D)
		{
			foreach (var gui3d in _gui3Ds)
			{
				((Node3D)gui3d.GDNode).Visible = _prompt.Visible;
			}
		}

		base.Process(delta);

		if (_hiddenByServer)
		{
			return;
		}

		if (Input.IsActionPressed("interact"))
		{
			if (CheckCanInteract() && _enabled)
			{
				if (_timeSpentActivating == 0.0f)
				{
					_animPlayer.Play("InputStart");
				}
				_timeSpentActivating += (float)delta;
			}
			if (_timeSpentActivating >= _activationTime)
			{
				_timeSpentActivating = 0.0f;
				_animPlayer.Play("InputEnd");
				Interacted.Invoke(Root.Players.LocalPlayer);
				RpcId(1, nameof(TriggerInteracted));
				if (_activationTime <= 0.1)
				{
					// Kind-of weird, silly solution, but this prevents the server from being spammed with requests from a client if the activation time is instant
					// Hate it? Blame JewelEyed <3
					if (Root.Players.LocalPlayer != null)
					{
						_timeSpentActivating = -Math.Max((Root.Players.LocalPlayer.NetworkPing / 1000f), 0.1f);
					}
				}
			}
		}
		else
		{
			if (_timeSpentActivating > 0.0f)
			{
				_timeSpentActivating = 0.0f;
				_animPlayer.Play("InputEnd");
			}

		}
		_progress = (_timeSpentActivating / _activationTime);
		_progressBar.Value = _progress * 100f;
	}

	[ScriptMethod]
	public void HideFor(Player plr)
	{
		_hiddenFor.Add(plr);
		RpcId(plr.PeerID, nameof(HideRPC));
	}

	[ScriptMethod]
	public void ShowFor(Player plr)
	{
		_hiddenFor.Remove(plr);
		RpcId(plr.PeerID, nameof(ShowRPC));
	}


	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void HideRPC()
	{
		_prompt.Visible = false;
		_hiddenByServer = true;
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void ShowRPC()
	{
		_hiddenByServer = false;
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void TriggerInteracted()
	{
		Player? p = Root.Players.GetPlayerFromPeerID(RemoteSenderId);
		if (p == null)
		{
			return;
		}
		if (_hiddenFor.Contains(p))
		{
			return; // kinda sus
		}
		Interacted.Invoke(p);
	}

	[ScriptProperty] public PTSignal<Player> Interacted { get; private set; } = new();

	[ScriptEnum("UIMode")]
	public enum UIModeEnum
	{
		Default,
		GUI3D
	}
}
