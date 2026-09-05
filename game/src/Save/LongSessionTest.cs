using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.Save;

/// TEST-SCENE ONLY: simulates a long play session — many room changes, many
/// deaths, repeated checkpoint writes — then checks the save on disk is still
/// loadable and coherent. Every death and checkpoint rewrites the file; nothing
/// so far has asked whether it survives being written hundreds of times.
public partial class LongSessionTest : Node
{
    private const int Cycles = 20;

    private int _f;
    private int _cycle;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private int _deaths, _writes;

    public override void _Ready()
    {
        EventBus.Instance.PlayerDied += () => _deaths++;
        EventBus.Instance.CheckpointReached += _ => _writes++;
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        // One cycle every 20 frames: change room, then die.
        if (_f > 20 && _f % 20 == 0 && _cycle < Cycles)
        {
            _cycle++;
            _room.RebuildAs(_cycle);
            if (_player is IDamageable d)
                d.TakeDamage(new DamageInfo { Amount = 9999, SourcePosition = _player.GlobalPosition });
        }

        if (_cycle == Cycles && _f % 20 == 10)
        {
            var mem = SaveManager.Instance.Current;
            var disk = SaveManager.Instance.LoadSave();

            GD.Print($"[LONG] {_cycle} room changes, {_deaths} deaths, {_writes} checkpoint writes");
            GD.Print($"[LONG] in memory: abilities={mem.UnlockedAbilities} checkpoint='{mem.LastCheckpointId}' pos={mem.PlayerPosition}");
            GD.Print($"[LONG] from disk:  abilities={disk.UnlockedAbilities} checkpoint='{disk.LastCheckpointId}' pos={disk.PlayerPosition}");

            bool coherent = disk != null
                         && disk.UnlockedAbilities == mem.UnlockedAbilities
                         && disk.LastCheckpointId == mem.LastCheckpointId
                         && disk.PlayerPosition.DistanceTo(mem.PlayerPosition) < 0.01f
                         && !float.IsNaN(disk.PlayerPosition.X);

            // Without this the check is vacuous: an empty save matches an
            // empty memory perfectly, so a run where nothing was ever written
            // would report the strongest possible pass.
            bool didWork = _deaths >= Cycles / 2 && _writes >= Cycles / 2;
            if (!didWork)
                GD.Print($"[LONG] RESULT: FAIL (only {_deaths} deaths and {_writes} writes - the session did not actually happen)");
            else
                GD.Print(coherent
                    ? "[LONG] RESULT: PASS (save still loadable and matches memory after a long session)"
                    : "[LONG] RESULT: FAIL (save drifted or corrupted)");
            GetTree().Quit();
        }
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
