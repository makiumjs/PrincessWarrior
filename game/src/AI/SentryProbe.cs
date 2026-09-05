using Godot;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// CAPTURE-ONLY: builds room 1 and parks the player in front of a sentry so
/// the enemy, its bolt, and the two types side by side can be photographed.
public partial class SentryProbe : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 20) _room.RebuildAs(1);

        // One of each, lined up, to check they can be told apart at the
        // camera's distance -- which is the only distance that matters.
        if (_f == 60)
        {
            Place("BasicMelee", 3.2f);
            Place("CrossbowSentry", 6.0f);
            Place("Skirmisher", 8.8f);
        }
    }

    private void Place(string type, float offsetX)
    {
        var e = GD.Load<PackedScene>($"res://scenes/enemies/{type}.tscn")
                  .Instantiate<EnemyController>();
        _room.AddChild(e);
        e.GlobalPosition = _player.GlobalPosition + new Vector3(offsetX, 0f, 0f);
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
