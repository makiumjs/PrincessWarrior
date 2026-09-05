using Godot;
namespace LostCrownlike.PlayerCamera;
public partial class BoneDumpProbe : Node
{
    private int _f;
    public override void _Process(double delta)
    {
        if (++_f != 30) return;
        var skel = Find<Skeleton3D>(GetTree().GetFirstNodeInGroup("player") as Node);
        if (skel == null) { GD.Print("[BONES] none"); GetTree().Quit(); return; }
        for (int i = 0; i < skel.GetBoneCount(); i++) GD.Print($"[BONES] {i}: {skel.GetBoneName(i)}");
        GetTree().Quit();
    }
    private static T Find<T>(Node n) where T : Node
    {
        if (n == null) return null;
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) { var f = Find<T>(c); if (f != null) return f; }
        return null;
    }
}
