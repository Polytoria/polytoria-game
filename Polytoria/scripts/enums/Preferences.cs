// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Enums;

[ScriptEnum("GraphicsPreset")]
public enum GraphicsPresetEnum
{
	Low,
	Medium,
	High,
	Ultra,
	Photo,
	Custom
}

[ScriptEnum("RenderingMethod")]
public enum RenderingMethodEnum
{
	Auto = -1,
	Standard = 0,
	Performance = 1,
	Compatibility = 2
}


[ScriptEnum("ShadowQuality")]
public enum ShadowQualityEnum
{
	Off,
	Low,
	Medium,
	High,
	Ultra
}


[ScriptEnum("MsaaScale")]
public enum MsaaScaleEnum
{
	Disabled = 0,
	X2 = 2,
	X4 = 4,
	X8 = 8
}
