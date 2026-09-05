using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: plays a whole run end to end and checks that it ENDS.
///
/// Before this existed the three room layouts cycled on `index % 3` forever,
/// so the game had no last room and no win state — a sandbox, not a game. The
/// check is deliberately end-to-end rather than a unit test of the comparison
/// in RoomExitTrigger: the interesting failures are that the run never
/// terminates, that it terminates more than once, or that the last room still
/// rebuilds underneath the ending.
///
/// The player is teleported onto each RoomExit rather than driven with input.
/// Walking six full rooms would make the test a platforming benchmark whose
/// failures would be about jump tuning, not about the arc.
public partial class RunArcTest : Node
{
    private const int StepFrames = 60;

    private int _f;
    private int _completions;
    private int _completedWith = -1;
    private int _lastRoomIndexSeen;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private bool _subscribed;

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


        if (!_subscribed)
        {
            _subscribed = true;
            EventBus.Instance.RunCompleted += OnRunCompleted;
        }

        _lastRoomIndexSeen = Mathf.Max(_lastRoomIndexSeen, _room.RoomIndex);

        // One nudge onto the exit every StepFrames, for two more steps than
        // the run is long. The surplus is the point: if the ending does not
        // stop the cycle, the extra steps keep it going and RoomIndex climbs
        // past RunLength, which the assertions below catch.
        int steps = _room.RunLength + 2;
        if (_f % StepFrames == 0 && _f / StepFrames <= steps && _completions == 0)
        {
            var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
            if (exit == null)
            {
                GD.Print($"[ARC] RESULT: FAIL (no exit in room {_room.RoomIndex})");
                GetTree().Quit();
                return;
            }
            // The last room's exit is sealed behind the boss now, so a run
            // that only walks to the door does not end. The arc is what this
            // check owns; the fight's rules belong to the boss check, so the
            // boss is simply killed rather than fought.
            ClearBoss();
            _player.GlobalPosition = exit.GlobalPosition;
        }

        // Keep running after the completion so a second one, or a late
        // rebuild, still has time to show up.
        if (_f == StepFrames * (steps + 2))
        {
            var save = Save.SaveManager.Instance?.Current;
            int savedRooms = save?.RoomsCleared ?? -1;
            bool savedDone = save?.RunCompleted ?? false;

            GD.Print($"[ARC] completions={_completions} completedWith={_completedWith} " +
                     $"runLength={_room.RunLength} lastRoomIndex={_lastRoomIndexSeen} " +
                     $"saved: rooms={savedRooms} completed={savedDone}");

            bool ok = _completions == 1
                   && _completedWith == _room.RunLength
                   && _lastRoomIndexSeen == _room.RunLength - 1
                   && savedDone
                   && savedRooms == _room.RunLength;

            GD.Print(ok
                ? $"[ARC] RESULT: PASS (the run ends once after {_room.RunLength} rooms and is recorded)"
                : "[ARC] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    /// Kills any live boss outright. Deliberately blunt: reproducing the parry
    /// loop here would make an ending check fail whenever combat tuning moved.
    private void ClearBoss()
    {
        foreach (var n in GetTree().GetNodesInGroup("boss"))
        {
            if (n is not Core.IDamageable d || n is not Node3D at) continue;

            // Open it first. The boss absorbs everything that lands on armour
            // down to chip damage, so a blunt 100000 here removed 2 hp and the
            // door stayed shut -- which is the armour working, not a bug, and
            // it took a trace to see because the number looked decisive.
            EventBus.Instance?.EmitParried(true, at.GlobalPosition);

            d.TakeDamage(new Core.DamageInfo
            {
                Amount = 100000,
                SourcePosition = at.GlobalPosition,
                Knockback = Vector3.Zero,
                IsCritical = false,
            });
        }
    }

    private void OnRunCompleted(int roomsCleared)
    {
        _completions++;
        _completedWith = roomsCleared;
        GD.Print($"[ARC] run completed at frame {_f} with {roomsCleared} rooms");
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
