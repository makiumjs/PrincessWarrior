using Godot;

namespace LostCrownlike.ProceduralArt;

/// <summary>
/// Builds ArrayMeshes at load time via SurfaceTool. No imported meshes/textures
/// anywhere in this subsystem — see ARCHITECTURE.md "ProceduralArt".
///
/// Two mesh families:
///  - Capsules (CreateCapsule) for character limb/torso/head segments.
///  - Rounded/beveled blocks (CreateRoundedBlock) for environment building
///    blocks (stone platform, wood beam), using a cube->sphere blend for the
///    bevel plus optional per-vertex hash-noise displacement for surface
///    variation (no textures, so the "grain"/"roughness" read has to come
///    from geometry + the shader, not a bump map).
/// </summary>
public static class ProceduralMeshFactory
{
    // ---------------------------------------------------------------
    // Cheap deterministic hash noise (CPU side). Mirrors the style of
    // hash used in the .gdshader files, just evaluated in C# instead
    // of GLSL, so mesh displacement and shader surface variation look
    // like they come from the same "material system".
    // ---------------------------------------------------------------
    private static float Hash31(Vector3 p, int seed)
    {
        float h = Mathf.Sin(p.Dot(new Vector3(12.9898f, 78.233f, 45.164f)) + seed * 0.6180339f) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    /// <summary>
    /// A procedural capsule (hemisphere + cylinder + hemisphere), built by hand
    /// with SurfaceTool rather than Godot's built-in CapsuleMesh, so normals/UVs
    /// are explicit and the geometry can be reused for every limb/torso/head
    /// segment of the character rig with consistent topology.
    /// </summary>
    public static ArrayMesh CreateCapsule(float radius, float cylinderHeight, int radialSegments = 8, int capRings = 4)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float halfCyl = cylinderHeight * 0.5f;
        int ringsPerCap = Mathf.Max(1, capRings);

        // Each "row" is a horizontal ring of vertices; we stack: top pole cap
        // rings, cylinder rings, bottom pole cap rings.
        var rowStartYs = new System.Collections.Generic.List<(float y, float ringRadius, float vAngle)>();

        // Top hemisphere: from pole (angle = PI/2) down to equator (angle = 0)
        for (int r = 0; r <= ringsPerCap; r++)
        {
            float t = (float)r / ringsPerCap;
            float phi = Mathf.Pi * 0.5f * (1f - t); // PI/2 .. 0
            float y = halfCyl + radius * Mathf.Sin(phi);
            float ringRadius = radius * Mathf.Cos(phi);
            rowStartYs.Add((y, ringRadius, phi));
        }
        // Bottom hemisphere: equator down to pole
        for (int r = 0; r <= ringsPerCap; r++)
        {
            float t = (float)r / ringsPerCap;
            float phi = -Mathf.Pi * 0.5f * t; // 0 .. -PI/2
            float y = -halfCyl + radius * Mathf.Sin(phi);
            float ringRadius = radius * Mathf.Cos(phi);
            rowStartYs.Add((y, ringRadius, phi));
        }

        int rows = rowStartYs.Count;
        int cols = radialSegments + 1; // duplicate seam vertex for clean UVs

        for (int row = 0; row < rows; row++)
        {
            var (y, ringRadius, phi) = rowStartYs[row];
            float v = (float)row / (rows - 1);
            for (int col = 0; col < cols; col++)
            {
                float u = (float)col / radialSegments;
                float theta = u * Mathf.Tau;
                float cx = Mathf.Cos(theta);
                float cz = Mathf.Sin(theta);
                var pos = new Vector3(cx * ringRadius, y, cz * ringRadius);

                // Normal: radial direction blended with pole direction via phi,
                // exact for a true capsule (sphere caps + cylinder side).
                Vector3 normal = ringRadius > 0.0001f
                    ? new Vector3(cx * Mathf.Cos(phi), Mathf.Sin(phi), cz * Mathf.Cos(phi))
                    : new Vector3(0f, Mathf.Sign(phi == 0 ? 1f : phi), 0f);

                st.SetNormal(normal);
                st.SetUV(new Vector2(u, v));
                st.AddVertex(pos);
            }
        }

        for (int row = 0; row < rows - 1; row++)
        {
            for (int col = 0; col < radialSegments; col++)
            {
                int i0 = row * cols + col;
                int i1 = i0 + 1;
                int i2 = i0 + cols;
                int i3 = i2 + 1;

                st.AddIndex(i0);
                st.AddIndex(i2);
                st.AddIndex(i1);

                st.AddIndex(i1);
                st.AddIndex(i2);
                st.AddIndex(i3);
            }
        }

        st.GenerateTangents();
        return st.Commit();
    }

    /// <summary>
    /// A rounded/beveled box: a subdivided cube whose vertices are blended
    /// between the flat cube position and its "spherified" projection
    /// (the standard cube-to-sphere mapping), giving smooth beveled edges
    /// without a bevel modifier or extra geometry passes. <paramref name="bevel"/>
    /// of 0 = sharp box, 1 = full ellipsoid. Optional per-vertex hash-noise
    /// displacement along the normal adds surface variation (rough stone,
    /// dented wood) so it doesn't read as a bare primitive.
    /// </summary>
    public static ArrayMesh CreateRoundedBlock(Vector3 size, float bevel = 0.25f, int subdivisions = 5,
        float noiseAmplitude = 0f, float noiseFrequency = 3f, int noiseSeed = 0)
    {
        bevel = Mathf.Clamp(bevel, 0f, 1f);
        subdivisions = Mathf.Max(1, subdivisions);
        Vector3 half = size * 0.5f;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // 6 faces, each defined by an origin corner + two edge axes on the unit cube.
        var faces = new (Vector3 normal, Vector3 axisU, Vector3 axisV)[6]
        {
            (Vector3.Right,   Vector3.Forward, Vector3.Up),      // +X
            (Vector3.Left,    Vector3.Back,    Vector3.Up),      // -X
            (Vector3.Up,      Vector3.Right,   Vector3.Back),    // +Y
            (Vector3.Down,    Vector3.Right,   Vector3.Forward), // -Y
            (Vector3.Back,    Vector3.Right,   Vector3.Up),      // +Z (Back = +Z in Godot)
            (Vector3.Forward, Vector3.Left,    Vector3.Up),      // -Z
        };

        int vertsPerFace = (subdivisions + 1) * (subdivisions + 1);
        int vertexCursor = 0;

        foreach (var face in faces)
        {
            for (int row = 0; row <= subdivisions; row++)
            {
                float v = (float)row / subdivisions * 2f - 1f;
                for (int col = 0; col <= subdivisions; col++)
                {
                    float u = (float)col / subdivisions * 2f - 1f;

                    Vector3 cubePoint = face.normal + face.axisU * u + face.axisV * v;

                    // Standard cube -> sphere warp (Nvidia "spherified cube").
                    float x = cubePoint.X, y = cubePoint.Y, z = cubePoint.Z;
                    float x2 = x * x, y2 = y * y, z2 = z * z;
                    var sphered = new Vector3(
                        x * Mathf.Sqrt(Mathf.Max(0f, 1f - y2 * 0.5f - z2 * 0.5f + y2 * z2 / 3f)),
                        y * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 * 0.5f - z2 * 0.5f + x2 * z2 / 3f)),
                        z * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 * 0.5f - y2 * 0.5f + x2 * y2 / 3f)));

                    Vector3 blended = cubePoint.Lerp(sphered, bevel);
                    Vector3 pos = blended * half;

                    Vector3 normal = cubePoint.Lerp(sphered, Mathf.Max(bevel, 0.15f)).Normalized();

                    if (noiseAmplitude > 0f)
                    {
                        float n = Hash31(pos * noiseFrequency, noiseSeed);
                        float disp = (n - 0.5f) * 2f * noiseAmplitude;
                        pos += normal * disp;
                    }

                    st.SetNormal(normal);
                    st.SetUV(new Vector2((u + 1f) * 0.5f, (v + 1f) * 0.5f));
                    st.AddVertex(pos);
                }
            }

            int stride = subdivisions + 1;
            for (int row = 0; row < subdivisions; row++)
            {
                for (int col = 0; col < subdivisions; col++)
                {
                    int i0 = vertexCursor + row * stride + col;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;

                    st.AddIndex(i0);
                    st.AddIndex(i1);
                    st.AddIndex(i2);

                    st.AddIndex(i1);
                    st.AddIndex(i3);
                    st.AddIndex(i2);
                }
            }

            vertexCursor += vertsPerFace;
        }

        st.GenerateTangents();
        return st.Commit();
    }

    /// <summary>Stone platform block: heavier bevel, noticeable rough-surface noise.</summary>
    public static ArrayMesh CreateStonePlatformBlock(Vector3 size, int seed = 0) =>
        CreateRoundedBlock(size, bevel: 0.18f, subdivisions: 8, noiseAmplitude: size.Length() * 0.012f,
            noiseFrequency: 4f, noiseSeed: seed);

    /// <summary>Wooden plank/beam: lighter bevel (sawn edge), subtle noise (splinters/grain bumps).</summary>
    public static ArrayMesh CreateWoodPlankBeam(Vector3 size, int seed = 0) =>
        CreateRoundedBlock(size, bevel: 0.06f, subdivisions: 6, noiseAmplitude: size.Length() * 0.004f,
            noiseFrequency: 8f, noiseSeed: seed);
}
