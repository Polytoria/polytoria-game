// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Creator;
using Polytoria.Datamodel;

namespace Polytoria.Creator.Tools;

[Abstract]
public partial class CreatorTool
{
	public virtual void ProcessInput(InputEvent @event, Dynamic? hoveringOn) {}
}
