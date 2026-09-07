using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the player owns every ability from the first frame, and
/// nothing in the world hands one out.
///
/// This check used to assert the opposite -- abilities start locked, a crystal
/// grants one, player and save agree. The crystals are gone: without
/// backtracking a gate is not a locked door you return to, it is a chunk
/// silently downgraded to an easier one until the pickup appears, and what it
/// bought in practice was a class of bug where a room's crossability depended
/// on which layout happened to be dealt before it.
///
/// So the question is inverted, and it is the one this project keeps asking in
/// the other direction: is everything DECLARED also real? Here, is everything
/// removed actually gone? A pickup still spawning somewhere in room seven would
/// be invisible -- it would grant what the player already has and read as
/// scenery -- which is exactly how dead systems survived here twice before.
public partial class GatingTest : Node
{
    private int _f, _room;
    private Node3D _player;
    private DungeonRoomBuilder _rooms;
    private int _pickupsSeen;
    private AbilityFlags _atStart = AbilityFlags.None;

    public override void _Ready() =>
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://save.cfg"));

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _rooms ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_player == null || _rooms == null)
        {
            if (_f > 120) Done(false, "no player, or no room builder");
            return;
        }
        if (_rooms.IsRebuilding) return;

        if (_atStart == AbilityFlags.None)
            _atStart = (AbilityFlags)(int)_player.Get("UnlockedAbilities");

        // Every room, not one: a spawner that fires in a single late layout is
        // precisely what a one-room check would miss.
        _pickupsSeen += CountPickups(_rooms);
        if (_room + 1 < _rooms.RunLength)
        {
            _room++;
            _rooms.RebuildAs(_room);
            return;
        }

        var save = Save.SaveManager.Instance?.Current;
        var saved = save?.UnlockedAbilities ?? AbilityFlags.None;

        GD.Print($"[GATING] player starts with: {_atStart}");
        GD.Print($"[GATING] the save agrees: {saved}");
        GD.Print($"[GATING] ability pickups found across {_rooms.RunLength} rooms: {_pickupsSeen}");

        bool ok = _atStart == AbilityFlags.All
               && saved == AbilityFlags.All
               && _pickupsSeen == 0;
        Done(ok, ok ? "every ability from the first frame, and nothing hands one out" : "");
    }

    /// By class NAME, so this keeps meaning something after AbilityPickup is
    /// deleted: a check that stops compiling when the thing it forbids is
    /// removed cannot then notice the thing coming back.
    private static int CountPickups(Node from)
    {
        int n = from.GetType().Name.Contains("AbilityPickup") ? 1 : 0;
        foreach (var c in from.GetChildren()) n += CountPickups(c);
        return n;
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[GATING] RESULT: PASS ({why})" : "[GATING] RESULT: FAIL");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
