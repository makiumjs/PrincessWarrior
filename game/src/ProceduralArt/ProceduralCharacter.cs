using Godot;

namespace LostCrownlike.ProceduralArt;

/// <summary>
/// Builds a stylized humanoid silhouette entirely at load time: capsule
/// segments (ArrayMesh via SurfaceTool, see ProceduralMeshFactory) parented
/// into a pivot hierarchy (Hips -> Torso/UpperLegs -> Head/UpperArms/LowerLegs
/// -> LowerArms), a runtime-built AnimationPlayer holding procedurally
/// generated "idle"/"run" Animation resources (ProceduralAnimationBuilder),
/// and an AnimationTree (Blend2 of idle/run) driving the pose blend — the
/// "code-driven procedural posing" called for in ARCHITECTURE.md instead of
/// hand-authored keyframe clips.
///
/// Every pivot is a plain Node3D (not a Skeleton3D/BoneAttachment3D rig) so
/// the Animation tracks can target ":rotation"/":position" directly by
/// NodePath — simplest thing that reads as a posed character.
/// </summary>
public partial class ProceduralCharacter : Node3D
{
    [Export] public float RunBlend { get; set; } = 1.0f; // 0 = idle, 1 = run

    private const float HipHeight = 0.95f;

    public Node3D Hips { get; private set; }
    public AnimationPlayer Player { get; private set; }
    public AnimationTree Tree { get; private set; }

    public override void _Ready()
    {
        var material = GD.Load<Shader>("res://src/ProceduralArt/shaders/character.gdshader");
        var shaderMat = new ShaderMaterial { Shader = material };

        BuildRig(shaderMat);
        BuildAnimation();
    }

    private static MeshInstance3D MakeSegment(string name, ArrayMesh mesh, Material mat, Vector3 localOffset)
    {
        var mi = new MeshInstance3D { Name = name, Mesh = mesh, Position = localOffset };
        mi.SetSurfaceOverrideMaterial(0, mat);
        return mi;
    }

    private void BuildRig(Material mat)
    {
        Hips = new Node3D { Name = "Hips", Position = new Vector3(0f, HipHeight, 0f) };
        AddChild(Hips);
        Hips.AddChild(MakeSegment("HipsMesh", ProceduralMeshFactory.CreateCapsule(0.14f, 0.06f, 10, 3), mat, Vector3.Zero));

        var torso = new Node3D { Name = "Torso", Position = new Vector3(0f, 0.10f, 0f) };
        Hips.AddChild(torso);
        torso.AddChild(MakeSegment("TorsoMesh", ProceduralMeshFactory.CreateCapsule(0.19f, 0.42f, 10, 4), mat, new Vector3(0f, 0.24f, 0f)));

        var head = new Node3D { Name = "Head", Position = new Vector3(0f, 0.52f, 0f) };
        torso.AddChild(head);
        head.AddChild(MakeSegment("HeadMesh", ProceduralMeshFactory.CreateCapsule(0.13f, 0.05f, 10, 5), mat, new Vector3(0f, 0.13f, 0f)));

        BuildArm(torso, "L", +0.24f, mat);
        BuildArm(torso, "R", -0.24f, mat);
        BuildLeg(Hips, "L", +0.11f, mat);
        BuildLeg(Hips, "R", -0.11f, mat);
    }

    private static void BuildArm(Node3D torso, string side, float xOffset, Material mat)
    {
        const float upperLen = 0.28f, lowerLen = 0.26f;
        var upper = new Node3D { Name = $"UpperArm{side}", Position = new Vector3(xOffset, 0.42f, 0f) };
        torso.AddChild(upper);
        upper.AddChild(MakeSegment($"UpperArm{side}Mesh", ProceduralMeshFactory.CreateCapsule(0.055f, upperLen, 8, 3), mat, new Vector3(0f, -upperLen * 0.5f, 0f)));

        var lower = new Node3D { Name = $"LowerArm{side}", Position = new Vector3(0f, -upperLen, 0f) };
        upper.AddChild(lower);
        lower.AddChild(MakeSegment($"LowerArm{side}Mesh", ProceduralMeshFactory.CreateCapsule(0.048f, lowerLen, 8, 3), mat, new Vector3(0f, -lowerLen * 0.5f, 0f)));
        lower.AddChild(MakeSegment($"Hand{side}Mesh", ProceduralMeshFactory.CreateCapsule(0.055f, 0.02f, 6, 3), mat, new Vector3(0f, -lowerLen - 0.02f, 0f)));
    }

    private static void BuildLeg(Node3D hips, string side, float xOffset, Material mat)
    {
        const float upperLen = 0.40f, lowerLen = 0.38f;
        var upper = new Node3D { Name = $"UpperLeg{side}", Position = new Vector3(xOffset, -0.02f, 0f) };
        hips.AddChild(upper);
        upper.AddChild(MakeSegment($"UpperLeg{side}Mesh", ProceduralMeshFactory.CreateCapsule(0.075f, upperLen, 8, 3), mat, new Vector3(0f, -upperLen * 0.5f, 0f)));

        var lower = new Node3D { Name = $"LowerLeg{side}", Position = new Vector3(0f, -upperLen, 0f) };
        upper.AddChild(lower);
        lower.AddChild(MakeSegment($"LowerLeg{side}Mesh", ProceduralMeshFactory.CreateCapsule(0.062f, lowerLen, 8, 3), mat, new Vector3(0f, -lowerLen * 0.5f, 0f)));

        // Small beveled-block "foot" so limbs terminate in something readable.
        var footMesh = ProceduralMeshFactory.CreateRoundedBlock(new Vector3(0.11f, 0.06f, 0.22f), bevel: 0.35f, subdivisions: 3);
        lower.AddChild(MakeSegment($"Foot{side}Mesh", footMesh, mat, new Vector3(0f, -lowerLen - 0.02f, 0.05f)));
    }

    private void BuildAnimation()
    {
        Vector3 hipsRest = Hips.Position;

        var lib = new AnimationLibrary();
        lib.AddAnimation("idle", ProceduralAnimationBuilder.BuildIdleAnimation(hipsRest));
        lib.AddAnimation("run", ProceduralAnimationBuilder.BuildRunAnimation(hipsRest));

        Player = new AnimationPlayer { Name = "AnimationPlayer" };
        AddChild(Player);
        Player.AddAnimationLibrary("", lib);

        var blendTree = new AnimationNodeBlendTree();
        var idleNode = new AnimationNodeAnimation { Animation = "idle" };
        var runNode = new AnimationNodeAnimation { Animation = "run" };
        var blend2 = new AnimationNodeBlend2();

        blendTree.AddNode("idle", idleNode, new Vector2(0, 0));
        blendTree.AddNode("run", runNode, new Vector2(0, 150));
        blendTree.AddNode("blend", blend2, new Vector2(250, 75));
        blendTree.ConnectNode("blend", 0, "idle");
        blendTree.ConnectNode("blend", 1, "run");
        blendTree.ConnectNode("output", 0, "blend");

        Tree = new AnimationTree { Name = "AnimationTree", TreeRoot = blendTree };
        AddChild(Tree);
        Tree.AnimPlayer = Tree.GetPathTo(Player);
        Tree.Active = true;
        Tree.Set("parameters/blend/blend_amount", RunBlend);
    }

    public override void _Process(double delta)
    {
        if (Tree != null && Tree.Active)
        {
            Tree.Set("parameters/blend/blend_amount", RunBlend);
        }
    }
}
