using Godot;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: enemies guard the ground they were placed on, and are
/// still there later.
///
/// Every enemy in every generated room patrolled toward world x = -3, wherever
/// it happened to be standing. The builder added the enemy to the tree, THEN
/// wrote its position, THEN wired two marker nodes -- and _Ready runs on the
/// first of those three, so the enemy took its "3 metres either side of me"
/// fallback while it was still at the origin, and never read the markers at
/// all.
///
/// It survived a long time because it was survivable: a room was 64 metres and
/// the enemies were near the start, so walking left ended near the start too.
/// At 200 metres they set off across the level; three of eight walked out of
/// the world in a single run, and one stood on a spike trap until it died.
/// What found it was a corpse at (98.0, -93.7) in a check that was counting
/// bodies for an entirely different reason.
///
/// Two assertions, because either alone is weak: the anchors are near the
/// enemy (the cause), and the room still has its population after ten seconds
/// (the symptom, which would also catch a different cause).
public partial class EnemyPostTest : Node
{
    private const float MaxAnchorDrift = 4.5f;
    private const int Settle = 40;
    private const int Observe = 600;   // ten seconds at 60fps

    private int _f;
    private DungeonRoomBuilder _room;
    private int _atStart = -1;
    private int _strayAnchors;
    private int _worstAnchor;
    private int _aliveAtEnd = -1;
    private int _leftTheWorld;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_room == null || _room.IsRebuilding) return;

        // Room 2 is the busiest layout: two arenas, eight enemies.
        if (_f == 10) { _room.RebuildAs(2); return; }

        if (_f == Settle)
        {
            _atStart = 0;
            float worst = 0f;
            foreach (var e in Enemies())
            {
                _atStart++;
                float da = Mathf.Abs(e.PatrolTargetA.X - e.GlobalPosition.X);
                float db = Mathf.Abs(e.PatrolTargetB.X - e.GlobalPosition.X);
                float drift = Mathf.Max(da, db);
                worst = Mathf.Max(worst, drift);
                if (drift > MaxAnchorDrift)
                {
                    _strayAnchors++;
                    GD.Print($"[POST] enemy at x={e.GlobalPosition.X:F1} patrols to " +
                             $"{e.PatrolTargetA.X:F1} / {e.PatrolTargetB.X:F1}");
                }
            }
            _worstAnchor = Mathf.RoundToInt(worst);
            GD.Print($"[POST] {_atStart} enemies placed, {_strayAnchors} anchored away from themselves " +
                     $"(worst drift {worst:F1}m)");
            if (_atStart == 0) Done(false, "the room spawned no enemies, so this proves nothing");
            return;
        }

        if (_f == Settle + Observe)
        {
            _aliveAtEnd = 0;
            foreach (var e in Enemies())
            {
                if (e.State == EnemyController.EnemyState.Dead) continue;
                _aliveAtEnd++;
                if (e.GlobalPosition.Y < -8f) _leftTheWorld++;
            }
            GD.Print($"[POST] after {Observe / 60}s untouched: {_aliveAtEnd} of {_atStart} still alive, " +
                     $"{_leftTheWorld} below the floor");

            bool ok = _atStart > 0
                   && _strayAnchors == 0
                   && _aliveAtEnd == _atStart
                   && _leftTheWorld == 0;

            Done(ok, ok ? "enemies hold the ground they were placed on and none of them wander off it" : "");
        }
    }

    private System.Collections.Generic.IEnumerable<EnemyController> Enemies()
    {
        foreach (var c in _room.GetChildren())
            if (c is EnemyController e && !e.IsQueuedForDeletion())
                yield return e;
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[POST] RESULT: PASS ({why})"
                    : $"[POST] RESULT: FAIL{(why.Length > 0 ? " (" + why + ")" : "")}");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
