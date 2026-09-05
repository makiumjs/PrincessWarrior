using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the last room is a boss fight, and the fight has a rule.
///
/// Four enemy types and no boss meant the run had no shape at the end: the
/// sixth room was the fifth room with more of the same in it, and the parry --
/// the mechanic with the most code behind it -- was never the cheapest way to
/// win anything, so a player had no reason to learn it.
///
/// What is asserted is the RULE, not the presence of a big enemy:
///   armoured, a heavy blow is reduced to chip damage and does not interrupt;
///   staggered by a perfect parry, the same blow lands in full;
///   the exit does not open while it lives, and does once it dies.
/// A check that only counted a boss node would pass with the armour deleted,
/// which is the entire fight.
public partial class BossFightTest : Node
{
    private const int Heavy = 55;

    private int _f;
    private DungeonRoomBuilder _room;
    private Warden _boss;
    private RoomExitTrigger _exit;

    private int _enemiesInRoom = -1;
    private int _chipDrop = -1;
    private int _openDrop = -1;
    private bool _staggeredByParry;
    private bool _chipDidNotInterrupt;
    private bool _sealedWhileAlive = true;
    private bool _runCompletedWhileAlive;
    private bool _runCompletedAfterKill;
    private bool _hudSawBoss;
    private bool _sealRemoved;

    public override void _Ready()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.RunCompleted += OnRunCompleted;
            EventBus.Instance.BossStateChanged += OnBossState;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.RunCompleted -= OnRunCompleted;
            EventBus.Instance.BossStateChanged -= OnBossState;
        }
    }

    private void OnRunCompleted(int rooms)
    {
        if (_boss != null && IsInstanceValid(_boss) && _boss.State != EnemyController.EnemyState.Dead)
            _runCompletedWhileAlive = true;
        else
            _runCompletedAfterKill = true;
    }

    private void OnBossState(bool alive, float frac, bool armoured, bool enraged) => _hudSawBoss = true;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_room == null) return;

        if (_f == 10)
        {
            _room.RebuildAs(_room.RunLength - 1);
            return;
        }

        if (_f == 30)
        {
            _boss = FindFirst<Warden>(_room);
            _exit = FindFirst<RoomExitTrigger>(_room);
            _enemiesInRoom = CountEnemies(_room);
            GD.Print($"[BOSS] room {_room.RoomIndex}: boss={_boss != null} enemies={_enemiesInRoom} exit={_exit != null}");
            if (_boss == null || _exit == null)
            {
                GD.Print("[BOSS] RESULT: FAIL (the last room has no boss, or no exit)");
                GetTree().Quit();
            }
            return;
        }

        // Armoured. A heavy blow, landed on a boss that is not staggered.
        if (_f == 40)
        {
            int before = _boss.CurrentHealth;
            Hit(_boss, Heavy);
            _chipDrop = before - _boss.CurrentHealth;
            // The other half of the armour, and the half a check that only
            // measured damage would miss: if chip hits still staggered it, the
            // player could stagger-lock the boss on 2 damage a swing and the
            // parry would stay optional.
            _chipDidNotInterrupt = _boss.State != EnemyController.EnemyState.Stagger;
            GD.Print($"[BOSS] armoured: {Heavy} damage removed {_chipDrop} hp; state={_boss.State}");
            return;
        }

        // Walk the player into the exit while the boss lives.
        if (_f == 55)
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            if (player != null) player.GlobalPosition = _exit.GlobalPosition;
            return;
        }

        if (_f == 75)
        {
            _sealedWhileAlive = !_runCompletedWhileAlive;
            _sealRemoved = _exit.GetNodeOrNull("ExitSeal") == null;
            GD.Print($"[BOSS] with the boss alive: runCompleted={_runCompletedWhileAlive} sealDrawn={!_sealRemoved}");

            // A perfect parry, announced the way the player's parry announces
            // it. What happens NEXT is the boss's own behaviour, and that is
            // what is under test here.
            EventBus.Instance?.EmitParried(true, _boss.GlobalPosition);
            return;
        }

        if (_f == 78)
        {
            _staggeredByParry = _boss.State == EnemyController.EnemyState.Stagger;
            int before = _boss.CurrentHealth;
            Hit(_boss, Heavy);
            _openDrop = before - _boss.CurrentHealth;
            GD.Print($"[BOSS] parried open: state={_boss.State} armoured={_boss.IsArmoured} " +
                     $"{Heavy} damage removed {_openDrop} hp");
            return;
        }

        // Kill it, then try the door again.
        if (_f == 90)
        {
            EventBus.Instance?.EmitParried(true, _boss.GlobalPosition);
            return;
        }
        if (_f == 93)
        {
            Hit(_boss, 500);
            GD.Print($"[BOSS] after the killing blow: state={_boss.State}");
            return;
        }

        if (_f == 110)
        {
            _sealRemoved = _exit.GetNodeOrNull("ExitSeal") == null;
            var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            if (player != null) player.GlobalPosition = _exit.GlobalPosition + new Vector3(0.2f, 0f, 0f);
            return;
        }

        if (_f == 140)
        {
            GD.Print($"[BOSS] after the kill: sealRemoved={_sealRemoved} runCompleted={_runCompletedAfterKill}");

            bool ok = _enemiesInRoom == 1          // the boss room is the boss, alone
                   && _chipDrop <= 3               // armour really absorbs
                   && _chipDrop > 0                // but is not invulnerability
                   && _chipDidNotInterrupt         // and a chip hit does not open it
                   && _staggeredByParry            // a perfect parry opens it
                   && _openDrop >= Heavy           // and the window is worth spending
                   && _sealedWhileAlive            // the run cannot end while it lives
                   && _sealRemoved                 // the seal is visibly gone
                   && _runCompletedAfterKill       // and the door then works
                   && _hudSawBoss;                 // the HUD was told any of this

            GD.Print(ok
                ? "[BOSS] RESULT: PASS (armoured until parried, and the way out is behind it)"
                : "[BOSS] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private static void Hit(Node3D target, int amount)
    {
        if (target is IDamageable d)
            d.TakeDamage(new DamageInfo
            {
                Amount = amount,
                SourcePosition = target.GlobalPosition + new Vector3(-1f, 0f, 0f),
                Knockback = new Vector3(2f, 1f, 0f),
                IsCritical = false,
            });
    }

    private static int CountEnemies(Node from)
    {
        int n = from is EnemyController ? 1 : 0;
        foreach (var c in from.GetChildren()) n += CountEnemies(c);
        return n;
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
