// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;
using Polytoria.Utils;
using System;
using System.Reflection.Metadata;

namespace Polytoria.Creator.Properties;

public sealed partial class Bitmap32Property : GridContainer, IProperty<uint>
{
	private readonly StyleBoxFlat _onStyle = new()
	{
		BgColor = Color.FromHtml("0097FF")
	};

	private readonly StyleBoxFlat _offStyle = new()
	{
		BgColor = Color.FromHtml("00000099")
	};

	private uint _value;

	public event Action<object?>? ValueChanged;

	public uint Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	private bool _resizing = false;

	public Type PropertyType { get; set; } = null!;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		if (value == null) return;
		Value = (uint)value;
	}

	public void Refresh()
	{
		int i = 1;
		foreach (Node n in GetChildren())
		{
			if (n is Button btn)
			{
				var style = BitmapUtils.Get(Value, i) ? _onStyle : _offStyle;

				btn.AddThemeStyleboxOverride("normal", style);
				btn.AddThemeStyleboxOverride("hover", style);
				btn.AddThemeStyleboxOverride("pressed", style);

				i++;
			}
		}
	}

	public override void _Ready()
	{
		Refresh();

		int i = 1;
		foreach (Node n in GetChildren())
		{
			if (n is Button btn)
			{
				int _i = i;
				btn.Pressed += () =>
				{
					Value = BitmapUtils.Set(Value, _i, !BitmapUtils.Get(Value, _i));
					ValueChanged?.Invoke(Value);
					Refresh();
				};

				i++;
			}
		}
	}
}
