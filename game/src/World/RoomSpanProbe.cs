using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: how long a room actually is, in metres and in platforms.
/// A measurement, not an assertion -- it exists so "the rooms are too short"
/// is a number before it is a change.
public partial class RoomSpanProbe : Node
{
    private int _f;
    private DungeonRoomBuilder _room;
    private int _next;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_room == null || _room.IsRebuilding) return;

        if (_f % 20 != 0) return;

        if (_next > 0) Report(_next - 1);

        if (_next >= _room.RunLength) { GetTree().Quit(); return; }
        _room.RebuildAs(_next);
        _next++;
    }

    private void Report(int index)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        int platforms = 0, enemies = 0;
        foreach (var c in _room.GetChildren())
        {
            if (c is StaticBody3D sb)
            {
                platforms++;
                minX = Mathf.Min(minX, sb.Position.X);
                maxX = Mathf.Max(maxX, sb.Position.X);
                minY = Mathf.Min(minY, sb.Position.Y);
                maxY = Mathf.Max(maxY, sb.Position.Y);
            }
            if (c is AI.EnemyController) enemies++;
        }
        var pickups = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var c in _room.GetChildren())
            if (c is AbilityPickup ap)
            {
                string k = ap.Ability.ToString();
                pickups[k] = pickups.TryGetValue(k, out int n) ? n + 1 : 1;
            }
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in pickups) parts.Add($"{kv.Key}x{kv.Value}");
        GD.Print($"[SPAN]   pickups: {(parts.Count > 0 ? string.Join(" ", parts) : "none")}");

        var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
        GD.Print($"[SPAN] room {index}: platforms={platforms} span={maxX - minX:F1}m " +
                 $"rise={maxY - minY:F1}m exitX={exit?.GlobalPosition.X ?? -1:F1} enemies={enemies}");
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
