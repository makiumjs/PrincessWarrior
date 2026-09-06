using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: what animations a GLB actually offers Godot.
/// A file Blender exported is not automatically a file Godot reads the same
/// way KayKit's own libraries are read.
public partial class ClipProbe : Node
{
    [Export] public string[] Files = System.Array.Empty<string>();

    public override void _Ready()
    {
        foreach (var path in Files)
        {
            var libs = Core.AnimationLibraryCache.Get(path);
            GD.Print($"[CLIP] {path}: {libs.Count} librar(y/ies)");
            foreach (var lib in libs)
                foreach (var name in lib.GetAnimationList())
                {
                    var a = lib.GetAnimation(name);
                    GD.Print($"[CLIP]   {name}  {a.Length:F2}s  tracks={a.GetTrackCount()}");
                }
        }
        GetTree().Quit();
    }
}
