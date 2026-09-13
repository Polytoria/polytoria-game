// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public sealed partial class GradientSkyboxAsset : SkyboxAsset
{
	private Color _horizonLineColor = new(0.9044118f, 0.8872592f, 0.7913603f, 1);
	private float _horizonLineExponent = 4;
	private float _horizonLineContribution = 0.25f;
	private Color _skyGradientTop = new(0.172549f, 0.5686274f, 0.6941177f, 1);
	private Color _skyGradientBottom = new(0.764706f, 0.8156863f, 0.8509805f);
	private float _skyGradientExponent = 2.5f;

	[Editable, ScriptProperty]
	public Color HorizonLineColor { get => _horizonLineColor; set { _horizonLineColor = value; NotifyChanged(); } }

	[Editable, ScriptProperty]
	public float HorizonLineExponent { get => _horizonLineExponent; set { _horizonLineExponent = value; NotifyChanged(); } }

	[Editable, ScriptProperty]
	public float HorizonLineContribution { get => _horizonLineContribution; set { _horizonLineContribution = value; NotifyChanged(); } }

	[Editable, ScriptProperty]
	public Color SkyGradientTop { get => _skyGradientTop; set { _skyGradientTop = value; NotifyChanged(); } }

	[Editable, ScriptProperty]
	public Color SkyGradientBottom { get => _skyGradientBottom; set { _skyGradientBottom = value; NotifyChanged(); } }

	[Editable, ScriptProperty]
	public float SkyGradientExponent { get => _skyGradientExponent; set { _skyGradientExponent = value; NotifyChanged(); } }

	internal override void Apply(ShaderMaterial mat)
	{
		mat.SetShaderParameter("horizon_line_color", _horizonLineColor);
		mat.SetShaderParameter("horizon_line_exponent", _horizonLineExponent);
		mat.SetShaderParameter("horizon_line_contribution", _horizonLineContribution);
		mat.SetShaderParameter("sky_gradient_top", _skyGradientTop);
		mat.SetShaderParameter("sky_gradient_bottom", _skyGradientBottom);
		mat.SetShaderParameter("sky_gradient_exponent", _skyGradientExponent);
	}
}
