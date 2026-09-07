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

    /// Everything, and it is what the player starts with.
    ///
    /// The run used to hand these out one room at a time, which is the
    /// metroidvania shape PROMPT.md asks for -- and the shape only pays off
    /// with backtracking, which this game does not have: it is a linear run of
    /// ten rooms, so a gate is not a locked door you come back to, it is a
    /// chunk that was quietly downgraded to an easier one until the crystal
    /// appeared. What it actually bought was a class of bug where a room's
    /// crossability depended on which layout happened to be dealt before it.
    ///
    /// Kept as a [Flags] enum rather than deleted: the movement code still asks
    /// "may I double jump", and a constant that answers yes is clearer than
    /// scattering the answer through the controller.
    All = DoubleJump | Dash | WallJump | ChargeAttack,
}
