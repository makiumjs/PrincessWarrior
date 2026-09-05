using Godot;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the ChaseSteering hook actually lets a type retreat, and
/// the default behaviour is unchanged.
///
/// The measurement has to isolate the ENEMY's movement from the player's. A
/// hit knocks the player back, so raw distance between the two grows whether
/// or not the skirmisher retreated. Instead this records the player's X at the
/// moment the attack lands and then watches how far the enemy travels away
/// from THAT fixed point, which the player's knockback cannot inflate.
///
/// A BasicMelee runs the same measurement as a control. Without it the check
/// would only say "the skirmisher moved", not "it moved differently from every
/// other enemy" -- and a hook that changes nothing is the failure worth
/// catching.
public partial class SkirmisherTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;

    private EnemyController _subject;
    private bool _wasAttacking;
    private bool _armed;
    private float _pinnedPlayerX;
    private float _sign;
    private float _startAway;
    private float _maxAway;

    private float _skirmisherTravel = float.NaN;
    private float _meleeTravel = float.NaN;
    private double _gapSum; private int _gapSamples;
    private float _warlockGap = float.NaN, _meleeGap = float.NaN;
    private int _skirmishersInRoom = -1;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        // Counted BEFORE the room is cleared. A retreat that works perfectly on
        // an enemy the generator never places is content that does not exist,
        // which this project has shipped before.
        if (_f == 20)
        {
            _room.RebuildAs(1);
            _skirmishersInRoom = Collect<Skirmisher>(_room).Count;
            GD.Print($"[SKIRM] skirmishers placed in room 1: {_skirmishersInRoom}");
            ClearSpawnedEnemies();
        }

        if (_f == 60) Spawn("res://scenes/enemies/Skirmisher.tscn");
        if (_f == 250) { _skirmisherTravel = _maxAway - _startAway; Finish("skirmisher"); }

        if (_f == 280) Spawn("res://scenes/enemies/BasicMelee.tscn");
        if (_f == 500) Spawn("res://scenes/enemies/Warlock.tscn");
        if (_f == 470) { _meleeTravel = _maxAway - _startAway; _meleeGap = AverageGap(); Finish("basic melee"); }
        if (_f == 690) { _warlockGap = AverageGap(); Finish("warlock"); }

        Track();

        // Average distance held, sampled every frame. Travel-after-a-hit is the
        // skirmisher's signature; the warlock's is the gap it keeps at all
        // times, so they need different measurements.
        if (_subject != null && IsInstanceValid(_subject) && _f > 40)
        {
            _gapSum += Mathf.Abs(_subject.GlobalPosition.X - _player.GlobalPosition.X);
            _gapSamples++;
        }

        if (_f == 900)
        {
            GD.Print($"[SKIRM] average gap held — warlock: {_warlockGap:F1}, basic melee: {_meleeGap:F1}");
            GD.Print($"[SKIRM] travel away from the strike point \u2014 " +
                     $"skirmisher: {_skirmisherTravel:F2}, basic melee: {_meleeTravel:F2}");

            bool ok = _skirmishersInRoom > 0
                   && !float.IsNaN(_skirmisherTravel)
                   && !float.IsNaN(_meleeTravel)
                   && _skirmisherTravel > 1.5f              // it really backed off
                   && _skirmisherTravel > _meleeTravel + 1f  // and the default does not
                   && _warlockGap > _meleeGap + 2f;           // and a warlock keeps its distance

            GD.Print(ok
                ? "[SKIRM] RESULT: PASS (ChaseSteering lets a type retreat; the default still closes)"
                : "[SKIRM] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private void Spawn(string path)
    {
        _subject = GD.Load<PackedScene>(path).Instantiate<EnemyController>();
        _room.AddChild(_subject);
        _subject.GlobalPosition = _player.GlobalPosition + new Vector3(3f, 0f, 0f);
        _wasAttacking = false;
        _armed = false;
        _maxAway = 0f;
        _startAway = 0f;
    }

    /// Arms on the falling edge of the attack state: that is the instant the
    /// cooldown starts, which is exactly when a retreating type should turn
    /// around and a closing one should not.
    private void Track()
    {
        if (_subject == null || !IsInstanceValid(_subject)) return;

        bool attacking = _subject.State == EnemyController.EnemyState.Attack;
        if (_wasAttacking && !attacking && !_armed)
        {
            _armed = true;
            _pinnedPlayerX = _player.GlobalPosition.X;
            _sign = Mathf.Sign(_subject.GlobalPosition.X - _pinnedPlayerX);
            if (Mathf.IsZeroApprox(_sign)) _sign = 1f;
            _startAway = (_subject.GlobalPosition.X - _pinnedPlayerX) * _sign;
            _maxAway = _startAway;
        }
        _wasAttacking = attacking;

        if (_armed)
            _maxAway = Mathf.Max(_maxAway, (_subject.GlobalPosition.X - _pinnedPlayerX) * _sign);
    }

    private float AverageGap()
    {
        float g = _gapSamples > 0 ? (float)(_gapSum / _gapSamples) : 0f;
        _gapSum = 0; _gapSamples = 0;
        return g;
    }

    private void Finish(string label)
    {
        GD.Print($"[SKIRM] {label}: armed={_armed} startAway={_startAway:F2} maxAway={_maxAway:F2}");
        if (IsInstanceValid(_subject)) _subject.QueueFree();
        _subject = null;
    }

    /// The generated room spawns its own encounters; a second enemy wandering
    /// into the measurement would make it about whichever one arrived first.
    private void ClearSpawnedEnemies()
    {
        foreach (var e in Collect<EnemyController>(_room)) e.QueueFree();
    }

    private static System.Collections.Generic.List<T> Collect<T>(Node from) where T : Node
    {
        var found = new System.Collections.Generic.List<T>();
        if (from is T t) found.Add(t);
        foreach (var c in from.GetChildren()) found.AddRange(Collect<T>(c));
        return found;
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren())
        {
            var f = FindRoom(c);
            if (f != null) return f;
        }
        return null;
    }
}
