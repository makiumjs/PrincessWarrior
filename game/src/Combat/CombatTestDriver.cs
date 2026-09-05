using Godot;
using LostCrownlike.AI;
using LostCrownlike.Core;
using LostCrownlike.PlayerCamera;

namespace LostCrownlike.Combat;

/// <summary>
/// TEST-SCENE ONLY. Drives scenes/CombatTestScene.tscn: bakes the test
/// navmesh, forces the player adjacent to the enemy, simulates an
/// attack_light press via Input.ActionPress/ActionRelease (these set the
/// action's internal pressed state directly and work headless, no real
/// input event needed), then verifies EventBus.EnemyDamaged actually fired
/// and the enemy's own state machine transitioned to Stagger/Dead before
/// quitting the tree with an explicit PASS/FAIL line in the log. Not part
/// of Combat's public contract -- mirrors the pattern already used by
/// src/AI/EnemyTestSceneBootstrap.cs and src/AI/PlayerStandIn.cs for their
/// own test scenes.
/// </summary>
[GlobalClass]
public partial class CombatTestDriver : Node3D
{
    [Export] public NodePath NavigationRegionPath;
    [Export] public NodePath PlayerNodePath;
    [Export] public NodePath EnemyPath;

    private PlayerController _player;
    private EnemyController _enemy;
    private int _frame;
    private bool _enemyDamagedFired;
    private bool _sawStagger;
    private DamageInfo _lastDamage;

    public override void _Ready()
    {
        var region = GetNodeOrNull<NavigationRegion3D>(NavigationRegionPath);
        region?.BakeNavigationMesh(false);

        _player = GetNodeOrNull<PlayerController>(PlayerNodePath);
        _enemy = GetNodeOrNull<EnemyController>(EnemyPath);

        if (EventBus.Instance != null)
            EventBus.Instance.EnemyDamaged += OnEnemyDamaged;

        GD.Print($"[CombatTestDriver] ready. player={_player != null} enemy={_enemy != null}");
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
            EventBus.Instance.EnemyDamaged -= OnEnemyDamaged;
    }

    private void OnEnemyDamaged(Node3D enemy, int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical)
    {
        var info = new DamageInfo { Amount = amount, SourcePosition = sourcePosition, Knockback = knockback, IsCritical = isCritical };
        _enemyDamagedFired = true;
        _lastDamage = info;
        GD.Print($"[CombatTestDriver] EnemyDamaged event: target={enemy.Name} amount={info.Amount} crit={info.IsCritical}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        if (_enemy != null && _enemy.State == EnemyController.EnemyState.Stagger)
            _sawStagger = true;

        if (_frame == 14 && _player != null && _enemy != null)
        {
            _player.GlobalPosition = _enemy.GlobalPosition + new Vector3(-1.0f, 0.1f, 0f);
            GD.Print($"[CombatTestDriver] positioned player at {_player.GlobalPosition}, enemy at {_enemy.GlobalPosition}");
        }
        else if (_frame == 15)
        {
            Input.ActionPress("attack_light");
            GD.Print("[CombatTestDriver] pressed attack_light");
        }
        else if (_frame == 16)
        {
            Input.ActionRelease("attack_light");
        }
        else if (_frame == 90)
        {
            var enemyState = _enemy?.State.ToString() ?? "null";
            // Evidence a hit actually landed and was processed by the enemy's
            // own state machine (not just that the event fired): the enemy
            // must have visibly reacted (Stagger) or died from it. By frame
            // 90 (1.5s) it may have already recovered out of Stagger back
            // into Chase/Patrol -- that recovery is itself expected AI
            // behavior, so _sawStagger (recorded every frame) is the check,
            // not the state at this one instant.
            var pass = _enemyDamagedFired && _lastDamage.Amount == 10 &&
                       (_sawStagger || _enemy?.State == EnemyController.EnemyState.Dead);
            GD.Print(pass
                ? $"[CombatTestDriver] TEST PASSED: enemy took {_lastDamage.Amount} damage, sawStagger={_sawStagger}, stateNow={enemyState}"
                : $"[CombatTestDriver] TEST FAILED: damagedEvent={_enemyDamagedFired} sawStagger={_sawStagger} enemyState={enemyState}");
            // Do NOT quit here: this scene is also driven by the generic
            // CaptureRunner harness, which owns the run's lifetime. Quitting at
            // a fixed frame truncated every capture (and produced an empty
            // events.csv before CaptureRunner gained AutoFlush), hiding whether
            // later scripted attacks ever landed.
        }
    }
}
