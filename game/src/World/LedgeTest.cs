using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: watches every enemy in the generated room and reports
/// whether any of them fell below the floor plane — i.e. whether the ledge
/// probe actually stops them walking into the generated gaps.
public partial class LedgeTest : Node
{
    private int _frame;
    private float _lowestY = 999f;
    private int _enemies;

    /// Walks the whole tree rather than one parent's children, so this works
    /// whether the node is hand-added to Main or living in its own test scene
    /// that instances Main.
    /// Walks each enemy to the right-hand lip of whatever it stands on, so it
    /// is one step from a fall and the ledge probe has to do its job.
    private void NudgeEnemiesToEdges()
    {
        foreach (var body in FindEnemies(GetTree().Root))
        {
            var space = body.GetWorld3D()?.DirectSpaceState;
            if (space == null) continue;

            // March forward until the ground stops, then stand just short of it.
            for (float dx = 0.5f; dx < 14f; dx += 0.5f)
            {
                var from = body.GlobalPosition + new Vector3(dx, 0.3f, 0f);
                var q = PhysicsRayQueryParameters3D.Create(from, from + new Vector3(0f, -1.5f, 0f), 2u);
                if (space.IntersectRay(q).Count == 0)
                {
                    body.GlobalPosition += new Vector3(dx - 0.6f, 0f, 0f);

                    // Moving the body is not enough: the enemy caches its
                    // patrol points in _Ready, so it just walks back toward
                    // where it spawned and the probe is never asked anything.
                    // Re-aim the patrol so the ONLY way to reach the far point
                    // is to step off the edge.
                    // One point BEHIND on solid ground, one BEYOND the gap.
                    // Putting both across the void leaves the enemy nowhere
                    // safe to turn to, so it falls whether the probe works or
                    // not — that tests the scenario, not the mechanic.
                    if (body is AI.EnemyController e)
                        e.SetPatrolPoints(body.GlobalPosition - new Vector3(5f, 0f, 0f),
                                          body.GlobalPosition + new Vector3(8f, 0f, 0f),
                                          headToB: true);   // start walking AT the gap

                    GD.Print($"[LEDGE] enemy at edge {body.GlobalPosition}, patrol re-aimed past the gap");
                    break;
                }
            }
        }
    }

    /// The room builder creates patrol markers as plain Marker3D children;
    /// the one ahead of the enemy is what pulls it toward the edge.
    private static Marker3D FindFarthestMarker(Node from, Vector3 near)
    {
        Marker3D best = null;
        float bestDist = float.MaxValue;
        Walk(from);
        return best;

        void Walk(Node n)
        {
            if (n is Marker3D m)
            {
                float d = Mathf.Abs(m.GlobalPosition.X - near.X);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    private static System.Collections.Generic.List<CharacterBody3D> FindEnemies(Node from)
    {
        var list = new System.Collections.Generic.List<CharacterBody3D>();
        if (from is CharacterBody3D b && !b.IsInGroup("player")) list.Add(b);
        foreach (var c in from.GetChildren()) list.AddRange(FindEnemies(c));
        return list;
    }

    private static int CountEnemies(Node from, ref float lowestY)
    {
        int n = 0;
        if (from is CharacterBody3D body && !body.IsInGroup("player"))
        {
            n++;
            lowestY = Mathf.Min(lowestY, body.GlobalPosition.Y);
        }
        foreach (var c in from.GetChildren())
            n += CountEnemies(c, ref lowestY);
        return n;
    }

    public override void _Process(double delta)
    {
        _frame++;
        // Put an enemy ON a platform edge so the probe is actually exercised.
        // Without this the test only proved enemies do not wander off during a
        // quiet patrol: disabling the probes entirely left it green.
        if (_frame == 30) NudgeEnemiesToEdges();

        int count = CountEnemies(GetTree().Root, ref _lowestY);
        _enemies = Mathf.Max(_enemies, count);

        if (_frame == 600)
        {
            GD.Print($"[LEDGE] enemies={_enemies} lowestEnemyY={_lowestY:F2} after {_frame} frames");
            // Assert the PREMISE too: "no enemy fell" is trivially true when
            // there are no enemies, so this would pass loudest exactly when the
            // room stopped spawning them.
            if (_enemies == 0)
                GD.Print("[LEDGE] RESULT: FAIL (no enemies existed - nothing was actually tested)");
            else
                GD.Print(_lowestY > -2f
                    ? "[LEDGE] RESULT: PASS (nobody fell into a gap)"
                    : "[LEDGE] RESULT: FAIL (an enemy fell)");
            GetTree().Quit();
        }
    }
}
