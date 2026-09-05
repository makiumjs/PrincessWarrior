using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: cycles through many room rebuilds and watches the total
/// node count. RebuildAs frees and recreates dozens of nodes each time; if any
/// of them are not actually released, an hour-long session accumulates them.
/// A leak of this kind never shows up in a short run.
public partial class RoomChurnTest : Node
{
    private const int Transitions = 12;

    private int _f;
    private int _done;
    private DungeonRoomBuilder _room;
    private int _baseline;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindRoom(GetTree().Root);
        if (_room == null) return;

        // Settle first: the very first frames still have scene setup in flight.
        if (_f == 30) { _baseline = CountNodes(GetTree().Root); GD.Print($"[CHURN] baseline nodes: {_baseline}"); }

        if (_f > 30 && _f % 15 == 0 && _done < Transitions)
        {
            _done++;
            _room.RebuildAs(_done);
        }

        if (_done == Transitions && _f % 15 == 5)
        {
            int now = CountNodes(GetTree().Root);
            int drift = now - _baseline;
            GD.Print($"[CHURN] after {Transitions} rebuilds: {now} nodes (baseline {_baseline}, drift {drift:+#;-#;0})");
            // Room layouts differ in size, so an exact match is not expected;
            // what matters is that the count is not climbing with each cycle.
            // A rebuild that silently did nothing also produces zero drift, so
            // check the room really changed before trusting the stability.
            bool rebuilt = _room.RoomIndex == Transitions;
            if (!rebuilt)
                GD.Print($"[CHURN] RESULT: FAIL (room index is {_room.RoomIndex}, expected {Transitions} - rebuilds did not happen)");
            else
                GD.Print(Mathf.Abs(drift) < _baseline / 2
                    ? "[CHURN] RESULT: PASS (node count stable across repeated rebuilds)"
                    : "[CHURN] RESULT: FAIL (nodes accumulating - rebuild leaks)");
            GetTree().Quit();
        }
    }

    private static int CountNodes(Node n)
    {
        int c = 1;
        foreach (var ch in n.GetChildren()) c += CountNodes(ch);
        return c;
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
