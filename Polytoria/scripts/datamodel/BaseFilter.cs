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
	protected virtual Shader _filterShader {
		get => null!;
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
		SetVisibility();
		UpdateFilter();
		base.Init();
	}

	public override void PostReparent()
	{
		SetVisibility();
		base.PostReparent();
	}
	
	public override void PreDelete()
	{
		_shaderMaterial.Dispose();
		base.PreDelete();
	}

	private void SetVisibility()
	{
		// Only display the effect if it's within the world.
		_filterRect.Visible = Parent != null && Parent is not Temporary;
	}

	protected virtual void UpdateFilter() {}
}
