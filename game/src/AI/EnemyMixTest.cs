using System.Collections.Generic;
using Godot;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the three enemy types across a WHOLE run, together.
///
/// Each type has its own check, and each passes in isolation. That is not the
/// same claim as this one. The type cycle is a property of the run, not of a
/// room: it has already been wrong twice in ways a single-room test could not
/// see -- once shifted by the skipped spawn gauntlet, once restarting per room
/// so the third type appeared nowhere at all. Both were invisible in the code
/// and obvious the moment something counted what six consecutive rooms
/// actually contained.
///
/// It also watches for accumulation. Sentries fire bolts, rooms are torn down
/// and rebuilt underneath them, and an entity that outlives its room is the
/// leak this project has already had once.
public partial class EnemyMixTest : Node
{
    private const int StepFrames = 70;

    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;

    private readonly HashSet<string> _typesSeen = new();
    private readonly List<int> _enemiesPerRoom = new();
    private readonly List<int> _roomsVisited = new();
    private int _emptyRooms;
    private int _peakBolts;
    private int _nodesAtRoom1;
    private int _nodesAtLastRoom;
    private bool _boltWasInFlight;
    private bool _rebuiltUnderBolt;
    private int _boltsAfterTeardown = -1;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        // A rebuild now runs one step per frame instead of all on the frame the
        // player crosses a door, so for a few frames the room exists but its
        // enemies, pickups or exit do not yet. Asserting inside that window
        // reads a half-built room and reports content as missing.
        if (_room != null && _room.IsRebuilding) return;


        _peakBolts = Mathf.Max(_peakBolts, Count<Projectile>(_room));

        // Survey just before moving on, so the room has had time to build.
        if (_f > StepFrames && (_f + 10) % StepFrames == 0 && _room.RoomIndex < _room.RunLength - 1)
            Survey();

        if (_f % StepFrames == 0 && _f / StepFrames <= _room.RunLength && _room.RoomIndex < _room.RunLength - 1)
        {
            // The halfway room seals behind the Sentinel, and this check walks
            // the run by teleporting onto exits -- so it parked there and
            // surveyed six rooms instead of nine, leaving the second half with
            // one sample. Freeing the sealed fight rather than winning it, the
            // same way the traversal bot does: whether a door holds is check
            // 60's question, and answering it twice in two places is how a
            // suite ends up with two truths about one mechanic.
            foreach (var n in GetTree().GetNodesInGroup("boss")) n.QueueFree();

            var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
            if (exit != null) _player.GlobalPosition = exit.GlobalPosition;
        }

        // Park next to a sentry so one actually fires. Teleporting from exit to
        // exit means the player is never in view long enough for the 0.55s
        // windup, so the first version of this test reported "peak bolts in
        // flight: 0" and its cleanup assertion was vacuous -- it proved that
        // zero bolts had been cleaned up.
        // The run now ENDS in the boss room, and the boss is melee until it is
        // half dead -- so the room the test comes to rest in no longer contains
        // anything that fires. Step back to the last room that does, after
        // recording the boss room. Without this the bolt assertions went quiet
        // and, worse, went quiet in the shape of a pass: "0 bolts left after
        // teardown" is trivially true when no bolt was ever created. That is
        // the sixth vacuous assertion this suite has produced, and the third
        // found by something else changing underneath it.
        if (_f == StepFrames * (_room.RunLength + 1) - 10)
        {
            Survey();
            if (FirstOfType<CrossbowSentry>(_room) == null)
                _room.RebuildAs(_room.RunLength - 2);
        }

        if (_f == StepFrames * (_room.RunLength + 1) + 10)
        {
            var sentry = FirstOfType<CrossbowSentry>(_room);
            if (sentry != null)
                _player.GlobalPosition = sentry.GlobalPosition + new Vector3(-5f, 0f, 0f);
        }

        if (_boltWasInFlight || Count<Projectile>(_room) > 0) _boltWasInFlight = true;

        // Tear the room down WITH a bolt in the air. That is the integration
        // risk the per-type checks cannot see: an Area3D freed by RebuildAs in
        // the middle of the physics step it is moving through.
        if (!_rebuiltUnderBolt && _boltWasInFlight && Count<Projectile>(_room) > 0)
        {
            _rebuiltUnderBolt = true;
            _room.RebuildAs(_room.RoomIndex);
        }

        if (_f == StepFrames * (_room.RunLength + 3))
        {
            _boltsAfterTeardown = Count<Projectile>(_room);
            Survey();
            _nodesAtLastRoom = CountAll(_room);

            GD.Print($"[MIX] rooms surveyed: [{string.Join(",", _roomsVisited)}]");
            GD.Print($"[MIX] enemies per room: [{string.Join(",", _enemiesPerRoom)}], empty rooms: {_emptyRooms}");
            GD.Print($"[MIX] types seen across the run: {string.Join(", ", _typesSeen)}");
            GD.Print($"[MIX] peak bolts in flight: {_peakBolts}, a bolt was airborne: {_boltWasInFlight}");
            GD.Print($"[MIX] room torn down under a live bolt: {_rebuiltUnderBolt}, bolts left after: {_boltsAfterTeardown}");
            GD.Print($"[MIX] room node count: first {_nodesAtRoom1} -> last {_nodesAtLastRoom}");

            // Does the run get heavier, or does it just get longer? The ramp
            // scaled obstacle SIZE and nothing else, so a ten-room run read
            // 4, 5, 8, 4, 5, 8, 4, 5, 8 enemies -- the layout cycle repeating.
            // BOTH sealed fights are left out of both halves: each holds one
            // enemy by design, and counting them would make a heavier back half
            // look lighter. The halfway room joined the boss room in that class
            // when the Sentinel went in, and this check found it -- the second
            // half's average fell to 2.5 the moment a room of nine enemies
            // became a room of one.
            int front = 0, back = 0, frontRooms = 0, backRooms = 0;
            int last = _room.RunLength - 1;
            int halfway = _room.RunLength / 2;
            for (int i = 0; i < _roomsVisited.Count && i < _enemiesPerRoom.Count; i++)
            {
                int index = _roomsVisited[i];
                if (index == last || index == halfway) continue;
                if (index < _room.RunLength / 2) { front += _enemiesPerRoom[i]; frontRooms++; }
                else { back += _enemiesPerRoom[i]; backRooms++; }
            }
            float frontAvg = frontRooms == 0 ? 0f : (float)front / frontRooms;
            float backAvg = backRooms == 0 ? 0f : (float)back / backRooms;
            GD.Print($"[MIX] enemies per room, first half {frontAvg:F1} ({frontRooms} rooms) " +
                     $"vs second half {backAvg:F1} ({backRooms} rooms)");

            bool ok = _typesSeen.Count >= 4
                   && backRooms > 0 && frontRooms > 0
                   && backAvg > frontAvg * 1.2f
                   && _roomsVisited.Count >= 3
                   && _emptyRooms == 0
                   && _boltWasInFlight          // a sentry really did fire
                   && _rebuiltUnderBolt         // and the room really was torn down under it
                   && _boltsAfterTeardown == 0
                   // Rooms differ in length, so node counts differ; this only
                   // catches runaway growth, not layout variation.
                   && _nodesAtLastRoom < _nodesAtRoom1 * 3;

            GD.Print(ok
                ? $"[MIX] RESULT: PASS ({_typesSeen.Count} enemy types appear across a run, every room is populated, nothing accumulates)"
                : "[MIX] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private void Survey()
    {
        int idx = _room.RoomIndex;
        if (_roomsVisited.Contains(idx)) return;
        _roomsVisited.Add(idx);

        // Counted by CONCRETE TYPE NAME rather than against a hardcoded list.
        // The list version went stale the moment a fourth type was added: a room
        // full of warlocks counted as zero enemies and the check called it an
        // empty room. A roster test that has to be edited every time the roster
        // grows will be wrong exactly when it matters.
        var here = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var e in CollectEnemies(_room))
        {
            string t = e.GetType().Name;
            here[t] = here.TryGetValue(t, out int n) ? n + 1 : 1;
            _typesSeen.Add(t);
        }

        int total = 0;
        foreach (var kv in here) total += kv.Value;
        _enemiesPerRoom.Add(total);
        if (total == 0) _emptyRooms++;

        if (_nodesAtRoom1 == 0) _nodesAtRoom1 = CountAll(_room);
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in here) parts.Add($"{kv.Key}={kv.Value}");
        GD.Print($"[MIX] room {idx}: {(parts.Count > 0 ? string.Join(" ", parts) : "empty")}");
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

    private static System.Collections.Generic.List<EnemyController> CollectEnemies(Node from)
    {
        var found = new System.Collections.Generic.List<EnemyController>();
        if (from is EnemyController e) found.Add(e);
        foreach (var c in from.GetChildren()) found.AddRange(CollectEnemies(c));
        return found;
    }

    private static int Count<T>(Node from) where T : Node
    {
        int n = from is T ? 1 : 0;
        foreach (var c in from.GetChildren()) n += Count<T>(c);
        return n;
    }

    private static int CountAll(Node from)
    {
        int n = 1;
        foreach (var c in from.GetChildren()) n += CountAll(c);
        return n;
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
