using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the real bounding box of every kit piece the builder uses.
/// Written because a foundation course was placed on assumed dimensions and
/// came out protruding above the floor with gaps between the blocks.
public partial class PropSizeProbe : Node
{
    private static readonly string[] Pieces =
    {
        "floor_tile_large", "floor_foundation_allsides", "floor_foundation_front_and_sides",
        "column", "wall", "stairs", "wall_arched", "wall_doorway",
    };

    public override void _Ready()
    {
        foreach (var name in Pieces)
        {
            var scene = GD.Load<PackedScene>($"res://assets/kaykit/dungeon/{name}.gltf");
            if (scene == null) { GD.Print($"[PROP] {name}: NOT FOUND"); continue; }
            var node = scene.Instantiate<Node3D>();
            AddChild(node);
            var aabb = Merge(node, new Aabb(), true);
            GD.Print($"[PROP] {name}: size=({aabb.Size.X:F2}, {aabb.Size.Y:F2}, {aabb.Size.Z:F2}) " +
                     $"min=({aabb.Position.X:F2}, {aabb.Position.Y:F2}, {aabb.Position.Z:F2})");
            node.QueueFree();
        }
        GetTree().Quit();
    }

    private static Aabb Merge(Node n, Aabb acc, bool first)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            var box = mi.Mesh.GetAabb();
            box = mi.Transform * box;
            acc = first ? box : acc.Merge(box);
            first = false;
        }
        foreach (var c in n.GetChildren())
        {
            var before = acc;
            acc = Merge(c, acc, first);
            if (!acc.Equals(before)) first = false;
        }
        return acc;
    }
}
