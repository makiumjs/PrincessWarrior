using Godot;

namespace LostCrownlike.Core;

/// <summary>
/// Cross-subsystem event vocabulary — see ARCHITECTURE.md.
///
/// Godot 4 source-generated signals ([Signal] ... EventHandler). Payloads that
/// were previously carried as a DamageInfo struct are FLATTENED into
/// Variant-compatible parameters: a custom struct cannot cross the Variant
/// boundary, and the two alternatives were both worse — making DamageInfo a
/// Resource/GodotObject would allocate on every hit, and dropping the data
/// would lose knockback and crit information that Combat and AI already use.
/// DamageInfo itself is unchanged and still used for the direct
/// IDamageable.TakeDamage call, which is the hot path and never goes through
/// the bus.
///
/// The Emit* helpers keep a DamageInfo-shaped overload so call sites stay
/// readable; they unpack into the signal. Subscribers still use += / -=,
/// because the generator emits an event wrapper per signal.
///
/// Autoloaded as "/root/EventBus" (see project.godot).
/// </summary>
public partial class EventBus : Node
{
    public static EventBus Instance { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    // -- PlayerCamera ---------------------------------------------------
    [Signal] public delegate void JumpedEventHandler();
    [Signal] public delegate void DoubleJumpedEventHandler();
    [Signal] public delegate void DashedEventHandler();
    [Signal] public delegate void WallJumpedEventHandler();
    [Signal] public delegate void LandedEventHandler();
    [Signal] public delegate void PlayerDamagedEventHandler(int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical);
    [Signal] public delegate void PlayerDiedEventHandler();
    /// Authoritative health broadcast. UI must render from this rather than
    /// keeping its own running total: a shadow copy only ever decremented by
    /// PlayerDamaged cannot know about healing, respawns or a new game, and
    /// the HUD sat at 0/100 for the rest of the run after the first death.
    [Signal] public delegate void PlayerHealthChangedEventHandler(int current, int max);

    // -- Combat / AI ----------------------------------------------------
    [Signal] public delegate void EnemyDamagedEventHandler(Node3D enemy, int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical);
    [Signal] public delegate void EnemyDiedEventHandler(Node3D enemy);

    // -- World / Save ---------------------------------------------------
    [Signal] public delegate void AbilityUnlockedEventHandler(int ability);
    [Signal] public delegate void CheckpointReachedEventHandler(string checkpointId);
    [Signal] public delegate void LevelTransitionRequestedEventHandler(string scenePath, string spawnPointId);
    /// The run reached its last room and ended. Distinct from a room
    /// transition: there is no next room, so nothing should rebuild on it.
    [Signal] public delegate void RunCompletedEventHandler(int roomsCleared);

    /// A blow was turned aside. `perfect` means it landed inside the tight
    /// opening window, which staggers the attacker; an ordinary parry only
    /// spares the player. Broadcast rather than handed to the attacker
    /// directly: DamageInfo carries a source POSITION, not a source node, and
    /// widening it to suit one mechanic would push a combat detail into every
    /// call site that deals damage.
    [Signal] public delegate void ParriedEventHandler(bool perfect, Vector3 atPosition);

    /// The player asked to start over from the pause menu.
    [Signal] public delegate void RestartRequestedEventHandler();

    /// The boss's state, for the HUD to draw. Flattened to primitives for the
    /// same reason as PlayerDamaged, and routed through the bus rather than
    /// letting the HUD find the boss: the HUD's one architectural rule is that
    /// it never references a gameplay type, and a boss bar is not worth the
    /// exception. `healthFraction` of 0 with `alive` false means the fight is
    /// over; the bar hides itself on that rather than on a separate signal.
    [Signal] public delegate void BossStateChangedEventHandler(bool alive, float healthFraction, bool armoured, bool enraged);

    public void EmitJumped() => EmitSignal(SignalName.Jumped);
    public void EmitDoubleJumped() => EmitSignal(SignalName.DoubleJumped);
    public void EmitDashed() => EmitSignal(SignalName.Dashed);
    public void EmitWallJumped() => EmitSignal(SignalName.WallJumped);
    public void EmitLanded() => EmitSignal(SignalName.Landed);
    public void EmitPlayerDied() => EmitSignal(SignalName.PlayerDied);
    public void EmitPlayerHealthChanged(int current, int max) =>
        EmitSignal(SignalName.PlayerHealthChanged, current, max);

    public void EmitPlayerDamaged(DamageInfo info) =>
        EmitSignal(SignalName.PlayerDamaged, info.Amount, info.SourcePosition, info.Knockback, info.IsCritical);

    public void EmitEnemyDamaged(Node3D enemy, DamageInfo info) =>
        EmitSignal(SignalName.EnemyDamaged, enemy, info.Amount, info.SourcePosition, info.Knockback, info.IsCritical);

    public void EmitEnemyDied(Node3D enemy) => EmitSignal(SignalName.EnemyDied, enemy);

    public void EmitAbilityUnlocked(AbilityFlags ability) => EmitSignal(SignalName.AbilityUnlocked, (int)ability);

    public void EmitCheckpointReached(string checkpointId) => EmitSignal(SignalName.CheckpointReached, checkpointId);

    public void EmitLevelTransitionRequested(string scenePath, string spawnPointId) =>
        EmitSignal(SignalName.LevelTransitionRequested, scenePath, spawnPointId);

    public void EmitRestartRequested() => EmitSignal(SignalName.RestartRequested);

    public void EmitBossStateChanged(bool alive, float healthFraction, bool armoured, bool enraged) =>
        EmitSignal(SignalName.BossStateChanged, alive, healthFraction, armoured, enraged);

    public void EmitParried(bool perfect, Vector3 atPosition) =>
        EmitSignal(SignalName.Parried, perfect, atPosition);

    public void EmitRunCompleted(int roomsCleared) =>
        EmitSignal(SignalName.RunCompleted, roomsCleared);
}
