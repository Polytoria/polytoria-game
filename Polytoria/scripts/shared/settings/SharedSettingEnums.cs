// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Shared.Settings;

public enum GraphicsPreset
{
	Low,
	Medium,
	High,
	Ultra,
	Photo,
	Custom
}

public enum RenderingMethodOption
{
	Standard,
	Performance,
	Compatibility
}

public enum ShadowQuality
{
	Off,
	Low,
	Medium,
	High,
	Ultra
}

public enum MsaaOption
{
	Disabled = 0,
	X2 = 2,
	X4 = 4,
	X8 = 8
}
