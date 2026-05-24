// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Input;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class KeybindsPopup : PopupWindowBase
{
	private static readonly StyleBoxFlat PanelStyle = new()
	{
		BgColor = new Color(0x181818ff),
		BorderColor = new Color(0x2d2d2dff),
		BorderWidthLeft = 1,
		BorderWidthTop = 1,
		BorderWidthRight = 1,
		BorderWidthBottom = 1,
		CornerRadiusTopLeft = 10,
		CornerRadiusTopRight = 10,
		CornerRadiusBottomRight = 10,
		CornerRadiusBottomLeft = 10,
		ContentMarginLeft = 12,
		ContentMarginTop = 12,
		ContentMarginRight = 12,
		ContentMarginBottom = 12
	};

	private static readonly StyleBoxFlat CategoryPanelStyle = new()
	{
		BgColor = new Color(0x121212ff),
		BorderColor = new Color(0x262626ff),
		BorderWidthLeft = 1,
		BorderWidthTop = 1,
		BorderWidthRight = 1,
		BorderWidthBottom = 1,
		CornerRadiusTopLeft = 8,
		CornerRadiusTopRight = 8,
		CornerRadiusBottomRight = 8,
		CornerRadiusBottomLeft = 8,
		ContentMarginLeft = 8,
		ContentMarginTop = 8,
		ContentMarginRight = 8,
		ContentMarginBottom = 8
	};

	private static readonly StyleBoxFlat ActionCardStyle = new()
	{
		BgColor = new Color(0x171717ff),
		BorderColor = new Color(0x2a2a2aff),
		BorderWidthLeft = 1,
		BorderWidthTop = 1,
		BorderWidthRight = 1,
		BorderWidthBottom = 1,
		CornerRadiusTopLeft = 10,
		CornerRadiusTopRight = 10,
		CornerRadiusBottomRight = 10,
		CornerRadiusBottomLeft = 10,
		ContentMarginLeft = 14,
		ContentMarginTop = 12,
		ContentMarginRight = 14,
		ContentMarginBottom = 12
	};

	private static readonly StyleBoxFlat BindingChipStyle = new()
	{
		BgColor = new Color(0x232323ff),
		BorderColor = new Color(0x363636ff),
		BorderWidthLeft = 1,
		BorderWidthTop = 1,
		BorderWidthRight = 1,
		BorderWidthBottom = 1,
		CornerRadiusTopLeft = 999,
		CornerRadiusTopRight = 999,
		CornerRadiusBottomRight = 999,
		CornerRadiusBottomLeft = 999,
		ContentMarginLeft = 10,
		ContentMarginTop = 6,
		ContentMarginRight = 10,
		ContentMarginBottom = 6
	};

	private Tree _sectionTree = null!;
	private VBoxContainer _sectionLayout = null!;
	private readonly Dictionary<TreeItem, string> _itemToSectionKey = [];
	private readonly Dictionary<string, VBoxContainer> _sectionContainers = [];
	private readonly Dictionary<string, ActionRowState> _rowStates = [];
	private string _activeSection = string.Empty;

	private sealed class ActionRowState
	{
		public required string ActionId { get; init; }
		public required CreatorKeybindDefinition Definition { get; init; }
		public required VBoxContainer Root { get; init; }
		public required VBoxContainer BindingContainer { get; init; }
	}

	public override void _Ready()
	{
		Title = "Keybinds";
		MinSize = new Vector2I(920, 560);
		Size = new Vector2I(920, 560);
		Unresizable = true;

		HBoxContainer root = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddThemeConstantOverride("separation", 16);
		AddChild(root);

		PanelContainer leftPane = new()
		{
			CustomMinimumSize = new Vector2(240, 0),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		leftPane.AddThemeStyleboxOverride("panel", CategoryPanelStyle);
		root.AddChild(leftPane);

		VBoxContainer leftLayout = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		leftPane.AddChild(leftLayout);

		HBoxContainer leftHeader = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		leftHeader.AddThemeConstantOverride("separation", 8);
		leftLayout.AddChild(leftHeader);

		Label title = new()
		{
			Text = "Categories",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		leftHeader.AddChild(title);

		Button resetAll = new()
		{
			Text = "Reset All"
		};
		resetAll.Pressed += OnResetAllPressed;
		leftHeader.AddChild(resetAll);

		ScrollContainer categoryScroll = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		leftLayout.AddChild(categoryScroll);

		_sectionTree = new Tree
		{
			HideRoot = true,
			AutoTooltip = false,
			EnableDragUnfolding = false,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 380)
		};
		categoryScroll.AddChild(_sectionTree);

		ScrollContainer scroll = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		root.AddChild(scroll);

		MarginContainer scrollMargin = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		scrollMargin.AddThemeConstantOverride("margin_left", 0);
		scrollMargin.AddThemeConstantOverride("margin_top", 0);
		scrollMargin.AddThemeConstantOverride("margin_right", 12);
		scrollMargin.AddThemeConstantOverride("margin_bottom", 0);
		scroll.AddChild(scrollMargin);

		_sectionLayout = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_sectionLayout.AddThemeConstantOverride("separation", 10);
		scrollMargin.AddChild(_sectionLayout);

		TreeItem rootItem = _sectionTree.CreateItem();
		TreeItem? first = null;

		foreach (CreatorKeybindSectionDef section in CreatorKeybinds.Sections.OrderBy(section => section.SortOrder))
		{
			int actionCount = CreatorKeybinds.GetDefinitionsForSection(section.Key).Count;
			TreeItem item = rootItem.CreateChild();
			item.SetText(0, actionCount > 0 ? $"{section.Label} ({actionCount})" : $"{section.Label} (empty)");
			item.SetSelectable(0, true);
			_itemToSectionKey[item] = section.Key;
			first ??= item;
		}

		_sectionTree.ItemSelected += OnSectionSelected;
		first?.Select(0);
		base._Ready();
	}

	public override void _ExitTree()
	{
		_sectionTree.ItemSelected -= OnSectionSelected;
		base._ExitTree();
	}

	private void OnResetAllPressed()
	{
		CreatorKeybinds.ResetAll();
		RefreshAllSections();
	}

	private void OnSectionSelected()
	{
		TreeItem? selected = _sectionTree.GetSelected();
		if (selected == null)
			return;

		if (!_itemToSectionKey.TryGetValue(selected, out string? sectionKey))
			return;

		if (sectionKey == _activeSection)
			return;

		if (_sectionContainers.TryGetValue(_activeSection, out VBoxContainer? previous))
			previous.Visible = false;

		_activeSection = sectionKey;

		if (!_sectionContainers.TryGetValue(sectionKey, out VBoxContainer? sectionPanel))
		{
			sectionPanel = BuildSection(sectionKey);
			_sectionContainers[sectionKey] = sectionPanel;
			_sectionLayout.AddChild(sectionPanel);
		}

		sectionPanel.Visible = true;
	}

	private VBoxContainer BuildSection(string sectionKey)
	{
		VBoxContainer container = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		container.AddThemeConstantOverride("separation", 12);
		container.Visible = false;

		IReadOnlyList<CreatorKeybindDefinition> defs = CreatorKeybinds.GetDefinitionsForSection(sectionKey);

		if (defs.Count == 0)
		{
			PanelContainer empty = new()
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			empty.AddThemeStyleboxOverride("panel", PanelStyle);

			VBoxContainer emptyBody = new()
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			emptyBody.AddThemeConstantOverride("separation", 4);
			empty.AddChild(emptyBody);

			Label emptyTitle = new()
			{
				Text = "Nothing here yet",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			emptyTitle.AddThemeFontSizeOverride("font_size", 16);
			emptyBody.AddChild(emptyTitle);

			Label emptyDesc = new()
			{
				Text = "This category is visible, but it does not currently have any keybind actions.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			emptyBody.AddChild(emptyDesc);

			container.AddChild(empty);
			return container;
		}

		foreach (CreatorKeybindDefinition def in defs)
		{
			container.AddChild(BuildActionCard(def));
		}

		return container;
	}

	private Control BuildActionCard(CreatorKeybindDefinition def)
	{
		PanelContainer card = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		card.AddThemeStyleboxOverride("panel", ActionCardStyle);

		VBoxContainer body = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		body.AddThemeConstantOverride("separation", 10);
		card.AddChild(body);

		HBoxContainer header = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		header.AddThemeConstantOverride("separation", 10);
		body.AddChild(header);

		VBoxContainer titleStack = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		header.AddChild(titleStack);

		Label label = new()
		{
			Text = def.Label,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		label.AddThemeFontSizeOverride("font_size", 15);
		titleStack.AddChild(label);

		if (!string.IsNullOrWhiteSpace(def.Description))
		{
			Label desc = new()
			{
				Text = def.Description,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Modulate = new Color(0xbbbbbbff),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				ThemeTypeVariation = "Label"
			};
			titleStack.AddChild(desc);
		}

		Control spacer = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		header.AddChild(spacer);

		Button reset = new()
		{
			Text = "Reset",
			Flat = true
		};
		reset.Pressed += () =>
		{
			CreatorKeybinds.ResetBinding(def.Id);
			RefreshAction(def.Id);
		};
		header.AddChild(reset);

		Button clear = new()
		{
			Text = "Clear",
			Flat = true
		};
		clear.Pressed += () =>
		{
			CreatorKeybinds.SetBindings(def.Id, []);
			RefreshAction(def.Id);
		};
		header.AddChild(clear);

		VBoxContainer bindings = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		bindings.AddThemeConstantOverride("separation", 6);
		body.AddChild(bindings);

		_rowStates[def.Id] = new()
		{
			ActionId = def.Id,
			Definition = def,
			Root = body,
			BindingContainer = bindings
		};

		RefreshAction(def.Id);
		return card;
	}

	private void RefreshAllSections()
	{
		foreach (string id in _rowStates.Keys.ToArray())
		{
			RefreshAction(id);
		}
	}

	private void RefreshAction(string actionId)
	{
		if (!_rowStates.TryGetValue(actionId, out ActionRowState? state))
			return;

		foreach (Node child in state.BindingContainer.GetChildren())
			child.QueueFree();

		IReadOnlyList<CreatorKeybindBinding> bindings = CreatorKeybinds.GetBindings(actionId);

		if (bindings.Count == 0)
		{
			Label empty = new()
			{
				Text = "Unbound",
				ThemeTypeVariation = "Label"
			};
			state.BindingContainer.AddChild(empty);
		}
		else
		{
			for (int i = 0; i < bindings.Count; i++)
			{
				int bindingIndex = i;
				CreatorKeybindBinding binding = bindings[i];

				PanelContainer chip = new()
				{
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
				};
				chip.AddThemeStyleboxOverride("panel", BindingChipStyle);

				HBoxContainer chipRow = new()
				{
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
				};
				chipRow.AddThemeConstantOverride("separation", 8);
				chip.AddChild(chipRow);

				Button edit = new()
				{
					Text = binding.ToDisplayString(),
					Flat = true,
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
				};
				edit.Pressed += () => OpenCapture(actionId, bindingIndex, binding);
				chipRow.AddChild(edit);

				Button remove = new()
				{
					Text = "Remove",
					Flat = true
				};
				remove.Pressed += () =>
				{
					CreatorKeybinds.RemoveBinding(actionId, bindingIndex);
					RefreshAction(actionId);
				};
				chipRow.AddChild(remove);

				state.BindingContainer.AddChild(chip);
			}
		}

		Button add = new()
		{
			Text = "Add Binding",
			Flat = true
		};
		add.Pressed += () => OpenCapture(actionId, -1, null);
		state.BindingContainer.AddChild(add);
	}

	private void OpenCapture(string actionId, int index, CreatorKeybindBinding? current)
	{
		if (!CreatorKeybinds.DefinitionsById.TryGetValue(actionId, out CreatorKeybindDefinition? def))
			return;

		ShortcutCapturePopup popup = new()
		{
			ActionLabel = def.Label,
			InitialBinding = current
		};
		popup.Submitted += binding =>
		{
			if (index >= 0)
			{
				CreatorKeybinds.ReplaceBinding(actionId, index, binding);
			}
			else
			{
				CreatorKeybinds.AddBinding(actionId, binding);
			}
			RefreshAction(actionId);
		};
		CreatorService.Interface.PopupWindow(popup);
	}
}
