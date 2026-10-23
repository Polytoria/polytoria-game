// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI.Prompt;
using Polytoria.Datamodel.Services;
using Polytoria.Enums;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;


namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class InteractionPrompt : Dynamic
{
	private const string ClassicScenePath = "res://scenes/client/ui/prompt/prompt_classic.tscn";
	private const string Style1ScenePath = "res://scenes/client/ui/prompt/prompt_style1.tscn";
	private const string Style2ScenePath = "res://scenes/client/ui/prompt/prompt_style2.tscn";

	private static readonly List<InteractionPrompt> _shownPrompts = [];
	private static readonly Dictionary<KeyCodeEnum, InteractionPrompt> _shownPerKey = [];

	private Physical? _parent = null!;

	private bool _hiddenByServer = false;

	private bool _enabled = true;
	private float _maxDistance = 10.0f;
	private bool _requireFacing = true;
	private bool _requiresLineOfSight = false;
	private bool _clickable = true;
	private float _losThreshold = 0.5f;
	private float _activationTime = 0.5f;
	private string _title = "Interact";
	private string _subtitle = "[color=#a3a3a3]Subtitle[/color]";
	private PromptStyleEnum _style = PromptStyleEnum.Classic;
	private PromptExclusivityEnum _exclusivity = PromptExclusivityEnum.OnePerButton;
	private KeyCodeEnum _keyCode = KeyCodeEnum.E;
	private KeyCodeEnum _gamepadKeyCode = KeyCodeEnum.GamepadX;
	private Vector2 _uiOffset = Vector2.Zero;


	private float _scale = 1f;

	private bool _useParentForVisibility = false;

	private Node3D _prompt = null!;

	private UIInteractionPrompt? _ui;
	private UIInteractionPrompt? _hiddenUI;
	private bool _isPreview = false;

	private float _progress = 0.0f;

	private bool _inSight = true;
	private float _losTimer = 0f;
	private bool _isMouseOverParent = false;
	private float _timeSpentActivating = 0.0f;

	private bool _registered = false;
	private bool _shown = false;
	private bool _triggered = false;
	private bool _holding = false;
	private bool _inputHeld = false;
	private InputService.InputTypeEnum? _shownInputType;

	private readonly HashSet<Player> _hiddenFor = [];
	private readonly HashSet<Player> _shownFor = [];
	private readonly Dictionary<Player, PromptState> _states = [];

	private bool _hideByDefault = false;

	private UIModeEnum _uiMode = UIModeEnum.Default;

	[Editable, ScriptProperty]
	public UIModeEnum UIMode
	{
		get => _uiMode;
		set
		{
			_uiMode = value;
			UpdatePreview();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(PromptStyleEnum.Classic)]
	public PromptStyleEnum Style
	{
		get => _style;
		set
		{
			_style = value;
			HideUI();
			_hiddenUI = null;
			UpdatePreview();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(PromptExclusivityEnum.OnePerButton)]
	public PromptExclusivityEnum Exclusivity
	{
		get => _exclusivity;
		set
		{
			_exclusivity = value;
			OnPropertyChanged();
		}
	}

	[Editable(CustomPropertyControl = "KeyCode"), ScriptProperty, DefaultValue(KeyCodeEnum.E)]
	public KeyCodeEnum KeyCode
	{
		get => _keyCode;
		set
		{
			_keyCode = value;
			_shownInputType = null;
			UpdatePreview();
			OnPropertyChanged();
		}
	}

	[Editable(CustomPropertyControl = "GamepadKeyCode"), ScriptProperty, DefaultValue(KeyCodeEnum.GamepadX)]
	public KeyCodeEnum GamepadKeyCode
	{
		get => _gamepadKeyCode;
		set
		{
			_gamepadKeyCode = value;
			_shownInputType = null;
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
			RefreshText();
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
			RefreshText();
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
			UpdatePreview();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 UIOffset
	{
		get => _uiOffset;
		set
		{
			_uiOffset = value;
			UpdatePreview();
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

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool RequiresLineOfSight
	{
		get => _requiresLineOfSight;
		set
		{
			_requiresLineOfSight = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Clickable
	{
		get => _clickable;
		set
		{
			_clickable = value;
			OnPropertyChanged();
		}
	}


	[ScriptProperty]
	public Player[] HiddenFor
	{
		get => _hiddenFor.ToArray();
		set
		{
			foreach (Player plr in _hiddenFor.Except(value).ToArray())
			{
				ShowFor(plr);
			}
			foreach (Player plr in value.Except(_hiddenFor).ToArray())
			{
				HideFor(plr);
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty, NoSync]
	public float Progress => _progress;

	private bool CanShow { get; set; }
	private bool CanActivate { get; set; }
	private float Priority { get; set; }
	internal Node3D PromptNode => _prompt;

	private KeyCodeEnum GetKey(InputService.InputTypeEnum inputType)
	{
		if (inputType == InputService.InputTypeEnum.Gamepad && _gamepadKeyCode != KeyCodeEnum.None)
		{
			return _gamepadKeyCode;
		}
		return _keyCode;
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
		Root.Players?.PlayerRemoved.Connect(OnPlayerRemoved);
		Register();
		base.EnterTree();
		UpdatePreview();
	}

	public override void ExitTree()
	{
		_parent?.MouseEnter.Disconnect(OnMouseEnterParent);
		_parent?.MouseExit.Disconnect(OnMouseExitParent);
		_parent = null;
		Root?.Players?.PlayerRemoved.Disconnect(OnPlayerRemoved);
		if (_registered)
		{
			Root?.Input?.Prompts.Remove(this);
			_registered = false;
		}
		HideUI();
		SetShown(false);
		base.ExitTree();
	}

	public override Node CreateGDNode()
	{
		_prompt = new Node3D();
		return _prompt;
	}

	public override void Init()
	{
		base.Init();
		if (Root.SessionType == World.SessionTypeEnum.Creator)
		{
			_isPreview = true;
			ChildAdded.Connect(OnChildrenChanged);
			ChildRemoved.Connect(OnChildrenChanged);
			UpdatePreview();
		}
		else
		{
			_prompt.Visible = false;
		}
	}

	private void Register()
	{
		if (_registered || Root?.Input == null) return;
		Root.Input.Prompts.Add(this);
		_registered = true;
	}

	internal static void UpdatePrompts(InputService input, double delta)
	{
		_shownPrompts.Clear();
		_shownPerKey.Clear();

		World root = input.Root;
		Player? player = root.Players.LocalPlayer;
		Camera? camera = root.Environment.CurrentCamera;

		InteractionPrompt? best = null;

		foreach (InteractionPrompt prompt in input.Prompts)
		{
			prompt.UpdateState(player, camera, delta);
			if (!prompt.CanShow) continue;

			if (prompt.Exclusivity == PromptExclusivityEnum.AlwaysShow)
			{
				_shownPrompts.Add(prompt);
				continue;
			}

			if (best == null || prompt.Beats(best))
			{
				best = prompt;
			}

			if (prompt.Exclusivity == PromptExclusivityEnum.OnePerButton)
			{
				KeyCodeEnum key = prompt.GetKey(input.LastInputType);
				if (!_shownPerKey.TryGetValue(key, out InteractionPrompt? other) || prompt.Beats(other))
				{
					_shownPerKey[key] = prompt;
				}
			}
		}

		if (best?.Exclusivity == PromptExclusivityEnum.OneGlobally)
		{
			_shownPrompts.Add(best);
		}
		else
		{
			_shownPrompts.AddRange(_shownPerKey.Values);
		}

		if (_shownPrompts.Count > 16)
		{
			_shownPrompts.Sort((a, b) => b.Priority.CompareTo(a.Priority));
			_shownPrompts.RemoveRange(16, _shownPrompts.Count - 16);
		}

		input.PromptKeys.Clear();
		foreach (InteractionPrompt prompt in _shownPrompts)
		{
			if (!prompt.CanActivate) continue;

			input.PromptKeys.Add(prompt.KeyCode);
			input.PromptKeys.Add(prompt.GamepadKeyCode);
		}

		for (int i = 0; i < input.Prompts.Count; i++)
		{
			input.Prompts[i].Tick(delta, input, _shownPrompts.Contains(input.Prompts[i]));
		}
	}

	private bool Beats(InteractionPrompt other)
	{
		return (CanActivate, Priority).CompareTo((other.CanActivate, other.Priority)) > 0;
	}

	private void UpdateState(Player? player, Camera? camera, double delta)
	{
		Vector3 position = _prompt.GlobalPosition;
		float distance = player != null ? player.GetGlobalPosition().DistanceTo(position) : float.PositiveInfinity;
		bool inRange = distance <= _maxDistance;

		float facing = 0f;
		if (camera != null)
		{
			Transform3D cameraTransform = camera.GetGlobalTransform();
			facing = (-cameraTransform.Basis.Z).Dot((position - cameraTransform.Origin).Normalized());

			_losTimer -= (float)delta;
			if (_requiresLineOfSight && inRange && _losTimer <= 0f)
			{
				_losTimer = 0.2f;
				Instance[] ignore = Parent is Dynamic parent ? [parent, player!] : [player!];
				_inSight = Root.Environment.HasLineOfSight(cameraTransform.Origin, position, ignore);
			}
		}

		CanShow = inRange && _enabled && !_hiddenByServer && !IsHidden
			&& (!_requiresLineOfSight || _inSight)
			&& (!_useParentForVisibility || _isMouseOverParent);
		CanActivate = CanShow && (!_requireFacing || (camera != null && facing >= _losThreshold));
		Priority = facing - distance / Mathf.Max(_maxDistance, 0.01f) * 0.25f;
	}

	private void Tick(double delta, InputService input, bool shown)
	{
		SetShown(shown);

		if (_shown && _uiMode == UIModeEnum.Default)
		{
			ShowUI();
		}
		else
		{
			HideUI();
		}

		if (_ui != null && _shownInputType != input.LastInputType)
		{
			_shownInputType = input.LastInputType;
			_ui.SetKey(GetKey(input.LastInputType), input.LastInputType == InputService.InputTypeEnum.Touch);
		}

		bool active = _shown && CanActivate && input.IsGameFocused;
		bool keyDown = input.IsKeyPressed(_keyCode) || input.IsKeyPressed(_gamepadKeyCode);
		bool down = keyDown || _inputHeld || (_ui?.PointerDown ?? false);
		if (!down && _triggered)
		{
			_triggered = false;
			SendPromptEvent(PromptEventEnum.TriggerEnded);
		}
		bool holding = down && !_triggered && (active || _holding && _shown && input.IsGameFocused);
		UpdateHold(holding, delta);

		if (_ui != null)
		{
			_ui.Progress = _progress;
			_ui.Focused = active || holding;
			_ui.Holding = holding || _triggered;
			_ui.PromptScale = _scale;
			_ui.ScreenOffset = _uiOffset;
		}
	}

	private void SetShown(bool shown)
	{
		if (_shown == shown) return;

		_shown = shown;
		if (GodotObject.IsInstanceValid(_prompt))
		{
			_prompt.Visible = shown;
		}
		if (shown)
		{
			PromptShown.Invoke();
		}
		else
		{
			PromptHidden.Invoke();
		}
	}

	private void UpdateHold(bool holding, double delta)
	{
		if (holding != _holding)
		{
			_holding = holding;
			if (_activationTime > 0f)
			{
				SendPromptEvent(holding ? PromptEventEnum.HoldBegan : PromptEventEnum.HoldEnded);
			}
		}

		if (holding)
		{
			_timeSpentActivating += (float)delta;

			if (_timeSpentActivating >= _activationTime)
			{
				_triggered = true;
				SendPromptEvent(PromptEventEnum.Triggered);
			}
		}
		else if (!_triggered)
		{
			_timeSpentActivating = 0.0f;
		}

		_progress = _activationTime > 0f ? Mathf.Clamp(_timeSpentActivating / _activationTime, 0f, 1f) : 0f;
	}

	private void ShowUI()
	{
		if (_ui != null) return;

		if (_hiddenUI != null && GodotObject.IsInstanceValid(_hiddenUI) && !_hiddenUI.IsQueuedForDeletion())
		{
			_ui = _hiddenUI;
		}
		else
		{
			Control? layer = Root.CoreUI?.CoreUI?.PromptLayer;
			if (layer == null) return;

			_ui = CreateUI();
			layer.AddChild(_ui);
		}
		_hiddenUI = null;
		_ui.Attach(this);
		RefreshText();
		_shownInputType = null;
	}

	private void HideUI()
	{
		_ui?.Detach();
		_hiddenUI = _ui ?? _hiddenUI;
		_ui = null;
	}

	private void OnChildrenChanged(Instance child)
	{
		UpdatePreview();
	}

	private void UpdatePreview()
	{
		if (!_isPreview) return;

		if (IsHidden || _uiMode == UIModeEnum.GUI3D && GetChildren().Any(child => child is GUI3D))
		{
			HideUI();
			return;
		}

#if CREATOR
		if (_ui == null && Root.Container != null)
		{
			_ui = CreateUI();
			Root.Container.Overlay.AddChild(_ui);
			Root.Container.Overlay.MoveChild(_ui, 0);
			_ui.Attach(this);
			_ui.ShowAsPreview();
		}
#endif
		if (_ui == null) return;

		_ui.SetKey(_keyCode, false);
		_ui.PromptScale = _scale;
		_ui.ScreenOffset = _uiOffset;
		RefreshText();
	}

	private void RefreshText()
	{
		_ui?.SetText(_title, _subtitle);
	}

	private UIInteractionPrompt CreateUI()
	{
		string scenePath = _style switch
		{
			PromptStyleEnum.Style1 => Style1ScenePath,
			PromptStyleEnum.Style2 => Style2ScenePath,
			_ => ClassicScenePath
		};
		return Globals.CreateInstanceFromScene<UIInteractionPrompt>(scenePath);
	}

	private void OnPlayerRemoved(Player plr)
	{
		_states.Remove(plr);
		_hiddenFor.Remove(plr);
		_shownFor.Remove(plr);
	}

	[ScriptMethod]
	public void HideFor(Player plr)
	{
		if (!Root.Network.IsServer) return;
		_hiddenFor.Add(plr);
		_shownFor.Remove(plr);
		RpcId(plr.PeerID, nameof(HideRPC));
	}

	[ScriptMethod]
	public void ShowFor(Player plr)
	{
		if (!Root.Network.IsServer) return;
		_hiddenFor.Remove(plr);
		_shownFor.Add(plr);
		RpcId(plr.PeerID, nameof(ShowRPC));
	}

	[ScriptMethod]
	public void InputHoldBegin()
	{
		_inputHeld = true;
	}

	[ScriptMethod]
	public void InputHoldEnd()
	{
		_inputHeld = false;
	}


	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void HideRPC()
	{
		_hiddenByServer = true;
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void ShowRPC()
	{
		_hiddenByServer = false;
	}

	private void SendPromptEvent(PromptEventEnum kind)
	{
		InvokePromptEvent(kind, Root.Players.LocalPlayer);
		RpcId(1, nameof(TriggerPromptEvent), (int)kind);
	}

	private void InvokePromptEvent(PromptEventEnum kind, Player? player)
	{
		PTSignal<Player>? signal = kind switch
		{
			PromptEventEnum.Triggered => Interacted,
			PromptEventEnum.TriggerEnded => TriggerEnded,
			PromptEventEnum.HoldBegan => HoldBegan,
			PromptEventEnum.HoldEnded => HoldEnded,
			_ => null
		};
		signal?.Invoke(player);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void TriggerPromptEvent(int kind)
	{
		Player? p = Root.Players.GetPlayerFromPeerID(RemoteSenderId);
		if (p == null)
		{
			return;
		}
		if (!_states.TryGetValue(p, out PromptState? state))
		{
			state = new();
			_states[p] = state;
		}

		bool canStart = _enabled && !_hiddenFor.Contains(p) && (!_hideByDefault || _shownFor.Contains(p)) && p.GetGlobalPosition().DistanceTo(GetGlobalPosition()) <= _maxDistance + 10f;

		switch ((PromptEventEnum)kind)
		{
			case PromptEventEnum.HoldBegan:
				if (!canStart || state.Holding || state.Triggered) return;
				state.Holding = true;
				break;
			case PromptEventEnum.HoldEnded:
				if (!state.Holding) return;
				state.Holding = false;
				break;
			case PromptEventEnum.Triggered:
				if (!canStart || state.Triggered || _activationTime > 0f && !state.Holding) return;
				state.Triggered = true;
				break;
			case PromptEventEnum.TriggerEnded:
				if (!state.Triggered) return;
				state.Triggered = false;
				break;
			default:
				return;
		}
		InvokePromptEvent((PromptEventEnum)kind, p);
	}

	[ScriptProperty] public PTSignal<Player> Interacted { get; private set; } = new();
	[ScriptProperty] public PTSignal<Player> TriggerEnded { get; private set; } = new();
	[ScriptProperty] public PTSignal<Player> HoldBegan { get; private set; } = new();
	[ScriptProperty] public PTSignal<Player> HoldEnded { get; private set; } = new();
	[ScriptProperty] public PTSignal PromptShown { get; private set; } = new();
	[ScriptProperty] public PTSignal PromptHidden { get; private set; } = new();

	[ScriptEnum("UIMode")]
	public enum UIModeEnum
	{
		Default,
		GUI3D
	}

	[ScriptEnum("PromptStyle")]
	public enum PromptStyleEnum
	{
		Classic,
		Style1,
		Style2
	}

	[ScriptEnum("PromptExclusivity")]
	public enum PromptExclusivityEnum
	{
		OnePerButton,
		OneGlobally,
		AlwaysShow
	}

	private enum PromptEventEnum
	{
		Triggered,
		TriggerEnded,
		HoldBegan,
		HoldEnded
	}

	private sealed class PromptState
	{
		public bool Holding;
		public bool Triggered;
	}
}
