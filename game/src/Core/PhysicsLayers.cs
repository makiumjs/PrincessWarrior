namespace LostCrownlike.Core;

/// <summary>
/// Collision layer bit constants (1-based, matching Godot's layer numbering).
/// Every body/area in the project must reference these instead of magic numbers.
/// </summary>
public static class PhysicsLayers
{
    public const uint Player = 1 << 0;
    public const uint World = 1 << 1;
    public const uint Enemy = 1 << 2;
    public const uint Hazard = 1 << 3;
    public const uint Pickup = 1 << 4;
}
