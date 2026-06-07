// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared.Settings;

public partial class UIViewLicensesRow : PanelContainer
{
	private const string WindowScenePath = "res://scenes/shared/licenses/licenses_window.tscn";

	private Window? _licenseWindow;

	[Export] private Button _viewLicensesButton = null!;

	public override void _Ready()
	{
		_viewLicensesButton.Pressed += OnViewLicensesPressed;
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_licenseWindow != null)
		{
			_licenseWindow.QueueFree();
			_licenseWindow = null;
		}
		base._ExitTree();
	}

	private void OnViewLicensesPressed()
	{
		if (_licenseWindow == null)
		{
			_licenseWindow = GD.Load<PackedScene>(WindowScenePath).Instantiate<Window>();
			_licenseWindow.Visible = false;
			_licenseWindow.ForceNative = true;
			_licenseWindow.CloseRequested += () => { _licenseWindow.QueueFree(); _licenseWindow = null; };
			GetTree().Root.AddChild(_licenseWindow);
		}
		_licenseWindow.PopupCentered();
	}
}
