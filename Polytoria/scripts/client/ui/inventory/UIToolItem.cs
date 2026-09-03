// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI;

public partial class UIToolItem : Button
{
	public UIInventory Root = null!;
	public Tool LinkedTool = null!;
	public Player Player = null!;

	public bool IsInBackpack
	{
		get => _isInBackpack;
		set
		{
			_isInBackpack = value;
			UpdateLabel();
		}
	}

	[Export]
	public int ToolIndex
	{
		get => _toolIndex;
		set
		{
			_toolIndex = value;
			UpdateLabel();
		}
	}

	private int _toolIndex = 0;
	private bool _isInBackpack = false;

	private Label _toolNameLabel = null!;
	private TextureRect _toolIconRect = null!;
	private Label _toolIndexLabel = null!;
	private TouchScreenButton? _touchscreenButton;
	private Control _touchscreenBlock = null!;
	private Control _baseControl = null!;
	private bool _initialized = false;

	public override void _EnterTree()
	{
		base._EnterTree();
		if (!_initialized)
		{
			Init();
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (_initialized)
		{
			DeInit();
		}
	}

	private bool _toggledConnected = false;

	private void Init()
	{
		_initialized = true;
		_baseControl = GetNode<Control>("Base");
		_toolNameLabel = _baseControl.GetNode<Label>("ToolNameLabel");
		_toolIconRect = _baseControl.GetNode<TextureRect>("Icon");
		_toolIndexLabel = _baseControl.GetNode<Label>("Index");
		_touchscreenBlock = _baseControl.GetNode<Control>("TouchscreenBlock");
		_touchscreenButton = _touchscreenBlock.GetNodeOrNull<TouchScreenButton>("TSB");

		UpdateLabel();
		UpdateName();

		LinkedTool.PropertyChanged.Connect(OnToolPropChanged);
		LinkedTool.Equipped.Connect(OnToolEquipped);
		LinkedTool.Unequipped.Connect(OnToolUnequipped);

		if (!_toggledConnected)
		{
			Toggled += OnToggled;
			_toggledConnected = true;
		}

		bool isTouchscreen = LinkedTool.Root.Input.IsTouchscreen;
		_touchscreenBlock.Visible = isTouchscreen;
		if (isTouchscreen && _touchscreenButton != null)
		{
			_touchscreenButton.Pressed += OnPressed;
		}

		if (LinkedTool.ToolImgTexture != null)
		{
			InsertToolImage();
		}
		LinkedTool.ToolImgTextureLoaded += InsertToolImage;
	}

	private void DeInit()
	{
		_initialized = false;
		LinkedTool.ToolImgTextureLoaded -= InsertToolImage;

		if (_toggledConnected)
		{
			Toggled -= OnToggled;
			_toggledConnected = false;
		}

		LinkedTool.PropertyChanged.Disconnect(OnToolPropChanged);
		LinkedTool.Equipped.Disconnect(OnToolEquipped);
		LinkedTool.Unequipped.Disconnect(OnToolUnequipped);

		_touchscreenButton?.Pressed -= OnPressed;
	}

	private void InsertToolImage()
	{
		Texture2D? img = LinkedTool.ToolImgTexture;
		_toolIconRect.Texture = LinkedTool.ToolImgTexture;
		_toolNameLabel.Visible = img == null;
	}

	private void UpdateLabel()
	{
		if (_toolIndexLabel == null)
		{
			return;
		}
		_toolIndexLabel.Visible = !IsInBackpack;
		_toolIndexLabel.Text = (ToolIndex + 1).ToString();
	}

	private void OnPressed()
	{
		ButtonPressed = !ButtonPressed;
	}

	public override Variant _GetDragData(Vector2 atPosition)
	{
		Root.StartDragFrom(this);
		return "tool:" + LinkedTool.NetworkedObjectID;
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		if (data.VariantType == Variant.Type.String)
		{
			string str = data.AsString();

			if (str.StartsWith("tool:"))
			{
				return true;
			}
		}
		return false;
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		if (data.VariantType == Variant.Type.String)
		{
			string str = data.AsString();

			if (str.StartsWith("tool:"))
			{
				string netId = str.Replace("tool:", "");
				Tool? tool = Root.GetToolFromNetworkID(netId);

				if (tool != null)
				{
					Root.MoveToolSlot(Root.GetToolItemFromTool(tool)!, this);
				}
			}
		}
	}

	private void OnToolPropChanged(string propName)
	{
		if (propName == "Name")
		{
			UpdateName();
		}
	}

	private void UpdateName()
	{
		_toolNameLabel.Text = LinkedTool.Name;
	}

	private void OnToolEquipped()
	{
		SetPressedNoSignal(true);
	}

	private void OnToolUnequipped()
	{
		SetPressedNoSignal(false);
	}

	private void OnToggled(bool to)
	{
		if (Player.Character != null)
		{
			if (to)
			{
				Player.Character.EquipTool(LinkedTool);
			}
			else
			{
				Player.Character.UnequipTool();
			}
		}
	}
}
