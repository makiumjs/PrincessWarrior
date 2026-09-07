using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the barrier holds, the lever opens it, and the lever is
/// what opened it.
///
/// The last clause is the one worth writing. The traversal bot crossed this
/// chunk with four wall jumps and one attack, which is exactly what getting
/// past WITHOUT throwing the lever would look like -- and a mechanic that can
/// be skipped is decoration with a collision box. So this drives the player at
/// the slab first and asserts it does not pass, strikes the lever, and asserts
/// it does.
public partial class LatchTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private LatchGate _gate;
    private LeverSwitch _lever;
    private float _blockedAtX, _afterX, _furthest = float.MinValue;
    private bool _leverThrown;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_player == null || _room == null) return;

        // Enemies off. The claim is about a slab, and the leading gauntlet of a
        // solo-chunk room carries an encounter that killed the player and
        // respawned it at the checkpoint -- which read as the player travelling
        // BACKWARDS after the lever was thrown.
        if (_f == 10) { _room.SpawnEnemies = false; _room.ChunkUnderTest = "Latch"; _room.RebuildAs(0); return; }
        if (_room.IsRebuilding) return;

        if (_f == 40)
        {
            _gate = FindFirst<LatchGate>(_room);
            _lever = FindFirst<LeverSwitch>(_room);
            if (_gate == null || _lever == null) { Done(false, "no latch built"); return; }

            // In front of the slab, on the floor, and moving right. Not
            // teleported past it: the claim is that it HOLDS.
            _player.GlobalPosition = _gate.GlobalPosition + new Vector3(-3f, 1.2f, 0f);
            Input.ActionPress("move_right");
            return;
        }

        // Furthest reached, not where it happens to stand: a knockback or a
        // respawn erases the evidence otherwise.
        if (_f > 40) _furthest = Mathf.Max(_furthest, _player.GlobalPosition.X);

        if (_f == 160)
        {
            _blockedAtX = _furthest;
            // Struck, not teleported onto: the lever answers damage, which is
            // what the player's own hitbox delivers.
            _lever.TakeDamage(new DamageInfo { Amount = 10, SourcePosition = _player.GlobalPosition });
            _leverThrown = _lever.IsThrown;
            return;
        }

        if (_f == 165) _furthest = _player.GlobalPosition.X;

        if (_f == 300)
        {
            _afterX = _furthest;
            float gateX = _gate != null && IsInstanceValid(_gate) ? _gate.GlobalPosition.X : _blockedAtX;

            Input.ActionRelease("move_right");
            GD.Print($"[LATCH] running at the slab stopped at x={_blockedAtX:F1} (slab at x={gateX:F1})");
            GD.Print($"[LATCH] lever thrown by a hit: {_leverThrown}");
            GD.Print($"[LATCH] after it was thrown, the player reached x={_afterX:F1}");

            // Within half a metre of the slab counts as stopped BY it: the
            // player's capsule has width, so "did not get past" is the claim,
            // not "stayed strictly behind the origin".
            bool held = _blockedAtX < gateX + 0.5f;
            bool opened = _afterX > gateX + 1f;
            GD.Print(held && _leverThrown && opened
                ? "[LATCH] RESULT: PASS (the slab holds, a struck lever opens it, and only then does the player pass)"
                : "[LATCH] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[LATCH] RESULT: PASS ({why})" : $"[LATCH] RESULT: FAIL ({why})");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
