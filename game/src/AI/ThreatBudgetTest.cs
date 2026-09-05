using System.Collections.Generic;
using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: how much of the damage a passive player takes actually
/// comes from the ENEMIES.
///
/// The first version of this measured total damage and called it threat. It
/// passed with every enemy's AttackDamage set to zero -- 56 damage still
/// arrived, because a player standing in a generated room is also standing
/// near spike traps. It was measuring the room, not the roster.
///
/// So it runs the same exposure twice, at the same coordinates, with enemies
/// on and then off: the difference is what the enemies did. PlayerDamaged
/// carries a source position but not a source kind, and widening the signal to
/// suit a test would be the wrong trade -- a control run costs nothing and
/// answers exactly the question.
///
/// The bound is two-sided on purpose. Enemies that cannot hurt a stationary
/// player are decoration; an opening room that kills one in four seconds is
/// not an opening room.
public partial class ThreatBudgetTest : Node
{
    private const int ExposureFrames = 240;   // 4 seconds standing per room
    private const int SettleFrames = 30;
    private const int RoomsToMeasure = 4;

    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private bool _subscribed;

    private bool _controlPass;
    private int _damageThisRoom;
    private int _roomsMeasured;
    private readonly List<int> _withEnemies = new();
    private readonly List<int> _withoutEnemies = new();
    private readonly List<Vector3> _parkedAt = new();
    private int _deathsWithEnemies;
    private int _phaseStartFrame;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (!_subscribed)
        {
            _subscribed = true;
            EventBus.Instance.PlayerDamaged += (amount, _, _, _) => _damageThisRoom += amount;
            EventBus.Instance.PlayerDied += () => { if (!_controlPass) _deathsWithEnemies++; };
        }

        int cycle = ExposureFrames + SettleFrames;
        int local = _f - _phaseStartFrame;

        if (local > 0 && local % cycle == SettleFrames)
        {
            // Live pass parks beside an enemy, where a player fighting through
            // would be. The control replays the SAME coordinate, so the two
            // runs stand in the same place relative to the same spikes.
            if (!_controlPass)
            {
                var enemy = FirstOfType<EnemyController>(_room);
                var at = enemy != null
                    ? enemy.GlobalPosition + new Vector3(-2.5f, 1f, 0f)
                    : _player.GlobalPosition;
                _parkedAt.Add(at);
                _player.GlobalPosition = at;
            }
            else if (_roomsMeasured < _parkedAt.Count)
            {
                _player.GlobalPosition = _parkedAt[_roomsMeasured];
            }
            _damageThisRoom = 0;
        }

        if (local > 0 && local % cycle == 0)
        {
            (_controlPass ? _withoutEnemies : _withEnemies).Add(_damageThisRoom);
            _roomsMeasured++;

            if (_roomsMeasured >= RoomsToMeasure)
            {
                if (!_controlPass)
                {
                    // Same rooms, same spots, no enemies.
                    _controlPass = true;
                    _roomsMeasured = 0;
                    _phaseStartFrame = _f;
                    _room.SpawnEnemies = false;
                    _room.RebuildAs(0);
                    return;
                }
                Report();
                return;
            }

            var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
            if (exit != null && _room.RoomIndex < _room.RunLength - 1)
                _player.GlobalPosition = exit.GlobalPosition;
        }
    }

    private void Report()
    {
        int live = 0, control = 0;
        for (int i = 0; i < _withEnemies.Count && i < _withoutEnemies.Count; i++)
        {
            live += _withEnemies[i];
            control += _withoutEnemies[i];
        }
        int fromEnemies = live - control;

        GD.Print($"[THREAT] with enemies:    [{string.Join(",", _withEnemies)}] = {live}");
        GD.Print($"[THREAT] hazards only:    [{string.Join(",", _withoutEnemies)}] = {control}");
        GD.Print($"[THREAT] attributable to enemies: {fromEnemies} over {RoomsToMeasure} rooms of 4s exposure");
        GD.Print($"[THREAT] opening room, enemies on: {_withEnemies[0]} of 100 health; deaths: {_deathsWithEnemies}");

        bool ok = fromEnemies > 0            // enemies are a threat, not decoration
               && _withEnemies[0] < 100      // the opening room is survivable standing still
               && control >= 0;

        GD.Print(ok
            ? "[THREAT] RESULT: PASS (enemies account for real damage, and the opening room does not kill a passive player)"
            : "[THREAT] RESULT: FAIL");
        GetTree().Quit();
    }

    private static T FirstOfType<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren())
        {
            var f = FirstOfType<T>(c);
            if (f != null) return f;
        }
        return null;
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
