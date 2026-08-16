// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System;

namespace Polytoria.Enums;

[ScriptEnum, Flags]
public enum PhysicsLayerEnum //: uint
{
    None = 0,
    Default = 1,
    Player = 2,
    CreatorBounds = 4,
    CreatorBoundsCanCollide = 8,
    RaycastCollision = 16,

    All = unchecked((int)uint.MaxValue) //Like this sense we want all 32 bit values active
}