// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.Spatial;

public interface IGizmo
{
	List<Dynamic> Targets { get; set; }
	bool Visible { get; set; }
	Gizmos? RootGizmos { get; set; }

	public event Action? DragStarted;
	public event Action? DragEnded;
}
