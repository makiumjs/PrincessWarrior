using Godot;

namespace LostCrownlike.Core.Testing;

/// TEST-SCENE ONLY. Instances a KayKit character, attaches the shared
/// Rig_Medium animation library, and plays a named animation so the model can
/// be inspected in a capture. Prints the full animation list so we know what
/// the pack actually ships before wiring anything to gameplay.
public partial class KayKitTestDriver : Node3D
{
    [Export] public string CharacterScene = "res://assets/kaykit/characters/Rogue.glb";
    [Export] public string AnimationScene = "res://assets/kaykit/animations/Rig_Medium_MovementBasic.glb";
    [Export] public string SecondAnimationScene = "res://assets/kaykit/animations/Rig_Medium_General.glb";
    [Export] public string PlayAnimation = "Running_A";
    [Export] public string WeaponScene = "res://assets/kaykit/props/sword_1handed.gltf";
    [Export] public string OffhandScene = "res://assets/kaykit/props/shield_round.gltf";

    public override void _Ready()
    {
        var charScene = GD.Load<PackedScene>(CharacterScene);
        var character = charScene.Instantiate<Node3D>();
        AddChild(character);
        character.Name = "Character";

        // The character .glb ships the rigged mesh only (Rig_Medium/Skeleton3D,
        // no AnimationPlayer); the clips live in the separate animation .glb,
        // whose AnimationPlayer sits as a SIBLING of an identically-named
        // Rig_Medium/Skeleton3D. Because both files are authored on the same
        // rig with the same node names, adding an AnimationPlayer at the same
        // relative position on the character makes the imported tracks
        // ("Rig_Medium/Skeleton3D:BoneName") resolve with zero retargeting.
        // Same handslot attachment as PlayerVisual/EnemyVisual, so the
        // close-up inspection scene shows what gameplay actually renders.
        var skel = character.GetNodeOrNull<Skeleton3D>("Rig_Medium/Skeleton3D");
        if (skel != null)
        {
            foreach (var (path, bone) in new[] { (WeaponScene, "handslot.r"), (OffhandScene, "handslot.l") })
            {
                if (string.IsNullOrEmpty(path) || skel.FindBone(bone) < 0) continue;
                var propScene = GD.Load<PackedScene>(path);
                if (propScene == null) continue;
                var att = new BoneAttachment3D { Name = $"Attach_{bone}" };
                skel.AddChild(att);
                att.BoneName = bone;
                att.AddChild(propScene.Instantiate<Node3D>());
            }
        }

        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        character.AddChild(player);

        int merged = 0;
        foreach (var animPath in new[] { AnimationScene, SecondAnimationScene })
        {
            if (string.IsNullOrEmpty(animPath)) continue;
            var animScene = GD.Load<PackedScene>(animPath);
            if (animScene == null) continue;
            var animRoot = animScene.Instantiate<Node3D>();
            var animSource = FindAnimationPlayer(animRoot);
            if (animSource != null)
            {
                foreach (var libName in animSource.GetAnimationLibraryList())
                {
                    var lib = (AnimationLibrary)animSource.GetAnimationLibrary(libName).Duplicate(true);
                    string target = libName == "" ? $"kaykit{merged}" : $"{libName}{merged}";
                    player.AddAnimationLibrary(target, lib);
                    merged++;
                }
            }
            animRoot.QueueFree();
        }

        var list = player.GetAnimationList();
        GD.Print($"[KayKit] {list.Length} animations available:");
        foreach (var a in list) GD.Print($"  - {a}");

        string toPlay = null;
        foreach (var a in list)
            if (a.Contains(PlayAnimation)) { toPlay = a; break; }
        toPlay ??= list.Length > 0 ? list[0] : null;

        if (toPlay != null)
        {
            player.Play(toPlay);
            GD.Print($"[KayKit] playing '{toPlay}'");
        }
        else
        {
            GD.PrintErr("[KayKit] no animations found");
        }
    }

    private static void DumpTree(Node n, int depth)
    {
        if (depth > 3) return;
        GD.Print(new string(' ', depth * 2) + $"{n.Name} [{n.GetType().Name}]");
        foreach (var c in n.GetChildren()) DumpTree(c, depth + 1);
    }

    private static AnimationPlayer FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var child in node.GetChildren())
        {
            var found = FindAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }
}
