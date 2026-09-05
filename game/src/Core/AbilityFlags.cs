using System;

namespace LostCrownlike.Core;

[Flags]
public enum AbilityFlags
{
    None = 0,
    DoubleJump = 1 << 0,
    Dash = 1 << 1,
    WallJump = 1 << 2,
    ChargeAttack = 1 << 3,
}
