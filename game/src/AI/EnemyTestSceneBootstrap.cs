using Godot;

namespace LostCrownlike.AI;

/// <summary>
/// TEST-SCENE ONLY. NavigationMesh resources aren't runtime-baked
/// automatically when hand-authored (no editor bake pass ran on this
/// file), so this triggers a synchronous bake against the floor geometry
/// on scene load. Attach to the root of scenes/enemies/EnemyTestScene.tscn.
/// </summary>
[GlobalClass]
public partial class EnemyTestSceneBootstrap : Node3D
{
    [Export] public NodePath NavigationRegionPath { get; set; }

    public override void _Ready()
    {
        var region = GetNodeOrNull<NavigationRegion3D>(NavigationRegionPath);

        region?.BakeNavigationMesh(false);

    }
}
