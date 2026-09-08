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

    /// A room finished building. Carries which one and how many there are, so
    /// the HUD can say where you are without knowing what a room is. It became
    /// worth saying when a run went from six rooms of 70 metres to ten of 200:
    /// at two minutes you remember; at twelve you do not.
    [Signal] public delegate void RoomEnteredEventHandler(int index, int total);

    /// What the room IS, as opposed to where it sits in the run: "flat", "open"
    /// or "climb", classified by the room's own vertical rise against the
    /// player's jump. A second signal rather than a third parameter on
    /// RoomEntered, because the HUD consumes that one and a room's shape is
    /// nothing the HUD has an opinion about.
    ///
    /// The classification lives in World and crosses as a string, so Audio does
    /// not have to know what a PlatformRect or a PlayerMetrics is to sound
    /// different in a shaft than in a corridor.
    [Signal] public delegate void RoomShapeEventHandler(string shape);

    /// A lever was struck. Carries where, so audio and UI can react to the
    /// place rather than having to find the node -- the same reason Parried
    /// carries a position.
    [Signal] public delegate void LeverThrownEventHandler(Vector3 atPosition);

    // -- World / The Crucible -------------------------------------------

    /// The Forge Heat of the Crucible branch, normalised 0..1, plus which of
    /// the four HeatStates it lands in. Two values rather than one because the
    /// bar and the RULES move at different rates: the fill is continuous and
    /// the state changes four times in a room, and a consumer that only wanted
    /// the state would otherwise have to re-derive it from thresholds it has
    /// no business knowing. `state` crosses as an int for the same reason
    /// AbilityFlags does — an enum cannot ride a Variant — and is cast back by
    /// each handler.
    ///
    /// Emitted only on a change of at least a hundredth of the bar. The heat
    /// moves every physics frame, and this bus carries events.
    [Signal] public delegate void HeatChangedEventHandler(float heat01, int state);

    /// The heat reached its ceiling and the room went off. Carries where, like
    /// Parried and LeverThrown, so the camera shake and the flash happen at the
    /// player rather than at the origin. The damage is NOT in this payload:
    /// it goes through IDamageable like every other blow, so the invulnerability
    /// window applies to it and a flashover cannot stack on the hit that caused it.
    [Signal] public delegate void FlashoverEventHandler(Vector3 atPosition);

    /// Consecutive perfect parries without taking damage. Drives the pip row on
    /// the HUD and, inside the Crucible, how much the next perfect parry heals.
    /// A separate signal from Parried because a streak is a running total and
    /// Parried is a moment — a consumer of one rarely wants the other.
    [Signal] public delegate void ParryStreakChangedEventHandler(int streak);

    /// A branching portal was taken. Carries the destination's name exactly as
    /// RoomExitTrigger.DestinationActName spells it, because that field is
    /// already the name of the thing and a second spelling is how the two
    /// drift apart. Empty destinations do not emit: an ordinary room exit is
    /// not a branch.
    [Signal] public delegate void BranchEnteredEventHandler(string branchName);

    /// The Crucible was beaten. Carries nothing: it is a fact about the run,
    /// not about a place or an amount, and every consumer wants a different
    /// consequence from it -- Combat sharpens the charged attack, the heat
    /// keeps the parry paying outside the branch, and Save writes the flags
    /// that outlive the run. One announcement, three answers, and none of the
    /// three has to know the other two exist.
    [Signal] public delegate void EmberSigilGrantedEventHandler();

    /// An enemy has begun a wind-up, and which channel it is on. Emitted at the
    /// START of the tell rather than at the blow, because the entire point of a
    /// telegraph is the time it buys: a cue that arrives with the damage is a
    /// report, not a warning.
    ///
    /// `type` crosses as an int for the reason every enum on this bus does --
    /// a custom enum cannot ride a Variant -- and is cast back to
    /// AttackTelegraphType by each handler.
    ///
    /// Carries the enemy so a listener can weigh it by distance or ignore one
    /// that is off screen. Audio does not do that yet; the payload is there
    /// because dropping it later is easy and adding it later means touching
    /// every call site.
    [Signal] public delegate void AttackTelegraphedEventHandler(Node3D enemy, int type);

    /// The boss's state, for the HUD to draw. Flattened to primitives for the
    /// same reason as PlayerDamaged, and routed through the bus rather than
    /// letting the HUD find the boss: the HUD's one architectural rule is that
    /// it never references a gameplay type, and a boss bar is not worth the
    /// exception. `healthFraction` of 0 with `alive` false means the fight is
    /// over; the bar hides itself on that rather than on a separate signal.
    [Signal] public delegate void BossStateChangedEventHandler(bool alive, float healthFraction, bool armoured, bool enraged);

    /// <summary>
    /// Emitted when the persistent embers meta-currency changes (collected or spent).
    /// </summary>
    [Signal] public delegate void EmbersChangedEventHandler(int totalEmbers);

    /// <summary>
    /// Emitted when interacting with the Runestone Shrine in Room 0 to open/close the Rune Forge.
    /// </summary>
    [Signal] public delegate void RuneForgeRequestedEventHandler(bool open);

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

    public void EmitRoomEntered(int index, int total) =>
        EmitSignal(SignalName.RoomEntered, index, total);

    public void EmitRoomShape(string shape) =>
        EmitSignal(SignalName.RoomShape, shape);

    public void EmitLeverThrown(Vector3 atPosition) =>
        EmitSignal(SignalName.LeverThrown, atPosition);

    public void EmitBossStateChanged(bool alive, float healthFraction, bool armoured, bool enraged) =>
        EmitSignal(SignalName.BossStateChanged, alive, healthFraction, armoured, enraged);

    public void EmitParried(bool perfect, Vector3 atPosition) =>
        EmitSignal(SignalName.Parried, perfect, atPosition);

    public void EmitRunCompleted(int roomsCleared) =>
        EmitSignal(SignalName.RunCompleted, roomsCleared);

    public void EmitHeatChanged(float heat01, HeatState state) =>
        EmitSignal(SignalName.HeatChanged, heat01, (int)state);

    public void EmitFlashover(Vector3 atPosition) =>
        EmitSignal(SignalName.Flashover, atPosition);

    public void EmitParryStreakChanged(int streak) =>
        EmitSignal(SignalName.ParryStreakChanged, streak);

    public void EmitBranchEntered(string branchName) =>
        EmitSignal(SignalName.BranchEntered, branchName);

    public void EmitEmberSigilGranted() =>
        EmitSignal(SignalName.EmberSigilGranted);

    public void EmitAttackTelegraphed(Node3D enemy, AttackTelegraphType type) =>
        EmitSignal(SignalName.AttackTelegraphed, enemy, (int)type);

    public void EmitEmbersChanged(int totalEmbers) =>
        EmitSignal(SignalName.EmbersChanged, totalEmbers);

    public void EmitRuneForgeRequested(bool open) =>
        EmitSignal(SignalName.RuneForgeRequested, open);
}
