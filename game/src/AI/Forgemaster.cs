using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// The Forgemaster — the Crucible's halfway boss, and the only fight in the
/// game whose gate is not health.
///
/// It inherits the Sentinel, which inherits the Warden, so it keeps the whole
/// grammar the run has already taught: armour that turns a blow into chip
/// damage, no stagger from chip hits, and one opening that only a perfect parry
/// buys. What it adds is a condition on that opening.
///
/// **The heat is its health bar.** Below Molten it alternates white and gold
/// like the Sentinel, and the gold is the door. At Molten and above every
/// strike is unparryable and the chip drops to 1 -- so there is no door at all,
/// and 82 health at 1 a hit is not a fight, it is a sentence. A player who
/// walks in with the room already glowing cannot win; they have to cool it
/// first, and the arena hands them exactly the tools: two slag vents worth -25
/// each, and the husks the room keeps throwing, worth -8 apiece.
///
/// That is the whole branch asked as one question. Everything before this has
/// been teaching the player to manage a number they could mostly ignore. Here
/// the number decides whether the fight is possible.
/// </summary>
[GlobalClass]
public partial class Forgemaster : Sentinel
{
    /// What a blow does on armour while the room is cool enough to fight in.
    /// Same as the Sentinel and the Warden: a stubborn player still wins, in
    /// about eighty hits, which is long enough to teach without being a wall.
    [Export] public int TemperedChipDamage { get; set; } = 2;

    /// And what it does once the room is Molten. Halved, deliberately: at this
    /// heat the parry is gone, so chip is the only route left, and it is made
    /// visibly worse to say "this is not the way".
    [Export] public int MoltenChipDamage { get; set; } = 1;

    /// True while the room is hot enough to seal the fight. Molten is 70+, and
    /// a flashover is hotter still -- the 2.5s window after the bar tops out is
    /// the worst moment this fight has, and it should not be the moment the
    /// boss becomes parryable again.
    ///
    /// Public because it is the one thing about this boss a player must be able
    /// to read, and a HUD that wanted to say "you cannot open it right now" has
    /// to be able to ask.
    public bool RoomIsSealing => RoomHeatState == HeatState.Molten
                              || RoomHeatState == HeatState.Flashover;

    protected override void Configure()
    {
        base.Configure();

        MaxHealth = 82;             // two clean stagger windows, with margin
        AttackDamage = 19;
        AttackRange = 2.10f;
        AttackWindupTime = 0.68f;   // still far past the 0.38s parry window
        AttackRecoveryTime = 0.42f;
        AttackCooldown = 1.95f;
        StaggerDuration = 1.50f;
        ChipDamage = TemperedChipDamage;
        _chipFollowsHeat = true;

        // Never enrages, for the reason the Sentinel does not: the second phase
        // is the Warden's, and a halfway boss that grows a new one steals the
        // ending's only structural surprise. This fight's escalation is the
        // heat, and it comes from the room rather than from the boss.
        EnrageAt = 0f;
        EmberBounty = 50;
    }

    /// <summary>
    /// The heat gate. Below Molten this is the Sentinel's alternation, white
    /// then gold, and the gold is what the fight is for. At Molten every strike
    /// is red and the parry is simply not available.
    ///
    /// AttackSequence is the base class's shared counter, incremented the
    /// instant the attack state is entered, so the first swing is 1 (white) and
    /// the second is 2 (gold).
    /// </summary>
    protected override void SelectAttackTelegraph()
    {
        CurrentTelegraph = RoomIsSealing
            ? AttackTelegraphType.UnparryableRed
            : (AttackSequence % 2 == 0
                ? AttackTelegraphType.CounterGold
                : AttackTelegraphType.StandardWhite);
    }

    /// <summary>
    /// This boss reads the heat itself and must not be promoted on top of it.
    ///
    /// Left promotable, the room would break its own contract: at Kindled --
    /// which is 40 to 69, well BELOW the gate -- every second attack steps up a
    /// channel, so the gold strike the fight is built around would become red
    /// and the boss would be unopenable at a heat the design says it is
    /// openable at. The gate is stated as a threshold, so it has to be the only
    /// thing deciding.
    /// </summary>
    protected override bool AcceptsHeatPromotion => false;

    /// <summary>
    /// The chip value follows the room, and it does so by moving ChipDamage
    /// ITSELF rather than by overriding AbsorbDamage.
    ///
    /// Overriding the absorb path would have worked and would have been wrong:
    /// ChipDamage is public, the HUD and every check can read it, and a boss
    /// whose advertised chip says 2 while it actually deals 1 is a field that
    /// lies. One source of truth, moved when the state changes, and the
    /// inherited armour keeps doing the arithmetic.
    ///
    /// Guarded by a flag rather than run unconditionally: Configure() is called
    /// from _Ready before this class is finished setting itself up, and a
    /// physics tick that fired first would write a chip value from a heat state
    /// nobody had broadcast yet.
    /// </summary>
    private bool _chipFollowsHeat;
    private bool _wasSealing;

    /// <summary>
    /// The Ember Sigil. Announced by the boss itself rather than inferred by a
    /// listener watching EnemyDied for a type name: the reward is this fight's,
    /// and a subsystem that had to recognise "Forgemaster" by string or by cast
    /// would be a second place that knows what beating the Crucible means.
    ///
    /// After base.Die(), which is what puts it in the death state -- an
    /// announcement made before the thing is actually dead is a race waiting to
    /// be found by whichever handler looks at the boss group.
    /// </summary>
    protected override void Die()
    {
        base.Die();
        Core.EventBus.Instance?.EmitEmberSigilGranted();
        GD.Print("[Forgemaster] down -- the Ember Sigil is granted");
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (!_chipFollowsHeat) return;

        bool sealing = RoomIsSealing;
        if (sealing == _wasSealing) return;

        _wasSealing = sealing;
        ChipDamage = sealing ? MoltenChipDamage : TemperedChipDamage;
        GD.Print($"[Forgemaster] room {(sealing ? "sealed" : "open")} -- chip {ChipDamage}");
    }
}
