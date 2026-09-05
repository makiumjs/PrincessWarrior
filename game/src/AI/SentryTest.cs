using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the ranged enemy exists in the game, hurts from a
/// distance, and does not leave its bolts behind.
///
/// Two separate claims, because they fail separately. A sentry that works
/// perfectly but is never placed in a room is content that does not exist —
/// this project has had that exact defect. And a projectile that damages
/// correctly but never frees itself is a leak that only shows up after a long
/// session, which is precisely when nobody is watching.
public partial class SentryTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private CrossbowSentry _sentry;
    private int _sentriesInRoom = -1;
    private int _hpBefore;
    private int _hpAfterShot = -1;
    private int _boltsAtPeak;
    private Projectile _strayBolt;
    private bool _strayAliveMidflight;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        // Room 1 onwards mixes the two types; room 0 is deliberately all melee.
        if (_f == 20) _room.RebuildAs(1);

        if (_f == 60)
        {
            _sentriesInRoom = CountOfType<CrossbowSentry>(_room);
            GD.Print($"[SENTRY] sentries placed in room 1: {_sentriesInRoom}");

            // Own sentry at a known offset. Using one from the layout would
            // make this a test of where the generator happens to put things.
            _sentry = GD.Load<PackedScene>("res://scenes/enemies/CrossbowSentry.tscn")
                        .Instantiate<CrossbowSentry>();
            _room.AddChild(_sentry);
            _sentry.GlobalPosition = _player.GlobalPosition + new Vector3(6f, 0f, 0f);
            _hpBefore = (int)_player.Get("CurrentHealth");
            GD.Print($"[SENTRY] player hp before: {_hpBefore}");
        }

        // The sentry needs to see, wind up (0.55s) and land a bolt travelling
        // 6 units at 11 u/s. Sampled every frame so the peak bolt count is the
        // real one, not whatever happens to exist at one chosen frame.
        if (_f > 60 && _f < 220)
            _boltsAtPeak = Mathf.Max(_boltsAtPeak, CountOfType<Projectile>(_room));

        if (_f == 220)
        {
            _hpAfterShot = (int)_player.Get("CurrentHealth");
            GD.Print($"[SENTRY] player hp after: {_hpAfterShot}, peak bolts in flight: {_boltsAtPeak}");
            if (IsInstanceValid(_sentry)) _sentry.QueueFree();
        }

        // A bolt that HITS despawns on impact, so the hit above says nothing
        // about the lifetime path -- removing the timeout entirely left this
        // test green until this phase existed. The bolt that flies forever is
        // the one that MISSES, so one is fired away from everything.
        if (_f == 240)
        {
            _strayBolt = GD.Load<PackedScene>("res://scenes/enemies/Bolt.tscn")
                           .Instantiate<Projectile>();
            _strayBolt.DirectionX = -1f;
            _room.AddChild(_strayBolt);
            _strayBolt.GlobalPosition = _player.GlobalPosition + new Vector3(-4f, 6f, 0f);
            GD.Print("[SENTRY] stray bolt fired away from the player, above the floor");
        }

        // Still airborne well before the 2.5s timeout: proves the check below
        // is watching a bolt that existed, not one that never spawned.
        if (_f == 280)
        {
            _strayAliveMidflight = IsInstanceValid(_strayBolt);
            GD.Print($"[SENTRY] stray bolt alive mid-flight: {_strayAliveMidflight}");
        }

        // Bolt lifetime is 2.5s = 150 frames at the fixed rate, and the stray is
        // fired at 240, so it is due at 390. Checked at 440 with margin: the
        // first attempt used 380 and failed the working code by ten frames.
        if (_f == 440)
        {
            int leftover = CountOfType<Projectile>(_room);
            GD.Print($"[SENTRY] bolts still alive after the sentry is gone: {leftover}");

            bool ok = _sentriesInRoom > 0
                   && _boltsAtPeak > 0
                   && _hpAfterShot < _hpBefore
                   && _strayAliveMidflight
                   && leftover == 0;

            GD.Print(ok
                ? "[SENTRY] RESULT: PASS (ranged enemies are placed, hurt at range, and clean up)"
                : "[SENTRY] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private static int CountOfType<T>(Node from) where T : Node
    {
        int n = from is T ? 1 : 0;
        foreach (var c in from.GetChildren()) n += CountOfType<T>(c);
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
