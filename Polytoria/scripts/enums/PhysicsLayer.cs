// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System;

namespace Polytoria.Enums;

[ScriptEnum, Flags]
public enum PhysicsLayerEnum : uint
{
    None = 0u,
    Default = 1u,
    Player = 2u,
    CreatorBounds = 4u,
    CreatorBoundsCanCollide = 8u,
    RaycastCollision = 16u,


    All = uint.MaxValue
}