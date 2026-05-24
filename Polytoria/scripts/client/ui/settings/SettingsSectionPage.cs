using Godot;
using Polytoria.Client.Settings;
using System.Linq;

namespace Polytoria.Client.UI;

public sealed partial class SettingsSectionPage : VBoxContainer
{
	public string SectionKey { get; set; } = "";

	public override void _Ready()
	{
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		AddThemeConstantOverride("separation", 12);

		var defs = ClientSettingsRegistry.Definitions.Values.Where(x => x.SectionKey == SectionKey).ToArray();

		foreach (var def in defs)
		{
			if (def.IsAdvanced && !ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.Advanced.ShowAdvancedSettings))
			{
				continue;
			}

			SettingRow row = new()
			{
				Definition = def,
				Context = ClientSettingsService.Instance
			};

			AddChild(row);
		}

		base._Ready();
	}
}
