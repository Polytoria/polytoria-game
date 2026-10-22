// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Enums;

[ScriptEnum]
public enum KeyModeEnum
{
	/// <summary>
	/// <para>The key is tested using its Latin equivalent.</para>
	/// </summary>
	KeyCode,
	/// <summary>
	/// <para>The key is tested using its position on US QWERTY Keyboard.</para>
	/// </summary>
	PhysicalKeyCode,
}
