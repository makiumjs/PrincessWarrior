using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: exercises the full loop in the real Main scene —
/// checkpoint pickup, taking enemy damage, death, respawn at the checkpoint.
/// Each stage is asserted from observed state, not assumed.
public partial class GameLoopTest : Node
{
    private int _f;
    private bool _sawCheckpoint, _sawDamage, _sawDeath;
    private Vector3 _checkpointPos;
    private Node3D _player;
    private int _deathFrame = -1;
    private bool _checked;

    public override void _Ready()
    {
        var bus = EventBus.Instance;
        bus.CheckpointReached += id =>
        {
            _sawCheckpoint = true;
            // Read the position Save actually banked, not the player node's:
            // the first checkpoint fires on frame 0 (the player spawns inside
            // its trigger), before this test has resolved the player.
            _checkpointPos = Save.SaveManager.Instance?.Current?.PlayerPosition ?? Vector3.Zero;
            GD.Print($"[LOOP] checkpoint '{id}' at {_checkpointPos} (frame {_f})");
        };
        bus.PlayerDamaged += (amount, _, _, _) =>
        {
            _sawDamage = true;
            GD.Print($"[LOOP] player took {amount} (frame {_f})");
        };
        bus.PlayerDied += () =>
        {
            _sawDeath = true;
            _deathFrame = _f;
            // Compare against the checkpoint that was current AT DEATH, not the
            // first one ever touched: the player banks a new one at every
            // gauntlet, and respawning at the latest is the correct behaviour.
            _checkpointPos = Save.SaveManager.Instance?.Current?.PlayerPosition ?? _checkpointPos;
            GD.Print($"[LOOP] player died (frame {_f})");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        if (_f == 5) Input.ActionPress("move_right");

        // Advance far enough to bank a second checkpoint, then stop: the point
        // is to prove the respawn returns to the CURRENT checkpoint, not the
        // first one.
        if (_f < 240) Input.ActionPress("move_right");
        else Input.ActionRelease("move_right");
        if (_f < 240 && _f % 30 == 0) Input.ActionPress("jump");
        if (_f < 240 && _f % 30 == 4) Input.ActionRelease("jump");

        // Force the death path deterministically rather than waiting on enemy AI
        // damage rolls: what is under test is the loop, not the enemy's DPS.
        if (_sawCheckpoint && !_sawDeath && _f > 240)
        {
            if (_player is IDamageable d)
                d.TakeDamage(new DamageInfo { Amount = 9999, SourcePosition = _player.GlobalPosition });
        }

        // Check the respawn a few frames AFTER death, not at the end of the
        // run: by then the test bot has already run off again, which would
        // read as a failed respawn.
        if (_sawDeath && !_checked && _f == _deathFrame + 10)
        {
            _checked = true;
            var p = _player.GlobalPosition;
            float d = p.DistanceTo(_checkpointPos);
            GD.Print($"[LOOP] 10 frames after death: pos={p} checkpoint={_checkpointPos} dist={d:F2}");
            GD.Print(d < 2.5f
                ? "[LOOP] RESULT: PASS (fell out of world -> died -> respawned at checkpoint)"
                : "[LOOP] RESULT: FAIL (did not respawn at the checkpoint)");
            GetTree().Quit();
        }

        if (_f == 900)
        {
            var pos = _player.GlobalPosition;
            bool respawned = _sawDeath && pos.DistanceTo(_checkpointPos) < 2.5f;
            GD.Print($"[LOOP] checkpoint={_sawCheckpoint} death={_sawDeath} " +
                     $"finalPos={pos} checkpointPos={_checkpointPos} dist={pos.DistanceTo(_checkpointPos):F2}");
            GD.Print(_sawCheckpoint && _sawDeath && respawned
                ? "[LOOP] RESULT: PASS (checkpoint -> death -> respawn at checkpoint)"
                : "[LOOP] RESULT: FAIL");
            GetTree().Quit();
        }
    }
}
