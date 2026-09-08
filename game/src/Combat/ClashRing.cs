using Godot;

namespace LostCrownlike.Combat;

/// <summary>
/// The shockwave a parry throws off: a ring that opens fast and a dozen sparks
/// thrown out of the point where the two blades met.
///
/// Built the same way ImpactBurst is, and for the same reason its file already
/// records -- a listener node placed in a scene is one more thing that can
/// exist, be correct, and be in no scene, which is how the pause menu spent
/// months invisible. Nothing here can be orphaned because there is nothing to
/// place; it is spawned, it runs, it frees itself.
///
/// Kept SEPARATE from ImpactBurst rather than added as another shape to it.
/// Two reasons, and neither is taste: a burst is spawned on every hit in the
/// game and a ring only on a parry, so folding them together would put the
/// ring's twelve extra nodes on a code path that runs constantly; and the
/// impact-fx check counts ImpactBurst nodes by TYPE to prove they are cleaned
/// up, so a parry that suddenly produced two of them would move a number that
/// check was written to trust.
///
/// Allocation: everything is built once, at spawn. The per-frame loop writes
/// only structs -- Vector3, Color -- into objects that already exist, so a long
/// fight does not hand the collector anything.
/// </summary>
public partial class ClashRing : Node3D
{
    private const int Sparks = 12;

    /// Gold for the tight window, cold steel for the ordinary block. The two
    /// have to be told apart at a glance: which one the player just did is the
    /// whole skill the Crucible is built on, and a parry that looks the same
    /// either way teaches nothing.
    public static readonly Color PerfectColor = new(1f, 0.878f, 0.400f);   // #FFE066
    public static readonly Color BlockedColor = new(0.533f, 0.733f, 0.867f); // #88BBDD

    private const float StartRadius = 0.2f;
    private const float EndRadius = 2.4f;
    private const float RingLife = 0.18f;

    /// Sparks outlive the ring by half again. The wave is the announcement and
    /// the sparks are the debris: cutting them at the same instant reads as the
    /// effect being switched off rather than as something having happened.
    private const float SparkLife = 0.27f;

    /// What this ring is. Public for the same reason ImpactBurst.Tint is: the
    /// two colours are a fact about the data, and a check should be able to
    /// tell a perfect clash from a blocked one without a renderer.
    public bool WasPerfect { get; private set; }

    private float _t;
    private MeshInstance3D _ring;
    private StandardMaterial3D _ringMat;
    private StandardMaterial3D _sparkMat;
    private MeshInstance3D[] _sparks;
    private Vector3[] _dirs;
    private float[] _speeds;
    private float _ringEnergy;

    /// <param name="host">Node the ring is parented to -- the room, normally,
    /// so a teardown takes it along. Never the player: an effect that rides the
    /// character reads as attached to them rather than as having happened at a
    /// place.</param>
    public static void Spawn(Node host, Vector3 at, bool perfect)
    {
        if (host == null || !GodotObject.IsInstanceValid(host)) return;

        var ring = new ClashRing { Name = "ClashRing" };
        host.AddChild(ring);
        ring.GlobalPosition = at;
        ring.Build(perfect);
    }

    private void Build(bool perfect)
    {
        WasPerfect = perfect;
        Color tint = perfect ? PerfectColor : BlockedColor;
        _ringEnergy = perfect ? 3.5f : 1.6f;

        _ringMat = new StandardMaterial3D
        {
            AlbedoColor = tint,
            EmissionEnabled = true,
            Emission = tint,
            EmissionEnergyMultiplier = _ringEnergy,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        // A torus of radius 1, turned to face the camera and then SCALED to the
        // radius we want. Rebuilding the mesh every frame to change its radius
        // would allocate a vertex buffer per frame, which is exactly the thing
        // this pass is supposed to be removing.
        //
        // The turn matters: a TorusMesh lies in its own XZ plane, and this game
        // is on X/Y with an orthographic camera looking down Z. Left unturned
        // the ring would be edge-on and invisible -- one flat line.
        _ring = new MeshInstance3D
        {
            Name = "Ring",
            Mesh = new TorusMesh
            {
                InnerRadius = 0.86f,
                OuterRadius = 1.0f,
                Rings = 4,
                RingSegments = 28,
            },
            MaterialOverride = _ringMat,
            RotationDegrees = new Vector3(90f, 0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ring);

        _sparkMat = new StandardMaterial3D
        {
            AlbedoColor = tint,
            EmissionEnabled = true,
            Emission = tint,
            EmissionEnergyMultiplier = _ringEnergy * 0.85f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        // One mesh resource shared by all twelve. Twelve BoxMeshes would be
        // twelve vertex buffers for twelve identical boxes.
        var sparkMesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, 0.07f) };

        _sparks = new MeshInstance3D[Sparks];
        _dirs = new Vector3[Sparks];
        _speeds = new float[Sparks];

        for (int i = 0; i < Sparks; i++)
        {
            // Fanned on X/Y, because the game is on X/Y: a spark thrown along Z
            // travels toward or away from an orthographic camera and reads as a
            // dot that does not move.
            //
            // The 0.19 offset stops the fan from putting a spark exactly along
            // the horizon, where twelve evenly spaced rays otherwise line up
            // into something that looks drawn rather than thrown.
            float a = Mathf.Tau * i / Sparks + 0.19f;
            _dirs[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

            // Uneven speeds. Twelve sparks at one speed are a ring, and the
            // ring is already there; the point of the sparks is that they
            // scatter. Derived from the index rather than from RNG so a capture
            // of the same frame looks the same twice -- this project has paid
            // three times for checks made flaky by unseeded randomness.
            _speeds[i] = 3.4f + 1.9f * Mathf.Abs(Mathf.Sin(a * 2.7f));

            var spark = new MeshInstance3D
            {
                Mesh = sparkMesh,
                MaterialOverride = _sparkMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(spark);
            _sparks[i] = spark;
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;

        if (_t >= SparkLife) { QueueFree(); return; }

        // -- the ring ------------------------------------------------------
        float rk = Mathf.Min(1f, _t / RingLife);

        // Out hard, then stall. A linear expansion reads as a circle being
        // scaled; this reads as pressure leaving a point.
        float eased = 1f - Mathf.Pow(1f - rk, 4f);
        float radius = Mathf.Lerp(StartRadius, EndRadius, eased);

        // X and Z of the mesh's own space, which after the 90-degree turn are
        // the world's X and Y. The remaining axis is the tube, left alone so
        // the ring thins as it opens instead of ballooning toward the camera.
        _ring.Scale = new Vector3(radius, 1f, radius);

        float ringFade = 1f - rk;
        var rc = _ringMat.AlbedoColor;
        rc.A = ringFade * ringFade;          // squared: gone before it is huge
        _ringMat.AlbedoColor = rc;
        _ringMat.EmissionEnergyMultiplier = _ringEnergy * ringFade;
        _ring.Visible = rk < 1f;

        // -- the sparks ----------------------------------------------------
        float sk = _t / SparkLife;
        float drag = 1f - Mathf.Pow(1f - sk, 2f);   // thrown, then air-braked

        for (int i = 0; i < _sparks.Length; i++)
        {
            float travel = _speeds[i] * drag * SparkLife;
            // Gravity on the fan, not on each spark: they are struck metal, so
            // they leave fast and then remember they have weight.
            float fall = 1.6f * sk * sk;
            _sparks[i].Position = new Vector3(
                _dirs[i].X * travel,
                _dirs[i].Y * travel - fall,
                0f);

            float s = Mathf.Max(0.05f, 1f - sk);
            _sparks[i].Scale = new Vector3(s, s, s);
        }

        float sparkFade = 1f - sk;
        var sc = _sparkMat.AlbedoColor;
        sc.A = sparkFade;
        _sparkMat.AlbedoColor = sc;
        _sparkMat.EmissionEnergyMultiplier = _ringEnergy * 0.85f * sparkFade;
    }
}
