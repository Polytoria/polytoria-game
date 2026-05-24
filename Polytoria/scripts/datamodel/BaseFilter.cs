// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using Polytoria.Datamodel.Services;

namespace Polytoria.Datamodel;

public partial class BaseFilter : Instance
{
	private ColorRect _filterRect = null!;
	protected ShaderMaterial _shaderMaterial = new();
	protected virtual Shader _filterShader
	{
		get => null!;
	}

	private bool _isEnabled;

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool IsEnabled
	{
		get => _isEnabled;
		set
		{
			_isEnabled = value;
			UpdateVisibility();
		}
	}

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene("BaseFilter")!;
	}

	public override void Init()
	{
		_filterRect = GDNode.GetNode<ColorRect>("FilterRect");
		_filterRect.Material = _shaderMaterial;
		_shaderMaterial.Shader = _filterShader;
		UpdateVisibility();
		UpdateFilter();
		base.Init();
	}

	public override void PostReparent()
	{
		UpdateVisibility();
		base.PostReparent();
	}

	public override void PreDelete()
	{
		_shaderMaterial.Dispose();
		base.PreDelete();
	}

	private void UpdateVisibility()
	{
		_filterRect.Visible = !IsHidden && _isEnabled;
	}

	protected virtual void UpdateFilter() { }
}
