using Godot;

namespace LostCrownlike.ProceduralArt;

/// <summary>
/// A single environment building-block instance: generates its ArrayMesh
/// and ShaderMaterial at _Ready from ProceduralMeshFactory + the .gdshader
/// files in src/ProceduralArt/shaders/. Used by the test scene to place a
/// stone platform block and a wood plank/beam without hand-authored meshes.
/// </summary>
public partial class ProceduralEnvironmentBlock : MeshInstance3D
{
    public enum Kind { Stone, Wood }

    [Export] public Kind BlockKind { get; set; } = Kind.Stone;
    [Export] public Vector3 BlockSize { get; set; } = new Vector3(2f, 0.5f, 1f);
    [Export] public int Seed { get; set; } = 0;

    public override void _Ready()
    {
        var (mesh, shaderPath) = BlockKind switch
        {
            Kind.Stone => (ProceduralMeshFactory.CreateStonePlatformBlock(BlockSize, Seed), "res://src/ProceduralArt/shaders/stone.gdshader"),
            Kind.Wood => (ProceduralMeshFactory.CreateWoodPlankBeam(BlockSize, Seed), "res://src/ProceduralArt/shaders/wood.gdshader"),
            _ => (ProceduralMeshFactory.CreateStonePlatformBlock(BlockSize, Seed), "res://src/ProceduralArt/shaders/stone.gdshader"),
        };

        Mesh = mesh;
        var shader = GD.Load<Shader>(shaderPath);
        var mat = new ShaderMaterial { Shader = shader };
        SetSurfaceOverrideMaterial(0, mat);
    }
}
