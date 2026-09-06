using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the metroidvania invariant, stated as something that can
/// actually be proven — abilities are OFF at the start, a pickup grants exactly
/// one, the player's own flags reflect it, and Save persists it.
///
/// Deliberately does NOT try to show "the ability opened progress": measuring
/// that with a fixed-cadence bot compares bot quality, not gating. A bot that
/// dashes on a timer drives itself into pits and travels LESS far with the
/// ability than without it.
public partial class GatingTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private AbilityFlags _granted;
    private bool _sawGrant;

    public override void _Ready()
    {
        EventBus.Instance.AbilityUnlocked += bits =>
        {
            _granted = (AbilityFlags)bits;
            _sawGrant = true;
            GD.Print($"[GATE] pickup granted {_granted} at frame {_f}");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null || _room.IsRebuilding) return;

        // Its own clean slate. "Starts locked" is a claim about a fresh run,
        // and SaveManager restores whatever the previous check in the suite
        // left on disk -- so this depended on being the fourth check rather
        // than on the game being right.
        if (_f == 3)
        {
            Save.SaveManager.Instance?.ResetSave();
            if (_player is PlayerCamera.PlayerController p) p.ResetForNewRun();
            return;
        }

        if (_f == 5)
        {
            var start = (AbilityFlags)(int)_player.Get("UnlockedAbilities");
            GD.Print($"[GATE] starting abilities = {start}");
            if (start != AbilityFlags.None)
            {
                GD.Print("[GATE] RESULT: FAIL (player did not start with abilities locked)");
                GetTree().Quit();
                return;
            }
            // Room 0 has no crystal in it any more -- it is the room that
            // teaches jumping -- and this used to walk right in whichever room
            // happened to be built. Asked, not remembered.
            _room.RebuildAs(_room.FirstRoomWithAbilityPickup());
            return;
        }

        // Put the player just short of the first crystal and walk into it.
        // Walking the whole room to reach one is a traversal test, and there
        // is one of those; what this owns is what happens when the crystal is
        // touched.
        if (_f == 30)
        {
            var pickup = FirstPickup(_room);
            if (pickup == null)
            {
                GD.Print("[GATE] RESULT: FAIL (no ability pickup anywhere in the first room that should have one)");
                GetTree().Quit();
                return;
            }
            _player.GlobalPosition = pickup.GlobalPosition + new Vector3(-3.5f, 0.4f, 0f);
            GD.Print($"[GATE] walking into a {pickup.Ability} crystal at x={pickup.GlobalPosition.X:F1}");
            Input.ActionPress("move_right");
            return;
        }

        if (_f == 300)
        {
            var now = (AbilityFlags)(int)_player.Get("UnlockedAbilities");
            var saved = Save.SaveManager.Instance?.Current?.UnlockedAbilities ?? AbilityFlags.None;
            GD.Print($"[GATE] granted={_granted} playerFlags={now} savedFlags={saved}");

            bool ok = _sawGrant
                   && now.HasFlag(_granted)
                   && saved.HasFlag(_granted);
            GD.Print(ok
                ? "[GATE] RESULT: PASS (locked at start, pickup grants, player and save agree)"
                : "[GATE] RESULT: FAIL");
            Input.ActionRelease("move_right");
            GetTree().Quit();
        }
    }

    private static AbilityPickup FirstPickup(Node from)
    {
        if (from is AbilityPickup p) return p;
        foreach (var c in from.GetChildren()) { var f = FirstPickup(c); if (f != null) return f; }
        return null;
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
