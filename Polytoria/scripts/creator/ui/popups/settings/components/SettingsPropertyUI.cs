// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Properties;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using System;

namespace Polytoria.Creator.UI.Components;

public partial class SettingsPropertyUI : Control
{
	[Export] private Label _propNameLabel = null!;
	[Export] private Control _propContainer = null!;

	public SettingDef SettingDef { get; private set; } = null!;
	public ISettingsContext SettingsContext { get; private set; } = null!;

	private IProperty _input = null!;
	private bool _suppressChanged;

	public void Init(SettingDef def, ISettingsContext context)
	{
		SettingDef = def;
		SettingsContext = context;
	}

	public override void _Ready()
	{
		_propNameLabel.Text = SettingDef.Label;

		Type valueType = SettingDef.ValueType;
		IProperty input = Globals.LoadProperty(valueType);

		input.PropertyType = valueType;
		_propContainer.AddChild((Node)input);

		if (input is SingleProperty sp && SettingDef.UntypedMinValue != null && SettingDef.UntypedMaxValue != null)
		{
			sp.MinValue = Convert.ToSingle(SettingDef.UntypedMinValue);
			sp.MaxValue = Convert.ToSingle(SettingDef.UntypedMaxValue);
			sp.AllowGreater = false;
			sp.AllowLesser = false;
		}

		((Control)input).SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_input = input;
		SettingsContext.Changed += OnExternalChanged;

		Callable.From(() =>
		{
			if (!IsInstanceValid(this))
				return;

			try
			{
				object? currentValue = SettingsContext.GetUntyped(SettingDef.Key);
				input.SetValue(currentValue);

				input.ValueChanged += val =>
				{
					_suppressChanged = true;
					SettingsContext.Set(SettingDef.Key, val!);
					_suppressChanged = false;
				};
			}
			catch (Exception e)
			{
				PT.PrintErr($"Failed to initialize settings property UI for '{SettingDef.Key}': {e}");
			}
		}).CallDeferred();
	}

	public override void _ExitTree()
	{
		if (SettingsContext != null)
			SettingsContext.Changed -= OnExternalChanged;
		base._ExitTree();
	}

	private void OnExternalChanged(SettingChangedEvent e)
	{
		if (_suppressChanged || e.Key != SettingDef.Key)
			return;

		_input.SetValue(e.NewValue);
	}
}
