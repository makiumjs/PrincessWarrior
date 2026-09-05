using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// Checkpoint/save-shrine trigger: an Area3D that, when the player body
/// overlaps it, reports itself reached via EventBus. LevelManager listens
/// and records the checkpoint's position for its own bookkeeping; Save
/// (a separate subsystem) also listens independently, per ARCHITECTURE.md.
/// </summary>
[GlobalClass]
public partial class CheckpointTrigger : Area3D
{
    [Export] public string CheckpointId = "";

    public override void _Ready()
    {
        AddToGroup("checkpoints");
        CollisionLayer = 0;
        CollisionMask = PhysicsLayers.Player;
        Monitoring = true;
        BodyEntered += OnBodyEntered;
    }

    /// The last checkpoint id announced by ANY trigger. Shared, because the
    /// point is "is this already the active checkpoint", not "has this
    /// particular node fired".
    private static string _activeCheckpointId = "";

    private void OnBodyEntered(Node3D body)
    {
        if (string.IsNullOrEmpty(CheckpointId))
            return;

        // Re-entry must not re-announce. The player respawns INSIDE its own
        // checkpoint, so every death produced another BodyEntered, and each one
        // made SaveManager write savegame.tres to disk again and re-show the
        // HUD prompt — which is why the prompt appeared to be stuck on screen
        // permanently. Re-arms as soon as a different checkpoint is reached, so
        // backtracking to an earlier one still works.
        if (_activeCheckpointId == CheckpointId)
            return;

        _activeCheckpointId = CheckpointId;
        EventBus.Instance?.EmitCheckpointReached(CheckpointId);
    }

    /// Rooms are rebuilt on transition; the static latch must not survive into
    /// a new room, or its first checkpoint would be silently skipped.
    public override void _ExitTree()
    {
        if (_activeCheckpointId == CheckpointId)
            _activeCheckpointId = "";
    }
}
