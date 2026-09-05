using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: grants an ability, crosses into the next room, and checks
/// what survives the rebuild — abilities, health, and the save. RebuildAs frees
/// every child of the room, so anything that lived there is gone; this proves
/// the player's own progression does not go with it.
public partial class ProgressionAcrossRoomsTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private AbilityFlags _before;
    private int _hpBefore;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 10)
        {
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash);
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.DoubleJump);
        }

        if (_f == 30)
        {
            _before = (AbilityFlags)(int)_player.Get("UnlockedAbilities");
            _hpBefore = (int)_player.Get("CurrentHealth");
            var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
            if (exit == null) { GD.Print("[PROG] RESULT: FAIL (no exit in room 0)"); GetTree().Quit(); return; }
            GD.Print($"[PROG] before transition: abilities={_before} hp={_hpBefore} roomIndex={_room.RoomIndex}");
            _player.GlobalPosition = exit.GlobalPosition;
        }

        if (_f == 90)
        {
            var after = (AbilityFlags)(int)_player.Get("UnlockedAbilities");
            var hp = (int)_player.Get("CurrentHealth");
            var saved = Save.SaveManager.Instance?.Current?.UnlockedAbilities ?? AbilityFlags.None;
            GD.Print($"[PROG] after transition: abilities={after} hp={hp} roomIndex={_room.RoomIndex} saved={saved}");

            bool ok = _room.RoomIndex == 1
                   && after == _before
                   && hp == _hpBefore
                   && saved.HasFlag(AbilityFlags.Dash);
            GD.Print(ok
                ? "[PROG] RESULT: PASS (abilities, health and save survive the room rebuild)"
                : "[PROG] RESULT: FAIL");
            GetTree().Quit();
        }
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
