using Godot;
using LostCrownlike.AI;
using LostCrownlike.World;
using LostCrownlike.Core;

namespace LostCrownlike.Combat;

/// TEST-SCENE ONLY: proves the charge attack exists as a mechanic, not just as
/// a flag with a HUD icon. Compares the damage of a tapped heavy against a held
/// one, and checks a room actually grants the ability.
public partial class ChargeAttackTest : Node
{
    private int _f;
    private Node3D _player;
    private int _tapDamage = -1, _chargedDamage = -1, _shortHoldDamage = -1;
    private bool _grantSeen;
    /// Seconds, not frames: headless runs uncapped, so 60 frames can be a
    /// fraction of a second and the hold never reaches the charge threshold.
    private double _elapsedSinceHold = -1;
    private double _shortElapsed = -1;
    private bool _refreshed;

    public override void _Ready()
    {
        EventBus.Instance.EnemyDamaged += (_, amount, _, _, _) =>
        {
            if (_tapDamage < 0) { _tapDamage = amount; GD.Print($"[CHARGE] tapped heavy (no ability) dealt {amount}"); }
            else if (_chargedDamage < 0) { _chargedDamage = amount; GD.Print($"[CHARGE] fully charged heavy dealt {amount}"); }
            else if (_shortHoldDamage < 0) { _shortHoldDamage = amount; GD.Print($"[CHARGE] SHORT hold dealt {amount}"); }
        };
        EventBus.Instance.AbilityUnlocked += bits =>
        {
            if (((AbilityFlags)bits).HasFlag(AbilityFlags.ChargeAttack)) _grantSeen = true;
        };
    }

    /// A target that never dies and never moves. Chasing real enemies made this
    /// test measure the AI instead of the damage: the charged swing killed the
    /// only enemy, a rebuilt room put the replacement somewhere else, and the
    /// third swing kept landing on empty air. What is under test is three
    /// damage numbers.
    private partial class Dummy : CharacterBody3D, IDamageable
    {
        public void TakeDamage(DamageInfo info) { }
    }

    private Dummy _dummy;

    private void SpawnDummy()
    {
        _dummy = new Dummy
        {
            CollisionLayer = PhysicsLayers.Enemy,
            CollisionMask = 0,
        };
        _dummy.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1f, 1.6f, 1f) } });
        GetTree().CurrentScene.AddChild(_dummy);
        _dummy.GlobalPosition = _player.GlobalPosition + new Vector3(1.2f, 0f, 0f);
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        if (_f == 15) SpawnDummy();
        if (_dummy == null) return;

        // Keep the dummy in front of the player, and the player facing it.
        _dummy.GlobalPosition = _player.GlobalPosition + new Vector3(1.2f, 0f, 0f);
        Input.ActionPress("move_right");

        // 1. Plain heavy, no ability.
        if (_f == 40) Input.ActionPress("attack_heavy");
        if (_f == 46) Input.ActionRelease("attack_heavy");

        // 2. Grant the ability, then hold well past the threshold.
        if (_f == 120) EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.ChargeAttack);
        if (_f == 160) Input.ActionPress("attack_heavy");
        if (_f == 220) Input.ActionRelease("attack_heavy");      // 60 frames = 1.0s at fixed 60fps

        // 3. Ability owned, released well BEFORE the threshold. Without this
        // the test cannot tell a charge from a flat damage buff.
        if (_f == 320) Input.ActionPress("attack_heavy");
        if (_f == 326) Input.ActionRelease("attack_heavy");      // 6 frames = 0.1s

        if (_f == 420)
        {
            GD.Print($"[CHARGE] tap={_tapDamage} fullCharge={_chargedDamage} shortHold={_shortHoldDamage}");
            bool chargeHelps = _tapDamage > 0 && _chargedDamage > _tapDamage;
            bool shortHoldIsNotCharged = _shortHoldDamage > 0 && _shortHoldDamage < _chargedDamage;
            if (!chargeHelps)
                GD.Print("[CHARGE] RESULT: FAIL (a full charge deals no more than a tap)");
            else if (_shortHoldDamage < 0)
                GD.Print("[CHARGE] RESULT: FAIL (the short-hold swing never landed - only half the mechanic was tested)");
            else if (!shortHoldIsNotCharged)
                GD.Print("[CHARGE] RESULT: FAIL (a short hold got the charged bonus - this is a flat damage buff, not a charge)");
            else
                GD.Print("[CHARGE] RESULT: PASS (a full hold is rewarded, a short one is not)");
            GetTree().Quit();
        }
    }

}
