namespace LostCrownlike.Core;

/// <summary>
/// The consequences of a heat state, as pure functions.
///
/// These live in Core rather than on HeatManager because two subsystems need
/// the same answer from opposite ends: World decides what the state IS, and AI
/// has to act on it. Routing "what does Kindled do to a cooldown" through the
/// bus would mean either a wider HeatChanged signal or AI holding a reference
/// to a World node -- and this project's one architectural rule is that
/// subsystems talk through the vocabulary, not through each other.
///
/// The THRESHOLDS are not here. Where a state begins is a tuning decision and
/// lives as an [Export] on HeatManager; what a state DOES is structure, and a
/// second copy of it is how the two halves drift apart.
/// </summary>
public static class HeatRules
{
    /// Attack cooldown multiplier. Below 1 means the room asks faster.
    public static float CooldownScale(HeatState state) => state switch
    {
        HeatState.Kindled => 0.90f,
        HeatState.Molten => 0.80f,
        HeatState.Flashover => 0.80f,
        _ => 1.0f,
    };

    /// <summary>
    /// The telegraph a given attack actually carries, once the room's heat has
    /// had its say. `native` is what the enemy type chose for itself;
    /// `attackSequence` is that enemy's own running attack count, so the
    /// "every second attack" rule is deterministic per enemy rather than a
    /// coin flip -- this project has already paid three times for checks made
    /// flaky by unseeded randomness.
    ///
    /// The ladder is White -> Gold -> Red and it CAPS at Red: a promoted Red
    /// is still Red, not an undefined fourth channel.
    /// </summary>
    public static AttackTelegraphType Promote(AttackTelegraphType native, HeatState state, int attackSequence)
    {
        switch (state)
        {
            // Everything is unparryable for the length of the window. Not a
            // promotion by one step -- a White attack becomes Red, not Gold,
            // because the point of a flashover is that nothing can be answered.
            case HeatState.Flashover:
                return AttackTelegraphType.UnparryableRed;

            case HeatState.Molten:
                return Step(native, 1);

            // Every second attack. Counted from the enemy's own sequence, which
            // starts at 1 on its first swing, so the promoted ones are the
            // 2nd, 4th, 6th -- the same parity the Sentinel already uses to
            // decide which of its strikes is the gold one.
            case HeatState.Kindled:
                return attackSequence % 2 == 0 ? Step(native, 1) : native;

            default:
                return native;
        }
    }

    private static AttackTelegraphType Step(AttackTelegraphType from, int steps)
    {
        int promoted = (int)from + steps;
        if (promoted > (int)AttackTelegraphType.UnparryableRed)
            promoted = (int)AttackTelegraphType.UnparryableRed;
        return (AttackTelegraphType)promoted;
    }
}
