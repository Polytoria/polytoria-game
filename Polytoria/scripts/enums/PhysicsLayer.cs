// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System;

namespace Polytoria.Enums;

[ScriptEnum, Flags]
public enum PhysicsLayerEnum
{
    None = 0,
    Default = 1,
    Player = 2,
    CreatorBounds = 4,
    CreatorBoundsCanCollide = 8,
    RaycastCollision = 16,

    All = -1 //Sense it's in 2's compliment being converted to a uint equivelent to every bit being 1
}