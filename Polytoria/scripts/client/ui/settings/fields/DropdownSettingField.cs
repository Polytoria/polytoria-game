using Godot;
using Polytoria.Client.Settings;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.UI;

public sealed partial class DropdownSettingField : MenuButton
{
	public SettingDef Definition = null!;
	private PopupMenu _popup = null!;
	private string _selectedLabel = "Select...";
	private System.Action<SettingChangedEvent>? _changedHandler;

	public override void _Ready()
	{
		MouseDefaultCursorShape = CursorShape.PointingHand;
		FocusMode = FocusModeEnum.All;
		Flat = false;
		Text = _selectedLabel;

		AddThemeStyleboxOverride("normal", CreateStyleBox(new Color(0.12f, 0.12f, 0.12f), new Color(0.24f, 0.24f, 0.24f)));
		AddThemeStyleboxOverride("hover", CreateStyleBox(new Color(0.17f, 0.17f, 0.17f), new Color(0.30f, 0.30f, 0.30f)));
		AddThemeStyleboxOverride("pressed", CreateStyleBox(new Color(0.20f, 0.20f, 0.20f), new Color(0.36f, 0.36f, 0.36f)));
		AddThemeStyleboxOverride("focus", CreateStyleBox(new Color(0.16f, 0.16f, 0.16f), new Color(0.08f, 0.59f, 1f)));
		AddThemeColorOverride("font_color", Colors.White);
		AddThemeColorOverride("font_hover_color", Colors.White);
		AddThemeColorOverride("font_pressed_color", Colors.White);
		AddThemeColorOverride("font_focus_color", Colors.White);
		AddThemeConstantOverride("h_separation", 8);

		_popup = GetPopup();
		BuildOptions();
		RefreshText();

		_popup.IndexPressed += OnIndexPressed;

		_changedHandler = e =>
		{
			if (e.Key == Definition.Key)
			{
				RefreshText();
			}
		};
		ClientSettingsService.Instance.Changed += _changedHandler;

		base._Ready();
	}

	public override void _ExitTree()
	{
		_popup.IndexPressed -= OnIndexPressed;

		if (_changedHandler != null && ClientSettingsService.Instance != null)
		{
			ClientSettingsService.Instance.Changed -= _changedHandler;
			_changedHandler = null;
		}

		base._ExitTree();
	}

	private void OnIndexPressed(long index)
	{
		ApplySelection((int)index);
	}

	private void BuildOptions()
	{
		_popup.Clear();
		var options = Definition.UntypedOptions;

		if (options == null)
		{
			return;
		}

		for (int i = 0; i < options.Count; i++)
		{
			_popup.AddItem(options[i].Label, i);
			if (options[i].IsDisabled) _popup.SetItemDisabled(i, true);
		}
	}

	private void ApplySelection(int id)
	{
		var options = Definition.UntypedOptions;

		if (options == null || id < 0 || id >= options.Count)
		{
			return;
		}

		ClientSettingsService.Instance.Set(Definition.Key, options[id].UntypedValue);
		_selectedLabel = options[id].Label;
		UpdateButtonText();
	}

	private void RefreshText()
	{
		object? current = ClientSettingsService.Instance.GetUntyped(Definition.Key);
		var options = Definition.UntypedOptions;

		if (options == null)
		{
			return;
		}

		foreach (var option in options)
		{
			if (Equals(option.UntypedValue, current))
			{
				_selectedLabel = option.Label;
				UpdateButtonText();
				return;
			}
		}

		_selectedLabel = "Select...";
		UpdateButtonText();
	}

	private void UpdateButtonText()
	{
		if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion())
		{
			return;
		}

		Text = _selectedLabel;
	}

	private static StyleBoxFlat CreateStyleBox(Color backgroundColor, Color borderColor)
	{
		return new StyleBoxFlat
		{
			BgColor = backgroundColor,
			BorderColor = borderColor,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomRight = 6,
			CornerRadiusBottomLeft = 6,
			ContentMarginLeft = 12,
			ContentMarginTop = 8,
			ContentMarginRight = 12,
			ContentMarginBottom = 8
		};
	}
}
