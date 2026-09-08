namespace LostCrownlike.AI;

/// <summary>
/// The armoured enemy at the halfway room, and it exists to teach the ending.
///
/// The parry is this game's signature move and the Warden is the only thing
/// that REQUIRES it: armour turns every blow into 2 chip damage, and the only
/// thing that opens the fight is a perfect parry. Until now the player met that
/// demand once, in the last room, twelve minutes in -- which means the mechanic
/// the whole finale rests on was being taught by the finale.
///
/// It is also the run's only punctuation. Twelve minutes with one event is a
/// long flat line; this splits it into two halves that are remembered
/// separately, which is the other half of why the second half read as the first
/// half louder.
///
/// Deliberately NOT a weaker Warden by numbers alone. It keeps the armour, the
/// stagger and the tell -- the parts that are the lesson -- and gives up the
/// health, the second phase and the reach, which are the parts that make the
/// Warden a wall rather than a teacher.
/// </summary>
public partial class Sentinel : Warden
{
    protected override void Configure()
    {
        base.Configure();

        MaxHealth = 55;             // half the Warden, and no second phase
        AttackDamage = 16;
        AttackRange = 1.9f;
        AttackWindupTime = 0.7f;    // still longer than the 0.38s parry window
        AttackCooldown = 2.1f;      // more room between asks than the Warden gives
        StaggerDuration = 1.4f;
        // Never enrages: EnrageAt is a fraction of health, and 0 is a fraction
        // the health bar cannot reach.
        EnrageAt = 0f;
        EmberBounty = 10;
    }

    private int _sentinelAttackSeq;

    protected override void SelectAttackTelegraph()
    {
        _sentinelAttackSeq++;
        // The Sentinel exists to teach the parry: every 2nd strike is a glowing CounterGold smash
        CurrentTelegraph = (_sentinelAttackSeq % 2 == 0)
            ? LostCrownlike.Core.AttackTelegraphType.CounterGold
            : LostCrownlike.Core.AttackTelegraphType.StandardWhite;
    }
}
