using Godot;

namespace LostCrownlike.World;

/// <summary>
/// The air in a room, as forty specks of it.
///
/// The rooms have been geometrically correct and atmospherically empty: a
/// corridor of lit stone with nothing between the camera and the wall. This
/// puts something in that gap -- embers climbing out of the Crucible's floor,
/// damp motes drifting down everywhere else -- and it is the cheapest depth
/// cue available, because a moving speck at a known distance tells the eye how
/// far away the wall behind it is.
///
/// ONE NODE, not forty. A MultiMeshInstance3D draws every mote in a single
/// draw call from a single mesh, which matters twice: it is a fraction of the
/// GPU cost of forty MeshInstance3Ds, and -- the reason it is written this way
/// rather than that way -- the node-leak check counts NODES across twelve
/// consecutive room rebuilds. Forty per room would be four hundred and eighty
/// nodes of churn a run for an effect nobody would trade a green gate for.
///
/// Parented to the ROOM by the builder, so a rebuild frees it with everything
/// else. There is no _ExitTree cleanup here because there is nothing to clean
/// up: the multimesh and its material die with the node.
///
/// Allocation: every per-mote array is sized once in Configure. The per-frame
/// loop writes Transform3D structs into a buffer the engine already owns, so a
/// twelve-minute run hands the collector nothing.
/// </summary>
public partial class BiomeMotes : MultiMeshInstance3D
{
    public enum Mood
    {
        /// The Crucible. Embers climbing out of a floor that is barely holding.
        Ember,
        /// Everywhere else. Dust and damp, settling.
        Damp,
    }

    /// The volume the motes live in, centred on the camera. Wide enough that a
    /// player running at 8 m/s never reaches an edge before the wrap does.
    [Export] public Vector3 FieldSize { get; set; } = new(26f, 16f, 4f);

    [Export] public int MoteCount { get; set; } = 40;

    /// Deterministic. Two runs of the same room look the same, which is what
    /// lets a capture be compared against a previous one -- and this project
    /// has three times been bitten by checks made flaky by unseeded randomness.
    [Export] public int Seed { get; set; } = 90210;

    private Mood _mood;
    private MultiMesh _mm;
    private StandardMaterial3D _material;
    private Camera3D _camera;

    // Per-mote state, allocated once.
    private Vector3[] _home;      // position within the field
    private float[] _speed;       // metres per second, signed by mood
    private float[] _wobblePhase;
    private float[] _wobbleRate;
    private float[] _wobbleAmp;
    private float[] _scale;

    private float _t;

    /// <summary>
    /// Builds the field. Called by the builder immediately after AddChild, so
    /// the node is in the tree and GlobalPosition is meaningful.
    /// </summary>
    public void Configure(Mood mood)
    {
        _mood = mood;

        bool ember = mood == Mood.Ember;

        _material = new StandardMaterial3D
        {
            // Unshaded and additive for embers -- they are light, not lit
            // objects -- and plain alpha for damp, which is dust catching what
            // the torches spill.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = ember ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            AlbedoColor = ember ? new Color(1f, 0.376f, 0.063f, 0.85f)   // #FF6010
                                : new Color(0.600f, 0.733f, 0.867f, 0.18f), // #99BBDD
            EmissionEnabled = ember,
            Emission = new Color(1f, 0.42f, 0.10f),
            EmissionEnergyMultiplier = ember ? 3.0f : 0f,
            // Always facing the camera: a quad seen edge-on is a mote that
            // blinks out of existence once per rotation.
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            // Motes must never occlude or be occluded by the fight, and they
            // must not write depth over each other.
            NoDepthTest = false,
            DisableReceiveShadows = true,
        };

        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new QuadMesh { Size = new Vector2(1f, 1f) },
        };
        // Set last: Godot reallocates the instance buffer here, and doing it
        // before the mesh and format are in place throws the buffer away.
        _mm.InstanceCount = Mathf.Max(1, MoteCount);

        Multimesh = _mm;
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;

        int n = _mm.InstanceCount;
        _home = new Vector3[n];
        _speed = new float[n];
        _wobblePhase = new float[n];
        _wobbleRate = new float[n];
        _wobbleAmp = new float[n];
        _scale = new float[n];

        var rng = new System.Random(Seed);
        for (int i = 0; i < n; i++)
        {
            _home[i] = new Vector3(
                ((float)rng.NextDouble() - 0.5f) * FieldSize.X,
                ((float)rng.NextDouble() - 0.5f) * FieldSize.Y,
                ((float)rng.NextDouble() - 0.5f) * FieldSize.Z);

            // Embers climb, dust falls. The sign is the whole difference in
            // behaviour between the two biomes; everything else is palette.
            float baseSpeed = ember ? 0.55f + (float)rng.NextDouble() * 0.75f
                                    : 0.16f + (float)rng.NextDouble() * 0.22f;
            _speed[i] = ember ? baseSpeed : -baseSpeed;

            _wobblePhase[i] = (float)rng.NextDouble() * Mathf.Tau;
            _wobbleRate[i] = ember ? 0.7f + (float)rng.NextDouble() * 1.1f
                                   : 0.25f + (float)rng.NextDouble() * 0.4f;
            _wobbleAmp[i] = ember ? 0.25f + (float)rng.NextDouble() * 0.45f
                                  : 0.35f + (float)rng.NextDouble() * 0.5f;

            _scale[i] = ember ? 0.035f + (float)rng.NextDouble() * 0.045f
                              : 0.05f + (float)rng.NextDouble() * 0.06f;
        }

        WriteInstances();
    }

    public override void _Process(double delta)
    {
        if (_mm == null) return;

        _t += (float)delta;
        FollowCamera();

        float dt = (float)delta;
        float halfY = FieldSize.Y * 0.5f;

        for (int i = 0; i < _home.Length; i++)
        {
            var h = _home[i];
            h.Y += _speed[i] * dt;

            // Wrap rather than respawn. A mote that reached the top and was
            // deleted would need a node created to replace it; moving it to the
            // other end costs one float and keeps the count fixed forever.
            if (h.Y > halfY)
            {
                h.Y = -halfY;
                h.X = Wrapped(h.X + 5.37f);   // shifted, so it does not fall in a column
            }
            else if (h.Y < -halfY)
            {
                h.Y = halfY;
                h.X = Wrapped(h.X + 5.37f);
            }

            _home[i] = h;
        }

        WriteInstances();
    }

    private float Wrapped(float x)
    {
        float half = FieldSize.X * 0.5f;
        while (x > half) x -= FieldSize.X;
        while (x < -half) x += FieldSize.X;
        return x;
    }

    /// The field rides the camera, so the air is populated wherever the player
    /// is rather than only where the room happened to start. Re-resolved when
    /// the cached camera goes: rooms are rebuilt constantly and a stale
    /// reference here throws on the frame after a transition.
    private void FollowCamera()
    {
        if (_camera == null || !IsInstanceValid(_camera))
            _camera = GetViewport()?.GetCamera3D();

        if (_camera == null) return;

        var c = _camera.GlobalPosition;
        // X and Y only. Z stays on the play plane: motes drifting toward the
        // camera would cross in front of the character and read as dirt on the
        // lens rather than as air in the room.
        GlobalPosition = new Vector3(c.X, c.Y, 0f);
    }

    private void WriteInstances()
    {
        for (int i = 0; i < _home.Length; i++)
        {
            // Sinusoidal drift on X. Embers rise through moving air and dust
            // never falls straight; a column of specks travelling in parallel
            // reads as rain, which is the one thing this must not look like.
            float wobble = Mathf.Sin(_t * _wobbleRate[i] + _wobblePhase[i]) * _wobbleAmp[i];

            var basis = Basis.Identity.Scaled(new Vector3(_scale[i], _scale[i], _scale[i]));

            _mm.SetInstanceTransform(i, new Transform3D(
                basis,
                new Vector3(_home[i].X + wobble, _home[i].Y, _home[i].Z)));
        }
    }
}
