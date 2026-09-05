using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// The door at the end of a generated room. Rebuilds the room with the next
/// index and puts the player at its start.
///
/// Generated rooms are not .tscn files, so this cannot go through
/// LevelManager's PackedScene path — there is nothing to load. The transition
/// is a regeneration, which keeps every room inside the metric contract
/// instead of falling back to hand-authored blockout scenes.
/// </summary>
public partial class RoomExitTrigger : Area3D
{
    [Export] public int NextRoomIndex = 1;

    /// How many rooms the run is. The exit of the last room ends the run
    /// instead of rebuilding: without this the three layouts cycled forever,
    /// which is a sandbox, not a game with an ending.
    [Export] public int RunLength = 6;

    /// Set on the boss room's exit. The run does not end because the player
    /// reached the far wall; it ends because the thing guarding it is dead.
    [Export] public bool SealedUntilBossDies;

    private bool _used;
    private MeshInstance3D _seal;
    private int _recheckFrames;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = PhysicsLayers.Player;
        Monitoring = true;
        BodyEntered += OnBodyEntered;

        if (SealedUntilBossDies) BuildSeal();
    }

    /// A door the player cannot pass and cannot see is a bug report. The seal
    /// is drawn, and it is removed the frame the boss dies, so "it opened"
    /// is something that happens on screen rather than something the player
    /// has to infer by walking into the wall again.
    private void BuildSeal()
    {
        _seal = new MeshInstance3D
        {
            Name = "ExitSeal",
            Mesh = new BoxMesh { Size = new Vector3(0.35f, 3.4f, 1.9f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.85f, 0.25f, 0.18f, 0.55f),
                EmissionEnabled = true,
                Emission = new Color(0.9f, 0.3f, 0.15f),
                EmissionEnergyMultiplier = 1.6f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        AddChild(_seal);
    }

    /// Physics, not _Process: this asks the physics server what overlaps the
    /// trigger, and in _Process that answer is one frame stale -- which is
    /// exactly long enough to miss a player who was already standing here.
    public override void _PhysicsProcess(double delta)
    {
        // A few frames of re-checking after the door opens, not one. A single
        // check reads whatever the physics server had settled at that instant,
        // and the boss's death knockback is very likely still moving the player
        // through the doorway on that frame.
        if (_recheckFrames > 0 && !_used)
        {
            _recheckFrames--;
            foreach (var body in GetOverlappingBodies())
                OnBodyEntered(body);
        }

        if (!SealedUntilBossDies) return;
        if (BossAlive()) return;

        SealedUntilBossDies = false;
        _seal?.QueueFree();
        _seal = null;
        GD.Print("[Exit] the boss is dead; the way out is open");

        // The player is very likely standing in the doorway at this exact
        // moment -- it is where you back away to, and where the boss's last
        // knockback throws you. BodyEntered does not fire again for a body
        // that never left, so without this the door opens and does nothing
        // until the player steps out and walks back in. Found by a check that
        // put the player in before the kill and then saw the run refuse to end.
        _recheckFrames = 12;
    }

    /// A boss counts as alive only while it is not in its death state. Checking
    /// merely that the node exists would keep the door shut for the seconds the
    /// corpse spends sinking, which reads as the kill not having registered.
    private bool BossAlive()
    {
        foreach (var n in GetTree().GetNodesInGroup("boss"))
            if (n is AI.EnemyController e && e.State != AI.EnemyController.EnemyState.Dead)
                return true;
        return false;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_used) return;
        // Not consumed: the player will walk into this again once the boss is
        // down, and marking it used here would seal the run shut for good.
        if (SealedUntilBossDies && BossAlive())
        {
            GD.Print("[Exit] sealed: the boss is still alive");
            return;
        }
        _used = true;

        var builder = GetParent<DungeonRoomBuilder>();
        if (builder == null)
        {
            GD.PushError("RoomExitTrigger: expected a DungeonRoomBuilder parent");
            return;
        }

        if (NextRoomIndex >= RunLength)
        {
            GD.Print($"[Exit] run complete after {NextRoomIndex} rooms");
            EventBus.Instance?.EmitRunCompleted(NextRoomIndex);
            return;
        }

        GD.Print($"[Exit] entering room {NextRoomIndex}");
        EventBus.Instance?.EmitLevelTransitionRequested($"generated:{NextRoomIndex}", "start");

        // Deferred: tearing the room down while the physics server is still
        // resolving this very overlap crashes or drops collisions.
        // Spread across frames rather than done on this one: the whole build
        // is about 155ms, which is nine dropped frames at the exact moment the
        // player walks through a door.
        builder.CallDeferred(nameof(DungeonRoomBuilder.BeginRebuild), NextRoomIndex);
    }
}
