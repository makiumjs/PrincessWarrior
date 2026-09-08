using Godot;

namespace LostCrownlike.World;

/// <summary>
/// The husks the Forgemaster's arena keeps throwing at you.
///
/// A wave every twelve seconds, two at a time, from the edges of the room. They
/// are not there to kill the player -- an Emberhusk has fourteen health and a
/// nine-damage swing. They are there to make the heat a decision: each one
/// killed drains 8, each one that detonates adds 10, and both of those land
/// whatever the player does about it. The fight is what you spend the gaps on.
///
/// Parented to the room, so a rebuild takes it with everything else, and it
/// stops the moment the boss is down: waves arriving after the kill would be a
/// victory the player has to fight their way out of.
/// </summary>
public partial class CrucibleWaveSpawner : Node3D
{
    [Export] public float IntervalSeconds { get; set; } = 12f;
    [Export] public int PerWave { get; set; } = 2;

    /// Waited out before the first wave as well as between them. Dropping two
    /// husks on the player during the boss's opening walk-in would bury the one
    /// thing the fight has to establish first, which is the boss.
    [Export] public float FirstWaveDelaySeconds { get; set; } = 6f;

    /// A ceiling, not a target. Without it a player who stalls the boss for
    /// three minutes is fighting thirty husks, and the room's frame budget is
    /// not a difficulty setting.
    [Export] public int MaxAliveHusks { get; set; } = 6;

    [Export] public string HuskScenePath { get; set; } = "res://scenes/enemies/Emberhusk.tscn";

    /// Where they come in. Set by the builder to the two ends of the arena,
    /// outside the boss's reach -- a husk spawning on top of the Forgemaster
    /// would be instantly staggered by its sweep and read as a bug.
    public Vector3 LeftSpawn { get; set; }
    public Vector3 RightSpawn { get; set; }

    private PackedScene _husk;
    private float _timer;
    private int _wave;

    public override void _Ready()
    {
        _husk = GD.Load<PackedScene>(HuskScenePath);
        if (_husk == null)
            GD.PushError($"CrucibleWaveSpawner: {HuskScenePath} not found; the Forgemaster's arena has no waves");

        _timer = FirstWaveDelaySeconds;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_husk == null) return;
        if (!BossAlive()) return;

        _timer -= (float)delta;
        if (_timer > 0f) return;
        _timer = IntervalSeconds;

        SpawnWave();
    }

    /// Alive means "not in its death state", not "the node exists" -- the same
    /// definition RoomExitTrigger uses, and for the same reason: a corpse
    /// spends seconds sinking, and waves during that would arrive after the
    /// fight was decided.
    private bool BossAlive()
    {
        foreach (var n in GetTree().GetNodesInGroup("boss"))
            if (n is AI.EnemyController e
                && !e.IsQueuedForDeletion()
                && e.State != AI.EnemyController.EnemyState.Dead)
                return true;
        return false;
    }

    private void SpawnWave()
    {
        int alive = CountAliveHusks();
        int room = Mathf.Max(0, MaxAliveHusks - alive);
        int spawning = Mathf.Min(PerWave, room);
        if (spawning <= 0) return;

        _wave++;
        var host = GetParent() ?? this;

        for (int i = 0; i < spawning; i++)
        {
            // Alternating sides, and the side the wave STARTS on alternates too,
            // so two husks never arrive from the same place twice running. A
            // fight whose reinforcements always come from the left is a fight
            // the player solves by standing on the right.
            bool left = ((i + _wave) % 2) == 0;
            Vector3 at = left ? LeftSpawn : RightSpawn;

            var husk = _husk.Instantiate<Node3D>();
            host.AddChild(husk);
            husk.GlobalPosition = at;

            if (husk is AI.EnemyController controller)
                controller.SetPatrolPoints(at + new Vector3(-2.5f, 0f, 0f),
                                           at + new Vector3(2.5f, 0f, 0f));
        }

        GD.Print($"[Crucible] wave {_wave}: {spawning} husk(s), {alive} already alive");
    }

    private int CountAliveHusks()
    {
        int alive = 0;
        var host = GetParent();
        if (host == null) return 0;

        foreach (Node child in host.GetChildren())
            if (child is AI.Emberhusk husk
                && !husk.IsQueuedForDeletion()
                && husk.State != AI.EnemyController.EnemyState.Dead)
                alive++;
        return alive;
    }
}
