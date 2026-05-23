// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using Polytoria.Datamodel.Data;
using System;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class UIScaleDto
{
	public float Offset { get; set; }
	public float Scale { get; set; }

	[MemoryPackConstructor, JsonConstructor]
	public UIScaleDto() { }

	public UIScaleDto(UIScale scale) { Offset = scale.Offset; Scale = scale.Scale; }

	public UIScale ToUIScale() => new(Offset, Scale);
}
