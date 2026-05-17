// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using System;
using System.Threading.Tasks;

namespace Polytoria.Creator.Properties;

public sealed partial class InstanceProperty : Button, IProperty<Instance?>
{
	private Instance? _value;

	public Instance? Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		Value = (Instance?)value;
	}

	public override async void _Pressed()
	{
		try
		{
			Instance i = await World.Current!.CreatorContext.Selections.RequestPickInstance();
			ValueChanged?.Invoke(i);
		}
		catch (TaskCanceledException) { }
		catch (Exception ex)
		{
			CreatorService.Interface.PopupAlert(ex.Message, "Assignment error");
		}
		base._Pressed();
	}

	public void Refresh()
	{
		Text = _value?.Name ?? "<null>";
	}

	public override void _Ready()
	{
		Refresh();
	}
}
