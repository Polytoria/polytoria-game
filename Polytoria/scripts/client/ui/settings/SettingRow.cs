using Godot;
using Polytoria.Shared.Settings;
using System.Linq;

namespace Polytoria.Client.UI;

public sealed partial class SettingRow : PanelContainer
{
	public SettingDef Definition = null!;
	public ISettingsContext Context = null!;

	public override void _Ready()
	{
		HBoxContainer root = new();
		AddChild(root);

		VBoxContainer textLayout = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		root.AddChild(textLayout);

		Label title = new() { Text = Definition.Label };
		title.AddThemeFontSizeOverride("font_size", 24);
		textLayout.AddChild(title);

		if (!string.IsNullOrEmpty(Definition.Description))
		{
			Label desc = new()
			{
				Text = Definition.Description,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			desc.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
			textLayout.AddChild(desc);
		}

		if (Definition.RequiresRestart)
		{
			Label restart = new()
			{
				Text = "Requires restart"
			};
			restart.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f));
			restart.AddThemeFontSizeOverride("font_size", 14);
			textLayout.AddChild(restart);
		}

		Control field = SettingFieldFactory.Create(Definition);
		field.CustomMinimumSize = new Vector2(220, 0);
		root.AddChild(field);

		if (Definition.Conditions != null)
		{
			Visible = Definition.Conditions.Any((cond) =>
			{
				object? value = Context.GetUntyped(cond.Target);
				return cond.UntypedPredicate(value);
			});
		}

		Context.Changed += OnExternalChanged;

		base._Ready();
	}

	private void OnExternalChanged(SettingChangedEvent e)
	{
		if (Definition.Conditions != null)
		{
			var match = Definition.Conditions.Where(c => c.Target == e.Key);
			if (match.Any())
				Visible = match.Any(c => c.UntypedPredicate(e.NewValue));
		}
	}

	public override void _ExitTree()
	{
		Context?.Changed -= OnExternalChanged;
		base._ExitTree();
	}
}
