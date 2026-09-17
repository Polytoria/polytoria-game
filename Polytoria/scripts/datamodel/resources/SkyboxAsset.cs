// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel.Resources;

[Abstract]
public abstract partial class SkyboxAsset : ResourceAsset
{
	public event System.Action? Changed;
	protected void NotifyChanged() => Changed?.Invoke();

	internal abstract void Apply(ShaderMaterial mat);
}
