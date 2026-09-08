using Godot;

namespace LostCrownlike.Combat;

/// <summary>
/// The arc a blade leaves behind it during a swing.
///
/// POOLED, not spawned. One of these is built once and lives on the weapon
/// pivot for the whole run; a swing shows it, animates it and hides it again.
/// That is the difference between this and ImpactBurst, and the reason is the
/// rate: a burst happens when a blow lands, a trail happens on every swing
/// including the ones that hit nothing, and a player mashing light attacks
/// swings about four times a second for twelve minutes. Spawning a node with a
/// mesh and a material for each of those is roughly three thousand allocations
/// a run, which is the frame-time p99 nobody can explain later.
///
/// The mesh is built ONCE, at _Ready, as a fixed arc of unit radius. A swing
/// changes its scale, its colour and its alpha -- never its geometry. Rebuilding
/// the ribbon per frame to follow the blade would allocate a vertex buffer
/// sixty times a second, which is the thing this whole pass exists to avoid.
///
/// It is parented to the pivot the weapon already swings on, so it inherits the
/// blade's motion for free rather than trying to track a bone position that
/// only the animation player really knows.
/// </summary>
public partial class SlashTrail : MeshInstance3D
{
    public enum Weight
    {
        /// Sharp and cold. A light attack is a cut, not a blow.
        Light,
        /// Wider and heavier, silver-amber: the swing you feel the weight of.
        Heavy,
        /// Dense and burning, and noticeably bigger. The charged heavy is the
        /// most damaging thing the player owns and it should look like it.
        Charged,
    }

    private static readonly Color LightColor = new(0.376f, 0.878f, 1f);   // #60E0FF
    private static readonly Color HeavyColor = new(1f, 0.816f, 0.376f);   // #FFD060
    private static readonly Color ChargedColor = new(1f, 0.376f, 0.125f); // #FF6020

    private const float LightLife = 0.10f;
    private const float HeavyLife = 0.14f;
    private const float ChargedLife = 0.18f;

    /// How far along the blade the ribbon starts and ends, in metres from the
    /// pivot. The inner edge sits past the grip so the arc reads as coming off
    /// the EDGE rather than out of the character's fist.
    /// Measured off sword_1handed.gltf rather than eyeballed, the same way the
    /// arrow's orientation was read out of its vertex buffer: the mesh spans
    /// -0.366 to +1.409 along its own Y, so the tip sits 1.41m from the pivot
    /// and the guard is a little above the origin. A trail sized for the
    /// placeholder dagger (tip at 0.98m) came up a quarter short of the blade
    /// and read as a smear near the hand.
    [Export] public float InnerRadius { get; set; } = 0.45f;
    [Export] public float OuterRadius { get; set; } = 1.41f;

    /// How much of a circle the ribbon covers, measured BACKWARD from the
    /// blade. Most of PlayerVisual's 140-degree swing, so the tail reaches
    /// nearly to where the swing started.
    [Export] public float ArcDegrees { get; set; } = 120f;

    [Export] public int Segments { get; set; } = 14;

    private StandardMaterial3D _mat;
    private float _t;
    private float _life;
    private float _energy;
    private float _spread = 1f;
    private bool _running;

    public override void _Ready()
    {
        _mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0f),
            EmissionEnabled = true,
            Emission = LightColor,
            EmissionEnergyMultiplier = 0f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Without this the vertex colours are decoration the renderer never
            // reads, and the taper baked into the mesh does nothing at all --
            // the ribbon comes out a flat wedge welded to the blade.
            VertexColorUseAsAlbedo = true,
            // Both faces: the player turns around, and a one-sided ribbon
            // disappears every time they swing while facing left.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        MaterialOverride = _mat;
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        Mesh = BuildArc();
        // Hidden and idle: _Process returns on its first line until a swing
        // fires, so a trail that is never used costs nothing but the node.
        Visible = false;
    }

    /// <summary>
    /// The ribbon, as a triangle strip laid out by hand.
    ///
    /// Swept in the pivot's own Y/Z plane, because that is the plane the weapon
    /// pivot rotates in -- PlayerVisual turns it about local X. An arc built in
    /// XY would be correct-looking in the editor and edge-on in the game, which
    /// is the same trap that made an early ring effect invisible.
    ///
    /// The strip narrows toward its trailing end so the arc tapers the way a
    /// blade's afterimage does rather than reading as a solid wedge.
    /// </summary>
    private ArrayMesh BuildArc()
    {
        int verts = (Segments + 1) * 2;
        var positions = new Vector3[verts];
        var colors = new Color[verts];
        var indices = new int[Segments * 6];

        float arc = Mathf.DegToRad(ArcDegrees);

        for (int i = 0; i <= Segments; i++)
        {
            float k = i / (float)Segments;

            // Swept from -arc to 0, not centred on 0. The ribbon is an
            // AFTERIMAGE: it has to sit behind the blade along the direction
            // the swing came from. Centred, half of it would lead the edge --
            // a fan bolted to the sword rather than a trail left by it.
            //
            // k = 1 is therefore the blade's own angle, which is where the
            // taper is widest and the vertex alpha is 1.
            float a = -arc + arc * k;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);

            // Leading edge is full width, trailing edge tapers to a point.
            float taper = Mathf.Lerp(0.25f, 1f, k);
            float inner = Mathf.Lerp(OuterRadius, InnerRadius, taper);

            positions[i * 2] = new Vector3(0f, c * inner, s * inner);
            positions[i * 2 + 1] = new Vector3(0f, c * OuterRadius, s * OuterRadius);

            // Vertex alpha does the fade ALONG the arc, so the tail thins out
            // without a second material or a shader. The uniform alpha applied
            // per frame then fades the whole thing over time; the two multiply.
            float along = k * k;
            colors[i * 2] = new Color(1f, 1f, 1f, along * 0.55f);
            colors[i * 2 + 1] = new Color(1f, 1f, 1f, along);
        }

        for (int i = 0; i < Segments; i++)
        {
            int v = i * 2, t = i * 6;
            indices[t] = v;
            indices[t + 1] = v + 1;
            indices[t + 2] = v + 2;
            indices[t + 3] = v + 1;
            indices[t + 4] = v + 3;
            indices[t + 5] = v + 2;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Godot.Mesh.ArrayType.Color] = colors;
        arrays[(int)Godot.Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// Starts a trail. Called on the frame a swing BEGINS -- re-firing while one
    /// is already running simply restarts it, which is what a combo does.
    /// </summary>
    public void Fire(Weight weight)
    {
        if (_mat == null) return;

        Color tint;
        switch (weight)
        {
            case Weight.Charged:
                tint = ChargedColor; _life = ChargedLife; _energy = 4.5f; _spread = 1.35f; break;
            case Weight.Heavy:
                tint = HeavyColor; _life = HeavyLife; _energy = 3.0f; _spread = 1.15f; break;
            default:
                tint = LightColor; _life = LightLife; _energy = 2.2f; _spread = 1f; break;
        }

        _mat.Emission = tint;
        _mat.AlbedoColor = new Color(tint.R, tint.G, tint.B, 1f);
        _mat.EmissionEnergyMultiplier = _energy;

        Scale = new Vector3(1f, _spread, _spread);
        _t = 0f;
        _running = true;
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!_running) return;

        _t += (float)delta;
        float k = _t / _life;

        if (k >= 1f)
        {
            _running = false;
            Visible = false;
            return;
        }

        // Fades fast and opens slightly as it goes: an afterimage that held its
        // size would read as a painted decal following the hand.
        float fade = 1f - k;
        var c = _mat.AlbedoColor;
        c.A = fade * fade;
        _mat.AlbedoColor = c;
        _mat.EmissionEnergyMultiplier = _energy * fade;

        float grow = 1f + 0.12f * k;
        Scale = new Vector3(1f, _spread * grow, _spread * grow);
    }
}
