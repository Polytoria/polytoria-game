// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using System;


namespace Polytoria.Creator.UI;

public sealed partial class Ribbon : PanelContainer
{
	[Export]
	private ButtonGroup _ribbonGroup = null!;

	private Control _container = null!;

	public override void _Ready()
	{
		_container = GetNode<HBoxContainer>("Buttons");

		Button colorButton = _container.GetNode<Button>("Color");
		Control paintColorView = _container.GetNode<Control>("Paint/Color");
		Button materialButton = _container.GetNode<Button>("Material");
		Button insertButton = _container.GetNode<Button>("Insert");

		StyleBoxFlat colorPreview = (StyleBoxFlat)colorButton.GetNode<Panel>("Preview").GetThemeStylebox("panel");
		colorButton.Pressed += () =>
		{
			ColorPicker.Singleton.SwitchTo(colorButton, colorPreview.BgColor, value =>
			{
				colorPreview.BgColor = value;
				paintColorView.Modulate = value;
				CreatorService.Interface.TargetPartColor = value;
			});
		};

		TextureRect materialPreview = materialButton.GetNode<TextureRect>("Preview/Texture");

		PopupPanel materialPopup = materialButton.GetNode<PopupPanel>("Popup");
		Control materialPopupSpawn = materialButton.GetNode<Control>("PopupSpawn");
		ItemList materialContainer = materialPopup.GetNode<ItemList>("Container");

		foreach (string name in Enum.GetNames<Part.PartMaterialEnum>())
		{
			string previewPath = "res://assets/textures/parts/".PathJoin(name).PathJoin("albedo.jpg");
			Texture2D? previewTex = null;
			if (ResourceLoader.Exists(previewPath))
			{
				previewTex = GD.Load<Texture2D>(previewPath);
			}
			materialContainer.AddItem(name, previewTex);
		}
		materialContainer.ItemSelected += idx =>
		{
			materialPreview.Texture = materialContainer.GetItemIcon((int)idx);
			string materialName = materialContainer.GetItemText((int)idx);
			if (Enum.TryParse(typeof(Part.PartMaterialEnum), materialName, out object? PartMaterialEnum))
			{
				CreatorService.Interface.TargetPartMaterial = (Part.PartMaterialEnum)PartMaterialEnum;
			}
		};

		materialButton.Pressed += () =>
		{
			Rect2I rect = new()
			{
				Position = (Vector2I)materialPopupSpawn.GlobalPosition,
				Size = materialPopup.Size
			};
			materialPopup.Popup(rect);
		};
		materialContainer.Select(0);

		insertButton.Pressed += () =>
		{
			CreatorService.Interface.OpenInsertMenu();
		};

		_ribbonGroup.Pressed += OnRibbonChanged;
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("tool_select") || CreatorService.Interface.ToolMode == ToolModeEnum.Select)
		{
			_container.GetNode<Button>("Select").ButtonPressed = true;
		}
		else if (@event.IsActionPressed("tool_move") || CreatorService.Interface.ToolMode == ToolModeEnum.Move)
		{
			_container.GetNode<Button>("Move").ButtonPressed = true;
		}
		else if (@event.IsActionPressed("tool_rotate") || CreatorService.Interface.ToolMode == ToolModeEnum.Rotate)
		{
			_container.GetNode<Button>("Rotate").ButtonPressed = true;
		}
		else if (@event.IsActionPressed("tool_scale") || CreatorService.Interface.ToolMode == ToolModeEnum.Scale)
		{
			_container.GetNode<Button>("Scale").ButtonPressed = true;
		}
		base._UnhandledKeyInput(@event);
	}

	private void OnRibbonChanged(BaseButton rawBtn)
	{
		RibbonToolButton btn = (RibbonToolButton)rawBtn;
		CreatorService.Interface.ToolMode = btn.ToolMode;
		switch (btn.ToolMode)
		{
			case ToolModeEnum.Paint:
			case ToolModeEnum.Brush:
				World.Current?.Container?.GrabFocus();
				break;
		}
	}
}
