using Godot;

namespace LostCrownlike.World;

/// <summary>
/// Layered depth behind the play plane.
///
/// An orthographic camera has no perspective divergence, so distant geometry
/// does NOT drift relative to near geometry on its own — the classic 3D
/// parallax you get for free with a perspective camera simply does not happen
/// here. Each layer is therefore shifted manually by a fraction of the
/// camera's own movement, which is 2D parallax done with 3D pieces.
/// </summary>
public partial class ParallaxBackdrop : Node3D
{
    /// Per layer: how far back it sits, how much of the camera's motion it
    /// follows (0 = pinned to the world, 1 = pinned to the camera), how dark,
    /// and how far down it is dropped.
    /// Placed ABOVE the play wall, not behind it: the foreground wall is a
    /// solid 4m slab spanning the whole frame, so anything directly behind it
    /// is simply occluded — the first attempt at this was invisible for that
    /// reason. Stacking the layers upward fills the empty band over the wall
    /// (the round-3 critic's "upper third of the frame is bare") and gives the
    /// depth cue at the same time.
    private static readonly (float Z, float Factor, float Dim, float Y)[] Layers =
    {
        (-7f,  0.35f, 0.45f, 4.0f),
        (-13f, 0.60f, 0.25f, 7.4f),
    };

    [Export] public float SpanX = 90f;

    /// How far the backdrop is tiled BEHIND x = 0. Without it the layers began
    /// at the origin while the orthographic camera, framing a player who
    /// spawns at x = 2, sees back to roughly x = -7 -- so every room opened
    /// with a bare grey band down the left quarter of the screen. Measured
    /// from the capture: the gap filled 275 of 1152 pixels. 24 covers the
    /// camera's half-width (~9.3) with room for the parallax lag, which shifts
    /// the near layer by only 0.35 of the camera's own travel.
    [Export] public float LeadX = 24f;
    [Export] public NodePath CameraPath;

    private Camera3D _camera;
    private readonly System.Collections.Generic.List<(Node3D Root, float Factor, float BaseX)> _layers = new();

    public override void _Ready()
    {
        foreach (var (z, factor, dim, y) in Layers)
        {
            var root = new Node3D { Name = $"Layer_{-z:F0}" };
            AddChild(root);

            int count = Mathf.CeilToInt((SpanX + LeadX) / 4f);
            for (int i = 0; i < count; i++)
            {
                var scene = GD.Load<PackedScene>("res://assets/kaykit/dungeon/wall.gltf");
                if (scene == null) return;
                var piece = scene.Instantiate<Node3D>();
                piece.Position = new Vector3(-LeadX + i * 4f, y, z);
                Tint(piece, dim);
                root.AddChild(piece);
            }
            _layers.Add((root, factor, 0f));
        }

        _camera = GetNodeOrNull<Camera3D>(CameraPath);
    }

    /// Darkens a layer so distance reads as atmosphere rather than as the same
    /// wall drawn twice.
    private static void Tint(Node node, float dim)
    {
        if (node is MeshInstance3D mi)
        {
            mi.MaterialOverlay = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.05f, 0.06f, 0.10f, 1f - dim),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
        }
        foreach (var c in node.GetChildren()) Tint(c, dim);
    }

    public override void _Process(double delta)
    {
        if (_camera == null) return;
        float camX = _camera.GlobalPosition.X;
        foreach (var (root, factor, baseX) in _layers)
            root.Position = new Vector3(baseX + camX * factor, root.Position.Y, root.Position.Z);
    }
}
