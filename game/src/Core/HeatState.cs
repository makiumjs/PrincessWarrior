namespace LostCrownlike.Core;

/// <summary>
/// The four states of the Crucible's Forge Heat, in ascending order of danger.
///
/// Crosses the EventBus as an int for the same reason AbilityFlags does: a
/// custom enum cannot ride a Variant, and widening HeatChanged into four
/// booleans would push the threshold rules into every consumer.
///
/// Flashover is a STATE and not just an event. The event fires once at 100;
/// the state persists for the 2.5 seconds that follow, during which every
/// attack in the room is unparryable and the floor burns regardless of the
/// heat value. A HUD that only heard the event would have nothing to draw for
/// the window that actually kills the player.
/// </summary>
public enum HeatState
{
    /// 0-39. The room behaves like the rest of the game.
    Tempered = 0,

    /// 40-69. Every second attack is promoted one telegraph channel.
    Kindled = 1,

    /// 70-99. Every attack is promoted, and the floor is slag.
    Molten = 2,

    /// The 2.5s window after the heat hit 100.
    Flashover = 3,
}
