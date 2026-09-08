using System.Collections.Generic;
using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// The Forge Heat of The Crucible — see docs/decisions/the_crucible_design.md.
///
/// One value, 0 to 100, that rises on its own and falls only when the player
/// fights well. It is the whole thesis of the alternate branch: the Catacombs
/// ask WHERE you are, and this asks WHEN you press. Everything it does is a
/// consequence of that single number — which telegraph channel an attack
/// carries, how fast enemies commit, whether the floor is standable, and
/// whether a perfect parry heals.
///
/// Autoloaded ("/root/HeatManager", listed AFTER EventBus in project.godot)
/// rather than owned by DungeonRoomBuilder, for one reason that is not
/// convenience: the heat has to survive the room 4 -> room 5 rebuild, and
/// BeginRebuild frees every child of the room. A value parented to the room
/// would reset at the door of the fight it was designed to gate.
///
/// It is INERT outside the branch. Active is false on every other room and on
/// the Catacombs path, so no signal it owns is ever emitted there and nothing
/// downstream has to ask where it is.
/// </summary>
public partial class HeatManager : Node
{
    public static HeatManager Instance { get; private set; }

    /// The group an enemy joins to feed the fire. Read by name rather than by
    /// type so this file does not have to know that Emberwright exists;
    /// counting is by live membership, and a corpse (which lingers 3.5s) does
    /// not heat anything.
    public const string HeatSourceGroup = "heat_source";

    // -- Branch identity --------------------------------------------------

    /// Matched against RoomExitTrigger.DestinationActName. A string rather
    /// than an enum because that field is already a string and inventing a
    /// second spelling of the same idea is how the two drift apart.
    [Export] public string BranchName { get; set; } = "Crucible";

    [Export] public int FirstBranchRoom { get; set; } = 4;
    [Export] public int LastBranchRoom { get; set; } = 5;

    // -- Gain --------------------------------------------------------------

    /// You do not enter cold. 25 is a quarter of the bar spent before the
    /// first swing, which is the branch's real entry toll.
    [Export] public float EntryHeat { get; set; } = 25f;

    [Export] public float HeatGainRoom4 { get; set; } = 2.80f;
    [Export] public float HeatGainRoom5 { get; set; } = 3.20f;

    /// Per living member of HeatSourceGroup, additive.
    [Export] public float HeatPerSourcePerSecond { get; set; } = 2.40f;

    [Export] public float HeatPerDamageTaken { get; set; } = 4f;

    // -- Drain -------------------------------------------------------------

    [Export] public float HeatPerPerfectParry { get; set; } = 12f;
    [Export] public float HeatPerBlockedParry { get; set; } = 6f;
    [Export] public float HeatPerKill { get; set; } = 8f;

    /// On top of HeatPerKill, for a member of HeatSourceGroup: killing the
    /// thing that was heating the room is worth more than killing a body.
    [Export] public float HeatSourceKillRelief { get; set; } = 15f;

    /// A slag vent (a Latch lever) opened. Once per vent — the dedupe is by
    /// position, because LeverThrown carries a place and not a node.
    [Export] public float HeatPerVent { get; set; } = 25f;
    [Export] public float VentDedupeRadius { get; set; } = 1.5f;

    // -- Thresholds --------------------------------------------------------

    [Export] public float KindledThreshold { get; set; } = 40f;
    [Export] public float MoltenThreshold { get; set; } = 70f;
    [Export] public float MaxHeat { get; set; } = 100f;

    // -- Flashover ---------------------------------------------------------

    [Export] public int FlashoverDamage { get; set; } = 20;

    /// Not 0. A flashover that handed back a clean bar would be a free reset
    /// for a player who had already lost control of the room.
    [Export] public float FlashoverResetTo { get; set; } = 60f;
    [Export] public float FlashoverRedSeconds { get; set; } = 2.5f;

    // -- Slag floor --------------------------------------------------------

    [Export] public float SlagGraceSeconds { get; set; } = 2.0f;
    [Export] public int SlagDamagePerTick { get; set; } = 2;
    [Export] public float SlagTickSeconds { get; set; } = 0.5f;

    // -- Parry economy -----------------------------------------------------

    [Export] public int ParryHealBase { get; set; } = 5;
    [Export] public int ParryHealPerStreak { get; set; } = 3;
    [Export] public int ParryHealStreakCap { get; set; } = 4;

    /// One heal per hurt-invulnerability window. The two numbers are equal on
    /// purpose: at a perfect streak the healing rate and the incoming damage
    /// rate are the same figure, so flawless play holds the line and nothing
    /// less does.
    [Export] public float ParryHealCooldownSeconds { get; set; } = 1.0f;

    /// What a perfect parry restores in a run where the Crucible was MASTERED
    /// in some earlier one. Flat, and deliberately a quarter of the branch's
    /// peak: the point is that the parry now always pays a little, not that
    /// the rest of the game inherits the Crucible's economy.
    [Export] public int MasteredParryHeal { get; set; } = 4;

    public const string FlagCleared = "crucible_cleared";
    public const string FlagMastered = "crucible_mastered";

    // -- State -------------------------------------------------------------

    /// True only inside the branch. Everything below is a no-op otherwise.
    public bool Active { get; private set; }

    public float Heat { get; private set; }
    public HeatState State { get; private set; } = HeatState.Tempered;
    public int ParryStreak { get; private set; }

    /// The Ember Sigil, for the rest of THIS run. It does not persist: what
    /// persists is the flag that says the branch was beaten, and the two are
    /// different promises.
    public bool SigilActive { get; private set; }

    /// Set at _Ready from the save, so every run after a flawless Crucible
    /// starts with the parry paying from room 1 on either path.
    public bool MasteredInAnEarlierRun { get; private set; }

    /// Flashovers since entering the branch. Zero of them, all the way to the
    /// Forgemaster, is what buys the permanent reward -- and it is counted
    /// rather than inferred, because "did the player ever lose control of the
    /// room" is not something the end state can be asked about afterwards.
    public int FlashoversThisBranch { get; private set; }

    /// 0..1, for anything drawing a bar.
    public float Heat01 => MaxHeat <= 0f ? 0f : Mathf.Clamp(Heat / MaxHeat, 0f, 1f);

    /// The floor burns at Molten, and for the whole flashover window whatever
    /// the value fell back to.
    public bool SlagActive => Active && (State == HeatState.Molten || _flashoverTimer > 0f);

    private float _baseGain;
    private float _flashoverTimer;
    private float _healCooldown;
    private float _groundedFor;
    private float _slagTickTimer;

    /// Set while the damage being dealt is a BURN rather than a BLOW.
    ///
    /// The design's "+4 heat on damage taken" is about blows. Applied to fire
    /// as well, the heat feeds itself: a slag tick emits PlayerDamaged,
    /// PlayerDamaged adds +4, and at two ticks a second that is +8/s on top of
    /// the base rate -- a molten room pushes a standing player to a flashover
    /// in about four seconds, and the flashover re-lights the floor. The same
    /// arithmetic breaks the Emberhusk: a 3.5-second pool ticking every 0.5s
    /// would add +28 where the design assigns the whole detonation +10.
    ///
    /// So every continuous fire in the branch routes through
    /// ApplyEnvironmentalBurn, which raises this for the duration of the tick.
    /// A detonation's blast, which goes through the ordinary damage path, is a
    /// blow and still pays its +4.
    ///
    /// The streak still breaks either way. Burning is damage, and the parry
    /// chain is broken by damage -- that rule has no exception.
    private bool _environmentalBurn;

    private float _lastBroadcastHeat01 = -1f;
    private HeatState _lastBroadcastState = (HeatState)(-1);
    private int _lastBroadcastStreak = -1;

    private readonly List<Vector3> _spentVents = new();
    private Node3D _player;
    private bool _subscribed;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        var bus = EventBus.Instance;
        if (bus == null)
        {
            GD.PushError("HeatManager: EventBus.Instance is null in _Ready — autoload order in project.godot must list EventBus before HeatManager.");
            return;
        }

        bus.BranchEntered += OnBranchEntered;
        bus.RoomEntered += OnRoomEntered;
        bus.Parried += OnParried;
        bus.EnemyDied += OnEnemyDied;
        bus.PlayerDamaged += OnPlayerDamaged;
        bus.PlayerDied += OnPlayerDied;
        bus.RestartRequested += OnRestartRequested;
        bus.RunCompleted += OnRunCompleted;
        bus.LeverThrown += OnLeverThrown;
        bus.EmberSigilGranted += OnEmberSigilGranted;
        _subscribed = true;

        MasteredInAnEarlierRun = Save.SaveManager.Instance?.GetWorldFlag(FlagMastered) ?? false;
        if (MasteredInAnEarlierRun)
            GD.Print("[Heat] the Crucible was mastered in an earlier run: the parry heals from room 1");
    }

    /// Unsubscribing matters even for an autoload: the bus outlives nothing
    /// here, but a quit that frees the autoloads in the wrong order leaves the
    /// bus emitting into a freed handler, and this project has already
    /// documented that failure once in EnemyController.
    public override void _ExitTree()
    {
        // First, and outside both guards below. If _Ready bailed because the
        // bus was not up yet, neither of them runs -- and a static pointing at
        // a freed node throws ObjectDisposedException on the next access,
        // which in a headless check reads as the manager being broken rather
        // than as the manager being gone.
        if (Instance == this) Instance = null;

        if (!_subscribed) return;
        var bus = EventBus.Instance;
        if (bus == null) return;

        bus.BranchEntered -= OnBranchEntered;
        bus.RoomEntered -= OnRoomEntered;
        bus.Parried -= OnParried;
        bus.EnemyDied -= OnEnemyDied;
        bus.PlayerDamaged -= OnPlayerDamaged;
        bus.PlayerDied -= OnPlayerDied;
        bus.RestartRequested -= OnRestartRequested;
        bus.RunCompleted -= OnRunCompleted;
        bus.LeverThrown -= OnLeverThrown;
        bus.EmberSigilGranted -= OnEmberSigilGranted;
        _subscribed = false;
    }

    // -- Branch lifecycle --------------------------------------------------

    private void OnBranchEntered(string branchName)
    {
        if (branchName == BranchName)
        {
            Activate();
            return;
        }

        // Any other branch portal — the Catacombs, the Act II shortcut — is a
        // way OUT of the Crucible as much as it is a way somewhere else.
        Deactivate();
    }

    private void Activate()
    {
        Active = true;
        Heat = Mathf.Clamp(EntryHeat, 0f, MaxHeat);
        SetStreak(0);
        _flashoverTimer = 0f;
        _healCooldown = 0f;
        _groundedFor = 0f;
        _slagTickTimer = 0f;
        _baseGain = HeatGainRoom4;
        FlashoversThisBranch = 0;
        _spentVents.Clear();
        RecomputeState();
        Broadcast(force: true);
        GD.Print($"[Heat] entered {BranchName}: heat {Heat:0} of {MaxHeat:0}");
    }

    private void Deactivate()
    {
        if (!Active) return;
        Active = false;
        Heat = 0f;
        SetStreak(0);
        _flashoverTimer = 0f;
        _groundedFor = 0f;
        _spentVents.Clear();
        State = HeatState.Tempered;
        Broadcast(force: true);

        // An empty name means "no branch". Nothing else says the Crucible is
        // over: room 4's door is an ordinary exit with no destination, and a
        // HeatChanged reading zero is indistinguishable from a player who
        // simply parried the room cold. Anything drawing the branch needs one
        // unambiguous edge, and this is it.
        //
        // This manager is itself a subscriber, so the emit re-enters
        // OnBranchEntered and calls back into here. Active was cleared at the
        // top, so the second call returns on its first line -- deliberate, and
        // noted because it is the kind of loop that only shows up at runtime.
        EventBus.Instance?.EmitBranchEntered(string.Empty);

        GD.Print("[Heat] left the branch; the fire is out");
    }

    /// The room index is what ends the branch, not the exit that started it:
    /// room 4's door is an ordinary exit with no destination name, so nothing
    /// else would ever say "you are out".
    private void OnRoomEntered(int index, int total)
    {
        if (!Active) return;

        if (index < FirstBranchRoom || index > LastBranchRoom)
        {
            Deactivate();
            return;
        }

        _baseGain = index >= LastBranchRoom ? HeatGainRoom5 : HeatGainRoom4;
        _player = null;   // the room was rebuilt; re-resolve on next use
        _groundedFor = 0f;
        _slagTickTimer = 0f;

        // The old room's levers were freed with it and the new room's are laid
        // out from x=0 by the same composer, so a room-5 vent can land within
        // the dedupe radius of a room-4 one. Kept across the rebuild, the list
        // would swallow a lever the player actually threw.
        _spentVents.Clear();

        // Forced, not opportunistic. Room 5 is entered through an ORDINARY
        // exit -- no destination name, so no BranchEntered -- and every enemy
        // in it caches the heat from this signal alone, starting at Tempered.
        // Waiting for the value to drift a hundredth of the bar is a third of
        // a second in which a freshly built room runs un-promoted telegraphs
        // and full-length cooldowns at Molten.
        Broadcast(force: true);
    }

    /// Dying costs the branch's progress but not the branch. The heat goes back
    /// to what it was at the door, because respawning into a room that is one
    /// second from a flashover is a death loop and not a punishment.
    private void OnPlayerDied()
    {
        if (!Active) return;
        Heat = Mathf.Clamp(EntryHeat, 0f, MaxHeat);
        SetStreak(0);
        _flashoverTimer = 0f;
        _groundedFor = 0f;
        _slagTickTimer = 0f;
        _healCooldown = 0f;
        RecomputeState();
        Broadcast(force: true);
    }

    /// A new run drops the Sigil. It is a reward for THIS run, and a restart is
    /// a different one -- the persistent half of the reward lives in the save.
    private void OnRestartRequested()
    {
        SigilActive = false;
        FlashoversThisBranch = 0;
        Deactivate();
    }

    private void OnRunCompleted(int roomsCleared) => Deactivate();

    // -- Per-frame ---------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (!Active) return;

        float dt = (float)delta;

        if (_healCooldown > 0f)
            _healCooldown = Mathf.Max(0f, _healCooldown - dt);

        if (_flashoverTimer > 0f)
            _flashoverTimer = Mathf.Max(0f, _flashoverTimer - dt);

        AddHeat(CurrentGain() * dt);

        if (Heat >= MaxHeat && _flashoverTimer <= 0f)
            TriggerFlashover();

        TickSlag(dt);

        RecomputeState();
        Broadcast(force: false);
    }

    /// Base rate plus every live heat source. A source that is dead but still
    /// lying there does not count: EnemyController keeps a corpse in the tree
    /// for 3.5 seconds, and a room that kept heating from bodies would make
    /// killing the Emberwright feel like it did nothing.
    public float CurrentGain() => _baseGain + HeatPerSourcePerSecond * LiveHeatSourceCount();

    public int LiveHeatSourceCount()
    {
        var tree = GetTree();
        if (tree == null) return 0;

        int live = 0;
        foreach (Node node in tree.GetNodesInGroup(HeatSourceGroup))
        {
            if (node is not AI.EnemyController enemy) continue;
            if (enemy.IsQueuedForDeletion()) continue;
            if (enemy.State == AI.EnemyController.EnemyState.Dead) continue;
            live++;
        }
        return live;
    }

    // -- Heat arithmetic ---------------------------------------------------

    /// The single writer. Everything that moves the number goes through here
    /// so the clamp exists in one place; a second clamp is how a bar ends up
    /// at 103.
    public void AddHeat(float delta)
    {
        if (!Active) return;
        Heat = Mathf.Clamp(Heat + delta, 0f, MaxHeat);
    }

    private void RecomputeState()
    {
        if (_flashoverTimer > 0f)
        {
            State = HeatState.Flashover;
            return;
        }

        if (Heat >= MoltenThreshold) State = HeatState.Molten;
        else if (Heat >= KindledThreshold) State = HeatState.Kindled;
        else State = HeatState.Tempered;
    }

    /// Only on a change, and only past a hundredth of the bar. The heat moves
    /// every physics frame, and 60 emissions a second on a bus whose other
    /// traffic is events is the mistake the Warden's health bar already
    /// documented.
    private void Broadcast(bool force)
    {
        float heat01 = Heat01;
        bool changed = force
            || State != _lastBroadcastState
            || Mathf.Abs(heat01 - _lastBroadcastHeat01) >= 0.01f;

        if (!changed) return;

        _lastBroadcastHeat01 = heat01;
        _lastBroadcastState = State;
        EventBus.Instance?.EmitHeatChanged(heat01, State);
    }

    // -- Flashover ---------------------------------------------------------

    private void TriggerFlashover()
    {
        _flashoverTimer = FlashoverRedSeconds;
        FlashoversThisBranch++;
        Heat = Mathf.Clamp(FlashoverResetTo, 0f, MaxHeat);
        SetStreak(0);
        _groundedFor = SlagGraceSeconds;   // the floor is already lit
        State = HeatState.Flashover;

        var player = ResolvePlayer();
        Vector3 at = player?.GlobalPosition ?? Vector3.Zero;

        // Through the ordinary damage path on purpose: a flashover respects
        // the hurt invulnerability window like everything else, so it cannot
        // stack on top of the blow that caused it.
        if (player is IDamageable damageable)
        {
            _environmentalBurn = true;
            damageable.TakeDamage(new DamageInfo
            {
                Amount = FlashoverDamage,
                SourcePosition = at + Vector3.Down,
                Knockback = Vector3.Zero,
                IsCritical = false,
                Telegraph = AttackTelegraphType.UnparryableRed,
            });
            _environmentalBurn = false;
        }

        EventBus.Instance?.EmitFlashover(at);
        Broadcast(force: true);
        GD.Print($"[Heat] FLASHOVER — {FlashoverDamage} damage, heat back to {Heat:0}");
    }

    // -- Slag floor --------------------------------------------------------

    private void TickSlag(float dt)
    {
        if (!SlagActive)
        {
            _groundedFor = 0f;
            _slagTickTimer = 0f;
            return;
        }

        var player = ResolvePlayer() as PlayerCamera.PlayerController;
        if (player == null) return;

        // A wall slide is not standing. It is the cheapest way to stop the
        // timer without leaving the fight, and it is deliberately the answer:
        // wall jump is the least-used ability in the game and this is the one
        // room that asks for it under pressure.
        bool grounded = player.IsOnFloor() && player.CurrentState != MovementState.WallSlide;

        if (!grounded)
        {
            _groundedFor = 0f;
            _slagTickTimer = 0f;
            return;
        }

        _groundedFor += dt;
        if (_groundedFor < SlagGraceSeconds) return;

        _slagTickTimer -= dt;
        if (_slagTickTimer > 0f) return;

        _slagTickTimer = SlagTickSeconds;
        ApplyEnvironmentalBurn(player, SlagDamagePerTick);
    }

    /// <summary>
    /// Burns the player as FIRE rather than as a blow: no stun, no knockback,
    /// no mercy window -- and no +4 heat, because a fire is not news about how
    /// the fight is going.
    ///
    /// Public because the branch's other two fires are not owned by this node.
    /// The Emberhusk's pool and the Slagbound's shell both burn continuously,
    /// and both would otherwise feed the heat several times a second through
    /// PlayerDamaged. Routing them here keeps ONE definition of the rule
    /// instead of three copies of a boolean.
    ///
    /// It does NOT check Active, and must not: outside the branch the damage
    /// still has to land, and the suppression is a no-op there anyway because
    /// AddHeat already refuses to move an inactive bar. The only case a caller
    /// has to handle itself is Instance being null at all, which happens in an
    /// isolated test scene that loads no autoloads -- hence the direct
    /// ApplyBurn fallback at both call sites.
    /// </summary>
    public void ApplyEnvironmentalBurn(PlayerCamera.PlayerController player, int amount)
    {
        if (player == null || amount <= 0) return;

        _environmentalBurn = true;
        player.ApplyBurn(amount);
        _environmentalBurn = false;
    }

    // -- Drains and gains --------------------------------------------------

    private void OnParried(bool perfect, Vector3 atPosition)
    {
        // Heat only moves inside the branch. Everything below this line can
        // happen outside it, which is the whole of what the Ember Sigil buys.
        if (Active)
            AddHeat(perfect ? -HeatPerPerfectParry : -HeatPerBlockedParry);

        // A blocked parry costs the attacker ground and cools the room, but it
        // is not the thing being taught. Only the perfect one pays.
        if (!perfect) return;

        // Computed BEFORE the increment, so the first perfect parry of a chain
        // heals the base amount and the fifth heals the cap. The table in the
        // design doc is this expression.
        int heal = ResolveParryHeal();
        SetStreak(ParryStreak + 1);

        if (heal <= 0) return;
        if (_healCooldown > 0f) return;
        _healCooldown = ParryHealCooldownSeconds;

        (ResolvePlayer() as PlayerCamera.PlayerController)?.Heal(heal);
    }

    /// <summary>
    /// What a perfect parry restores, and it is three different answers.
    ///
    /// Inside the Crucible, or anywhere once the Ember Sigil is held, it is the
    /// branch's escalating chain -- that economy is what the Sigil carries out
    /// of the branch with it. In a run following a MASTERED Crucible it is a
    /// flat four, everywhere, from room one: a permanent change to a rule
    /// rather than a bigger number, which is the meta-progression a Roguevania
    /// is actually for. Otherwise it is nothing, and the parry stays what it
    /// has always been -- a blow turned aside.
    /// </summary>
    public int ResolveParryHeal()
    {
        if (Active || SigilActive)
            return ParryHealBase + ParryHealPerStreak * Mathf.Min(ParryStreak, ParryHealStreakCap);

        return MasteredInAnEarlierRun ? MasteredParryHeal : 0;
    }

    /// <summary>
    /// The Forgemaster is down. Three consequences, and this node owns all
    /// three because it owns the two facts they depend on: whether the branch
    /// was flawless, and whether the parry keeps paying.
    /// </summary>
    private void OnEmberSigilGranted()
    {
        SigilActive = true;

        var save = Save.SaveManager.Instance;
        save?.SetWorldFlag(FlagCleared, true);

        // Mastery is the stricter claim and is asserted only from the counter.
        // "The player finished the branch" is not the same as "the room never
        // got away from them", and only the second one is worth a permanent
        // rule change.
        if (FlashoversThisBranch == 0)
        {
            save?.SetWorldFlag(FlagMastered, true);
            MasteredInAnEarlierRun = true;
            GD.Print("[Heat] Crucible MASTERED: no flashovers -- the parry heals in every run from now on");
        }
        else
        {
            GD.Print($"[Heat] Crucible cleared with {FlashoversThisBranch} flashover(s): the ground portal is open");
        }
    }

    private void OnEnemyDied(Node3D enemy)
    {
        if (!Active) return;

        AddHeat(-HeatPerKill);

        if (enemy != null && !enemy.IsQueuedForDeletion() && enemy.IsInGroup(HeatSourceGroup))
            AddHeat(-HeatSourceKillRelief);
    }

    private void OnPlayerDamaged(int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical)
    {
        // EmitSignal is synchronous, so the flag is still raised for exactly the
        // handlers of the tick that raised it and for nothing else.
        if (Active && !_environmentalBurn)
            AddHeat(HeatPerDamageTaken);

        // Outside the branch too: with the Sigil the chain is still paying, so
        // the rule that breaks it has to still be running. A streak that only
        // reset inside the Crucible would let a player carry a maxed chain
        // through the back half of the run and heal 17 a parry through
        // everything the game had left.
        SetStreak(0);
    }

    /// A struck lever inside the branch is a slag vent. Deduped by position
    /// because LeverThrown carries a place rather than a node -- the same
    /// reason Parried does -- and a vent that could be hit twice would be an
    /// unlimited supply of the branch's only non-renewable resource.
    private void OnLeverThrown(Vector3 atPosition)
    {
        if (!Active) return;

        foreach (Vector3 spent in _spentVents)
            if (spent.DistanceTo(atPosition) <= VentDedupeRadius)
                return;

        _spentVents.Add(atPosition);
        AddHeat(-HeatPerVent);
        GD.Print($"[Heat] vent opened: -{HeatPerVent:0} heat, now {Heat:0}");
    }

    // -- Streak ------------------------------------------------------------

    private void SetStreak(int value)
    {
        ParryStreak = Mathf.Max(0, value);
        if (ParryStreak == _lastBroadcastStreak) return;
        _lastBroadcastStreak = ParryStreak;
        EventBus.Instance?.EmitParryStreakChanged(ParryStreak);
    }

    // -- Player lookup -----------------------------------------------------

    /// By group, so this file carries no compile-time dependency on the player
    /// scene, and re-resolved whenever the cached node has gone: rooms are
    /// rebuilt constantly and a stale reference here would throw on the frame
    /// after a transition.
    private Node3D ResolvePlayer()
    {
        if (_player != null && IsInstanceValid(_player) && !_player.IsQueuedForDeletion())
            return _player;

        _player = GetTree()?.GetFirstNodeInGroup("player") as Node3D;
        return _player;
    }

    // -- Test seams --------------------------------------------------------

    /// For headless checks that need a known starting point without walking a
    /// player through a portal. Not a gameplay path: nothing in the game calls
    /// this, and the branch is entered by touching the Crucible exit.
    public void DebugForceActivate(float heat)
    {
        Activate();
        Heat = Mathf.Clamp(heat, 0f, MaxHeat);
        if (Heat >= MaxHeat)
            TriggerFlashover();
        else
        {
            RecomputeState();
            Broadcast(force: true);
        }
    }

    /// Lets a check drive the room index without a rebuild.
    public void DebugSetRoom(int index) => OnRoomEntered(index, LastBranchRoom + 1);
}
