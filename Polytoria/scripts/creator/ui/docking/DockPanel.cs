// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI.Docking;

public sealed class DockPanel(string id, string title, Control content, Texture2D? icon = null)
{
	public string Id { get; } = id;
	public string Title { get; } = title;
	public Texture2D? Icon { get; } = icon;
	public Control Content { get; } = content;
}
