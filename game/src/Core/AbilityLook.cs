using Godot;

namespace LostCrownlike.Core;

/// One place that decides what an ability looks like.
///
/// The colour was written out twice -- once in AbilityPickup for the crystal and
/// its light, once in the HUD icon -- so the thing you picked up off the floor
/// and the thing that appeared in the corner agreed only by coincidence. Two
/// copies of a mapping is a bug waiting for whichever one is edited first.
public static class AbilityLook
{
    public static Color ColourFor(AbilityFlags a) => a switch
    {
        AbilityFlags.DoubleJump => new Color(0.35f, 0.75f, 1f),
        AbilityFlags.Dash => new Color(1f, 0.75f, 0.2f),
        AbilityFlags.WallJump => new Color(0.5f, 1f, 0.5f),
        _ => new Color(1f, 0.4f, 0.9f),
    };

    /// The dungeon prop that stands for each ability on the floor.
    public static string PropFor(AbilityFlags a) => a switch
    {
        AbilityFlags.DoubleJump => "bottle_A_labeled_green",
        AbilityFlags.Dash => "bottle_B_brown",
        AbilityFlags.WallJump => "bottle_C_green",
        _ => "bottle_A_labeled_brown",
    };
}
