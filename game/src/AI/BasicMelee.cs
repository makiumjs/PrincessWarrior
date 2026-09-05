using Godot;

namespace LostCrownlike.AI;

/// <summary>
/// Concrete simple enemy: patrols between two points, chases the player on
/// detection, and throws a short melee swing when in range. All behavior
/// is inherited from EnemyController — this subclass exists as the named,
/// spawnable enemy type referenced from level scenes, with defaults tuned
/// for a basic grunt (see ARCHITECTURE.md "### AI").
/// </summary>
[GlobalClass]
public partial class BasicMelee : EnemyController
{
    public override void _Ready()
    {
        base._Ready();
    }
}
