using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the two doors at the end of room 3 lead to different rooms.
///
/// This is the check the branch existed without for a whole round. Both portals
/// called BeginRebuild(4) and the content they produced was byte-identical: the
/// Crucible was a name, a colour and a terrace climb. A check that only asserted
/// "the second exit exists" would have passed the entire time.
///
/// So it asserts CONTENT, twice, from the same room index. Nothing here reads a
/// layout table -- it counts what the built room actually contains, which is the
/// only form of this claim that a future refactor cannot quietly hollow out.
public partial class CrucibleForkTest : Node
{
    private int _f;
    private DungeonRoomBuilder _rooms;

    private int _catacombArenas = -1, _catacombCrucibleEnemies = -1, _catacombRects = -1;
    private int _crucibleArenas = -1, _crucibleCrucibleEnemies = -1, _crucibleRects = -1;

    public override void _Process(double delta)
    {
        _f++;
        _rooms ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_rooms == null) { if (_f > 120) Done(false, "no room builder"); return; }
        if (_rooms.IsRebuilding) return;

        // Pass 1: room 4 as the Catacombs path builds it. The heat manager is
        // untouched, so IsCrucibleRoom is false and the ordinary layout runs.
        if (_f == 20) { _rooms.RebuildAs(DungeonRoomBuilder.CrucibleFirstRoom); return; }

        if (_f == 60)
        {
            _catacombArenas = CountArenaGates();
            _catacombCrucibleEnemies = CountCrucibleEnemies();
            _catacombRects = _rooms.LastComposedRects.Count;
            return;
        }

        // Pass 2: the same index, entered through the other door. Forcing the
        // manager active is what walking into the amber portal does.
        if (_f == 70)
        {
            HeatManager.Instance?.DebugForceActivate(25f);
            _rooms.RebuildAs(DungeonRoomBuilder.CrucibleFirstRoom);
            return;
        }

        if (_f == 110)
        {
            _crucibleArenas = CountArenaGates();
            _crucibleCrucibleEnemies = CountCrucibleEnemies();
            _crucibleRects = _rooms.LastComposedRects.Count;

            GD.Print($"[FORK] catacombs room 4: {_catacombRects} rects, {_catacombArenas} arena gates, {_catacombCrucibleEnemies} crucible enemies");
            GD.Print($"[FORK] crucible room 4: {_crucibleRects} rects, {_crucibleArenas} arena gates, {_crucibleCrucibleEnemies} crucible enemies");

            // Three independent differences, because any one of them alone
            // could be made true by accident. The enemy count is the one that
            // matters most: a layout can be retuned into agreement, but a room
            // holding Slagbounds is not the room holding grunts.
            bool differentShape = _crucibleRects != _catacombRects;
            bool moreArenas = _crucibleArenas > _catacombArenas && _crucibleArenas >= 4;
            bool ownRoster = _crucibleCrucibleEnemies > 0 && _catacombCrucibleEnemies == 0;

            bool ok = differentShape && moreArenas && ownRoster;
            Done(ok, ok ? "the two room-3 doors build different rooms: layout, arenas and roster all differ" : "");
        }
    }

    private int CountArenaGates() => CountOf<ArenaGate>(_rooms);

    /// Anything only the Crucible spawns. Counted by type rather than by name:
    /// a renamed scene must not be able to make this check pass.
    private int CountCrucibleEnemies()
    {
        int n = 0;
        foreach (var c in _rooms.GetChildren())
            if (c is AI.Slagbound or AI.Emberwright or AI.Emberhusk or AI.Forgemaster) n++;
        return n;
    }

    private static int CountOf<T>(Node from) where T : Node
    {
        int n = from is T ? 1 : 0;
        foreach (var c in from.GetChildren()) n += CountOf<T>(c);
        return n;
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[FORK] RESULT: PASS ({why})" : "[FORK] RESULT: FAIL");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
