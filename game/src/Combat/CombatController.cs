using System.Collections.Generic;
using Godot;
using LostCrownlike.Core;
using LostCrownlike.PlayerCamera;

namespace LostCrownlike.Combat;

/// <summary>
/// Player-side melee combat: combo input buffering with a cancel window,
/// an Area3D hitbox enabled during an attack's active frames, and a brief
/// global hit-stop on a successful hit. See ARCHITECTURE.md "### Combat".
///
/// Attach as a child of the player CharacterBody3D (PlayerController) --
/// see scenes/player/PlayerTestScene.tscn and scenes/CombatTestScene.tscn,
/// both of which add a "CombatController" node as a child of "Player".
/// Reads PlayerController.CurrentState / FacingSign / CurrentHealth
/// read-only and never writes them, per the documented "Combat reads
/// PlayerController, does not modify it" contract.
///
/// The hitbox is built entirely in code (no scene authoring required) and
/// parented under this node, which is itself parented under the player.
/// PlayerController flips the whole body's RotationDegrees.Y between 0/180
/// to face left/right, so a local +X offset here inherits that rotation
/// and ends up on the correct side automatically -- this class never needs
/// to multiply by FacingSign for hitbox placement.
///
/// Hit-stop implementation choice: a global Engine.TimeScale dip rather
/// than per-node pause. Simpler, and arguably more correct for a
/// single-player action game, where a hit-stop is meant to read as a
/// shared moment of impact (the enemy's own flinch/reaction included) --
/// per-node pause would freeze the player's swing but let the enemy's
/// hurt animation keep playing underneath it, which looks wrong. The
/// downside (documented, not hidden): TimeScale is global, so it also
/// dips background systems like patrolling enemies elsewhere on screen for
/// the same brief window. Considered acceptable for a short (tens-of-ms)
/// dip. The restore is scheduled via a SceneTreeTimer created with
/// ignoreTimeScale: true, so the restore fires on real wall-clock time --
/// a timer counting down via scaled delta would nearly freeze itself out
/// at the same low TimeScale it's supposed to be undoing.
/// </summary>
[GlobalClass]
public partial class CombatController : Node3D
{
    private enum AttackPhase { None, Windup, Active, Recovery }
    private enum AttackType { Light, Heavy }

    // ------------------------------------------------------------------
    // Tunables required by ARCHITECTURE.md's "### Combat" section
    // ------------------------------------------------------------------
    [ExportGroup("Combat tunables")]
    [Export] public int ComboWindowMs = 400;
    /// The fallback, kept because TriggerHitStop's public overload defaults to
    /// it and something outside Combat may still call that. Every path INSIDE
    /// this class now names its own tier.
    [Export] public int HitStopDurationMs = 80;

    // -- Hit-stop, by the weight of the blow -------------------------------
    //
    // One duration for every impact is the reason a light jab and a fully
    // charged overhead felt the same in the hand: the freeze is most of what
    // the player reads as weight, and a constant one flattens the whole
    // moveset into a single verb. These are the four weights the game actually
    // has, priced apart far enough to be told apart without being counted.
    //
    // The freeze is GLOBAL (Engine.TimeScale), so these are deliberately short.
    // 90ms is already five frames of a near-stopped world; the next step up
    // stops reading as impact and starts reading as a hitch.

    /// A cut. Crisp, and over before it can interrupt a combo's rhythm.
    [Export] public int LightHitStopMs = 25;

    /// A blow. Long enough to feel the weight land.
    [Export] public int HeavyHitStopMs = 50;

    /// The most damaging thing the player owns, and the only one that gets to
    /// stop the world long enough to be felt as devastating.
    [Export] public int ChargedHitStopMs = 90;

    /// A guard that held. The same weight as a heavy hit, because that is what
    /// it is -- a blow, absorbed.
    [Export] public int BlockedParryHitStopMs = 50;

    /// The longest freeze in the game, and the only one that is cinematic
    /// rather than physical. The perfect parry is this project's signature
    /// move; 120ms is the beat that says so, and it is what the audio duck is
    /// timed against.
    [Export] public int PerfectParryHitStopMs = 120;
    [Export] public int LightAttackDamage = 10;
    [Export] public int HeavyAttackDamage = 25;

    // ------------------------------------------------------------------
    // Additional tunables needed to make the above actually playable
    // (additive, mirrors how PlayerController documents its "extra" exports)
    // ------------------------------------------------------------------
    [ExportGroup("Attack timing (per swing)")]
    [Export] public int LightWindupMs = 90;
    [Export] public int LightActiveMs = 110;
    [Export] public int LightRecoveryMs = 180;
    [Export] public int HeavyWindupMs = 200;
    [Export] public int HeavyActiveMs = 150;
    [Export] public int HeavyRecoveryMs = 350;
    [Export] public int ComboMaxSteps = 3;

    /// <summary>Measured from the moment Active begins. A "dash" input
    /// inside this window scraps whatever's left of the current attack
    /// (including any buffered follow-up); outside it, the swing finishes
    /// on its own. PlayerController's dash itself is never blocked by
    /// Combat either way -- this only resets Combat's own state so the
    /// player isn't left "finishing a swing" after they've already
    /// physically dashed away.</summary>
    [Export] public int CancelWindowMs = 250;

    [ExportGroup("Hitbox")]
    [Export] public float HitboxOffsetX = 0.9f;
    [Export] public float HitboxOffsetY = 0.1f;
    [Export] public Vector3 HitboxSize = new(1.2f, 1.6f, 1.4f);
    [Export] public float LightKnockback = 3f;
    [Export] public float HeavyKnockback = 6f;
    /// Multiplier applied to Engine.TimeScale during hit-stop. Not a hard 0:
    /// a zero timescale creates physics-step edge cases.
    [Export] public float HitStopTimeScale = 0.05f;

    // -- Charge attack ---------------------------------------------------
    /// How long attack_heavy must be held before the swing counts as charged.
    [Export] public int ChargeTimeMs = 450;
    /// Damage multiplier applied to a fully charged heavy attack.
    [Export] public float ChargeDamageMultiplier = 2.2f;

    /// What the charged heavy multiplies by once the Ember Sigil is held: 2.6
    /// against the base 2.2, which is 65 damage against 55.
    ///
    /// It does NOT accelerate the Warden. Armour is FLAT -- min(ChipDamage,
    /// amount) -- precisely so a bigger hammer is never the answer to it, so
    /// the Sigil buys shorter stagger windows and nothing else on that fight.
    /// That is the reward working as designed rather than a gap in it.
    [Export] public float SigilChargeMultiplier = 2.6f;

    /// The value to go back to when the run ends. Captured rather than
    /// hard-coded, so retuning the export above does not leave a second copy
    /// of 2.2 in this file to drift away from it.
    private float _baseChargeMultiplier;
    private bool _sigilSubscribed;
    [Export] public float ChargeKnockbackMultiplier = 1.8f;
    [Export] public int ComboDamageStepBonus = 2; // small flat escalation per combo step

    [ExportGroup("Debug")]
    [Export] public bool DebugLogging = true;

    /// <summary>Optional explicit link to the player. If unset, resolves
    /// via GetParent() (the expected setup -- this node as a child of the
    /// player) and falls back to the "player" group lookup.</summary>
    [Export] public NodePath PlayerPath;

    public bool IsAttacking => _phase != AttackPhase.None;

    private PlayerController _player;
    private Area3D _hitbox;
    private CollisionShape3D _hitboxShape;
    private readonly HashSet<ulong> _hitThisSwing = new();
    /// The factor THIS controller currently contributes to Engine.TimeScale.
    private float _selfAppliedTimeScale = 1f;
    private float _chargeHeldSeconds;
    private bool _releasedCharged;

    private AttackPhase _phase = AttackPhase.None;
    private AttackType _currentType;
    private int _comboStep;
    private float _phaseTimer;
    private float _comboWindowTimer;
    private float _sinceActiveStart;
    private bool _bufferedValid;
    private AttackType _bufferedType;

    public override void _Ready()
    {
        _baseChargeMultiplier = ChargeDamageMultiplier;

        // Subscribed here rather than reading a flag each swing: the multiplier
        // is read on every charged release, and a per-hit lookup into another
        // subsystem is the coupling this architecture spends a whole event bus
        // avoiding.
        if (EventBus.Instance != null)
        {
            EventBus.Instance.EmberSigilGranted += OnEmberSigilGranted;
            EventBus.Instance.RestartRequested += OnRunReset;
            EventBus.Instance.RunCompleted += OnRunCompleted;
            _sigilSubscribed = true;
        }

        _player = ResolvePlayer();

        _hitbox = new Area3D
        {
            Name = "MeleeHitbox",
            CollisionLayer = 0,
            CollisionMask = PhysicsLayers.Enemy,
            Monitoring = false,
            Monitorable = false,
        };
        AddChild(_hitbox);

        _hitboxShape = new CollisionShape3D { Shape = new BoxShape3D { Size = HitboxSize } };
        _hitbox.AddChild(_hitboxShape);
        UpdateHitboxTransform();

        if (EventBus.Instance != null)
            EventBus.Instance.Dashed += OnPlayerDashed;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
            EventBus.Instance.Dashed -= OnPlayerDashed;

        if (!_sigilSubscribed) return;
        if (EventBus.Instance != null)
        {
            EventBus.Instance.EmberSigilGranted -= OnEmberSigilGranted;
            EventBus.Instance.RestartRequested -= OnRunReset;
            EventBus.Instance.RunCompleted -= OnRunCompleted;
        }
        _sigilSubscribed = false;
    }

    private PlayerController ResolvePlayer()
    {
        if (PlayerPath != null && !PlayerPath.IsEmpty)
        {
            var explicitNode = GetNodeOrNull<PlayerController>(PlayerPath);
            if (explicitNode != null) return explicitNode;
        }
        if (GetParent() is PlayerController parentPlayer) return parentPlayer;
        return GetTree().GetFirstNodeInGroup("player") as PlayerController;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Attack pacing must run on UNSCALED time. Hit-stop dips
        // Engine.TimeScale to 0.05, and Godot scales the delta handed to
        // _PhysicsProcess by it — so with the scaled delta, one physics frame
        // advances only ~0.83ms of attack time instead of ~16.7ms, stretching
        // Windup/Active/Recovery by ~20x for the duration of the dip.
        // Measured consequence before this fix: a light attack's Active phase
        // ran 12+ physics frames instead of its configured window, the follow-up
        // light attack's windup took ~29 frames instead of ~5, and the third
        // scripted attack (heavy) never started at all because the previous
        // attack was still resolving — logged as "attack silently misses".
        // Hit-stop should freeze the WORLD, not reschedule the attack that
        // caused it.
        // Compensate ONLY for the dip Combat itself applied, not for
        // Engine.TimeScale as a whole. Dividing by the global scale also
        // cancelled any legitimate slow-motion the game might apply later
        // (a bullet-time ability, a pause ramp, a debug slowdown), silently
        // exempting attacks from it — a flaw the round-2 critic called out in
        // the previous version of this fix.
        var dt = (float)delta / Mathf.Max(_selfAppliedTimeScale, 0.0001f);

        TickParry(dt);

        var lightPressed = Input.IsActionJustPressed("attack_light");
        var heavyPressed = Input.IsActionJustPressed("attack_heavy");

        // Charge attack: hold heavy rather than tapping it. Gated on the
        // ability, which previously existed only as a flag and a HUD icon with
        // no mechanic behind it — the game showed the player an unlock for
        // something it could not do.
        bool canCharge = _player != null && _player.UnlockedAbilities.HasFlag(AbilityFlags.ChargeAttack);
        if (canCharge && Input.IsActionPressed("attack_heavy") && _phase == AttackPhase.None)
        {
            _chargeHeldSeconds += (float)delta / Mathf.Max(_selfAppliedTimeScale, 0.0001f);
        }
        if (canCharge && Input.IsActionJustReleased("attack_heavy"))
        {
            _releasedCharged = _chargeHeldSeconds >= ChargeTimeMs / 1000f;

            // Swing on EVERY release, not only a charged one. Firing only on a
            // full charge meant that unlocking the ability silently removed the
            // ordinary heavy attack: a short press was suppressed on the way in
            // and dropped on the way out, so nothing came out at all. An
            // upgrade must not take a move away. The hold only decides the
            // damage.
            heavyPressed = true;
            if (DebugLogging)
                GD.Print($"[Combat] heavy released after {_chargeHeldSeconds:F2}s (charged={_releasedCharged})");
            _chargeHeldSeconds = 0f;
        }
        // Tapping heavy while the ability is owned must not also fire on press,
        // or a charged swing would come out twice.
        if (canCharge && Input.IsActionJustPressed("attack_heavy"))
            heavyPressed = false;

        UpdatePhase(dt);
        HandleInput(lightPressed, heavyPressed);
        UpdateHitboxTransform();

        if (_phase == AttackPhase.Active)
            ProcessActiveHitbox();
    }

    private void HandleInput(bool lightPressed, bool heavyPressed)
    {
        if (!lightPressed && !heavyPressed) return;
        var type = heavyPressed ? AttackType.Heavy : AttackType.Light;

        if (_phase == AttackPhase.None)
        {
            if (!CanStartAttack()) return;
            BeginAttack(type, 0);
        }
        else if (_comboStep < ComboMaxSteps - 1 && CanBuffer())
        {
            _bufferedType = type;
            _bufferedValid = true;
        }
    }

    private bool CanBuffer() =>
        _phase == AttackPhase.Active || (_phase == AttackPhase.Recovery && _comboWindowTimer > 0f);

    private bool CanStartAttack()
    {
        if (_player == null) return true; // no player found (isolated test) -- don't block
        if (_player.CurrentHealth <= 0) return false;
        return _player.CurrentState is not (MovementState.Dash or MovementState.Hurt
            or MovementState.WallJump or MovementState.WallSlide);
    }

    private void BeginAttack(AttackType type, int step)
    {
        _currentType = type;
        _comboStep = step;
        _phase = AttackPhase.Windup;
        _phaseTimer = 0f;
        _sinceActiveStart = 0f;
        _bufferedValid = false;

        if (DebugLogging)
            GD.Print($"[Combat] begin {type} attack, combo step {step}");
    }

    private void UpdatePhase(float dt)
    {
        if (_phase == AttackPhase.None) return;

        _phaseTimer += dt;
        if (_phase is AttackPhase.Active or AttackPhase.Recovery)
            _sinceActiveStart += dt;

        switch (_phase)
        {
            case AttackPhase.Windup:
                if (_phaseTimer >= WindupSeconds())
                {
                    _phase = AttackPhase.Active;
                    _phaseTimer = 0f;
                    _hitThisSwing.Clear();
                    _hitbox.Monitoring = true;
                }
                break;

            case AttackPhase.Active:
                if (_phaseTimer >= ActiveSeconds())
                {
                    _phase = AttackPhase.Recovery;
                    _phaseTimer = 0f;
                    _comboWindowTimer = ComboWindowMs / 1000f;
                    _hitbox.Monitoring = false;
                }
                break;

            case AttackPhase.Recovery:
                if (_comboWindowTimer > 0f)
                    _comboWindowTimer -= dt;

                if (_bufferedValid)
                {
                    var next = _bufferedType;
                    var nextStep = _comboStep + 1;
                    if (nextStep >= ComboMaxSteps) nextStep = 0;
                    BeginAttack(next, nextStep);
                }
                else if (_phaseTimer >= RecoverySeconds() && _comboWindowTimer <= 0f)
                {
                    EndAttack();
                }
                break;
        }
    }

    private void EndAttack()
    {
        _phase = AttackPhase.None;
        _releasedCharged = false;
        _comboStep = 0;
        _bufferedValid = false;
        _hitbox.Monitoring = false;
    }

    private void OnPlayerDashed()
    {
        if (_phase is AttackPhase.Active or AttackPhase.Recovery &&
            _sinceActiveStart <= CancelWindowMs / 1000f)
        {
            if (DebugLogging)
                GD.Print("[Combat] attack canceled into dash");
            EndAttack();
        }
    }

    private void ProcessActiveHitbox()
    {
        foreach (var body in _hitbox.GetOverlappingBodies())
        {
            if (body is not IDamageable damageable) continue;
            if (ReferenceEquals(body, _player)) continue;

            var id = body.GetInstanceId();
            if (_hitThisSwing.Contains(id)) continue;
            _hitThisSwing.Add(id);

            ApplyHit(body, damageable);
        }
    }

    private void ApplyHit(Node3D target, IDamageable damageable)
    {
        var baseDamage = _currentType == AttackType.Heavy ? HeavyAttackDamage : LightAttackDamage;
        if (_releasedCharged) baseDamage = Mathf.RoundToInt(baseDamage * ChargeDamageMultiplier);
        var damage = baseDamage + _comboStep * ComboDamageStepBonus;
        var knockbackMag = _currentType == AttackType.Heavy ? HeavyKnockback : LightKnockback;
        if (_releasedCharged) knockbackMag *= ChargeKnockbackMultiplier;

        var sourcePos = GlobalPosition;
        var away = target.GlobalPosition - sourcePos;
        away.Y = 0.3f; // slight upward pop on hit
        if (away.Length() < 0.01f) away = new Vector3(_player?.FacingSign ?? 1, 0.3f, 0f);

        var info = new DamageInfo
        {
            Amount = damage,
            SourcePosition = sourcePos,
            Knockback = away.Normalized() * knockbackMag,
            IsCritical = _currentType == AttackType.Heavy && _comboStep == ComboMaxSteps - 1,
        };

        damageable.TakeDamage(info);
        EventBus.Instance?.EmitEnemyDamaged(target, info);
        TriggerHitStop(HitStopForSwing());

        if (DebugLogging)
            GD.Print($"[Combat] hit {target.Name} for {damage} damage ({_currentType}, combo {_comboStep})");
    }

    /// <summary>
    /// How long THIS swing should stop the world for. Charged is checked before
    /// heavy because a charged attack is also a heavy one, and asking in the
    /// other order would price every charged hit at 50ms and quietly delete the
    /// tier that matters most.
    /// </summary>
    public int HitStopForSwing()
    {
        if (_releasedCharged) return ChargedHitStopMs;
        return _currentType == AttackType.Heavy ? HeavyHitStopMs : LightHitStopMs;
    }

    /// Whether the swing being resolved was a released charge. Public so the
    /// visual can pick a trail that matches the blow rather than keeping its
    /// own idea of what a charged attack is.
    public bool SwingIsCharged => _releasedCharged;

    public void TriggerHitStop(int durationMs = -1)
    {
        int dur = durationMs > 0 ? durationMs : HitStopDurationMs;
        if (dur <= 0 || GetTree() == null) return;
        if (_selfAppliedTimeScale < 1f) return; // Prevent compounding if already in hit-stop
        _selfAppliedTimeScale = HitStopTimeScale;
        Engine.TimeScale *= HitStopTimeScale;
        var timer = GetTree().CreateTimer(dur / 1000.0, processAlways: true,
            processInPhysics: false, ignoreTimeScale: true);
        timer.Timeout += () =>
        {
            // Divide back out rather than assigning 1.0: assigning would stomp
            // any other slow-motion that started while hit-stop was running.
            Engine.TimeScale /= HitStopTimeScale;
            _selfAppliedTimeScale = 1f;
        };
    }

    private void UpdateHitboxTransform()
    {
        if (_hitbox == null) return;
        _hitbox.Position = new Vector3(HitboxOffsetX, HitboxOffsetY, 0f);
        if (_hitboxShape.Shape is BoxShape3D box)
            box.Size = HitboxSize;
    }

    // ------------------------------------------------------------------
    // Parry
    // ------------------------------------------------------------------

    [ExportGroup("Parry")]
    /// How long the guard stays up after a press.
    [Export] public float ParryWindowMs = 380f;

    /// The opening slice of that window that counts as perfect (220ms per brief item 06).
    [Export] public float PerfectParryMs = 220f;

    /// Stops the guard being held permanently by spamming the key.
    [Export] public float ParryCooldownMs = 520f;

    private float _parryTimer;
    private float _parryCooldown;
    private float _parryHeldFor;

    public enum ParryResult { None, Blocked, Perfect }

    /// True while the guard is up, for the visual.
    public bool IsParrying => _parryTimer > 0f;

    /// 0..1 through the guard, for the visual.
    public float ParryProgress => _parryTimer <= 0f
        ? 0f
        : 1f - Mathf.Clamp(_parryTimer / (ParryWindowMs / 1000f), 0f, 1f);

    public float EffectivePerfectParryMs => PerfectParryMs + (Save.SaveManager.Instance?.GetWorldFlag("rune_parry_window") == true ? 30f : 0f);

    public bool ParryIsPerfectNow => _parryTimer > 0f && _parryHeldFor <= EffectivePerfectParryMs / 1000f;
    public float ParryHeldFor => _parryHeldFor;
    public float TimeSinceLastParryPress { get; private set; } = 999f;
    public float ParryCooldownRemaining => _parryCooldown;

    private void TickParry(float dt)
    {
        TimeSinceLastParryPress += dt;
        if (_parryCooldown > 0f) _parryCooldown = Mathf.Max(0f, _parryCooldown - dt);

        if (_parryTimer > 0f)
        {
            _parryTimer = Mathf.Max(0f, _parryTimer - dt);
            _parryHeldFor += dt;
        }

        if (Input.IsActionJustPressed("parry"))
        {
            TimeSinceLastParryPress = 0f;
            if (_parryTimer <= 0f && _parryCooldown <= 0f)
            {
                _parryTimer = ParryWindowMs / 1000f;
                _parryHeldFor = 0f;
                _parryCooldown = ParryCooldownMs / 1000f;
            }
        }
    }

    /// <summary>
    /// Explicitly activates parry guard (used by test harness and abilities).
    /// </summary>
    public void StartParry()
    {
        _parryTimer = ParryWindowMs / 1000f;
        _parryHeldFor = 0f;
        _parryCooldown = 0f;
        TimeSinceLastParryPress = 0f;
    }

    /// Called by the player when a blow is about to land. Consumes the guard:
    /// one press turns aside one blow, so a parry cannot cover a whole
    /// exchange.
    public ParryResult TryConsumeParry()
    {
        if (_parryTimer <= 0f) return ParryResult.None;

        bool perfect = _parryHeldFor <= EffectivePerfectParryMs / 1000f;
        _parryTimer = 0f;
        return perfect ? ParryResult.Perfect : ParryResult.Blocked;
    }

    /// Swing state, for the visual to draw. The free KayKit pack ships no
    /// attack clip -- the two animation libraries here cover movement, hits
    /// TAKEN, death and item use, and nothing else -- so the swing is animated
    /// procedurally from these. Reported from play as "the attack animations
    /// are missing", which was literally true: ClipFor had no attack case
    /// because there was no clip to name.
    public bool IsSwinging => _phase is AttackPhase.Windup or AttackPhase.Active;

    public bool IsActivePhase => _phase == AttackPhase.Active;

    public int ComboStep => _comboStep;

    public float CurrentAttackTotalDuration => WindupSeconds() + ActiveSeconds() + RecoverySeconds();

    /// 0 at the start of the windup, 1 at the end of the active frames.
    public float SwingProgress
    {
        get
        {
            float windup = WindupSeconds(), active = ActiveSeconds();
            float total = Mathf.Max(windup + active, 0.0001f);
            float elapsed = _phase switch
            {
                AttackPhase.Windup => _phaseTimer,
                AttackPhase.Active => windup + _phaseTimer,
                _ => total,
            };
            return Mathf.Clamp(elapsed / total, 0f, 1f);
        }
    }

    /// True while the heavier swing is out, so the visual can make it bigger.
    public bool SwingIsHeavy => _currentType == AttackType.Heavy;

    private float WindupSeconds() => (_currentType == AttackType.Heavy ? HeavyWindupMs : LightWindupMs) / 1000f;
    private float ActiveSeconds() => (_currentType == AttackType.Heavy ? HeavyActiveMs : LightActiveMs) / 1000f;
    private float RecoverySeconds() => (_currentType == AttackType.Heavy ? HeavyRecoveryMs : LightRecoveryMs) / 1000f;
    /// <summary>
    /// The Ember Sigil sharpens the charged attack for the rest of the run.
    /// Assignment rather than multiplication: granting it twice in one run --
    /// which cannot happen today and would be free to happen tomorrow -- must
    /// not stack into a 3.1x hammer.
    /// </summary>
    private void OnEmberSigilGranted()
    {
        ChargeDamageMultiplier = SigilChargeMultiplier;
        GD.Print($"[Combat] Ember Sigil: charged attack x{ChargeDamageMultiplier:0.0#}");
    }

    private void OnRunCompleted(int roomsCleared) => OnRunReset();

    /// A reward for THIS run does not survive it. Restored from the captured
    /// base rather than from a literal, so the two cannot disagree.
    private void OnRunReset() => ChargeDamageMultiplier = _baseChargeMultiplier;

}
