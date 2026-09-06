using Godot;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: the model is still MOVING a few seconds into a run.
///
/// Check 40 asserts LoopMode is set. That is the fix, not the symptom, and a
/// check written against the fix cannot catch the next way this breaks -- a
/// clip that loops but is never advanced, an AnimationPlayer left paused, a
/// state machine that stops calling Play.
///
/// This watches the skeleton instead. It holds "run" for three seconds and
/// measures how much the pose changes per frame, in the FIRST second and in the
/// LAST. Before the loop fix those two numbers were "moving" and "frozen":
/// Running_A is 0.80s long, so the model held its final pose for the remaining
/// 2.2s while the body kept sliding forward. That is what a person saw
/// immediately and what forty checks did not, because every capture in this
/// project is a single frame and a stopped clip photographs exactly like a
/// playing one.
public partial class AnimationMotionTest : Node
{
    private const int WarmUp = 40;
    private const int Window = 180;   // 3 seconds at the fixed rate

    private int _f;
    private Skeleton3D _skel;
    private int _bone = -1;
    private Quaternion _previous;
    private double _earlyMotion;
    private double _lateMotion;
    private int _earlySamples, _lateSamples;

    /// The legs, sampled separately and WHILE ATTACKING. Reported from play:
    /// hit something while running and the character slides. Slash_A has five
    /// tracks -- chest, arms -- and touches no leg, but AnimationPlayer plays
    /// one clip at a time, so starting the swing stopped the run and left the
    /// legs holding a pose while the body kept travelling. The upper body is
    /// layered over the legs now; this is the number that says so.
    private int _legBone = -1;
    private Quaternion _legPrevious;
    private double _legMotionWhileSwinging, _legMotionWhileFree;
    private int _legSamples, _legFreeSamples, _swingFrames;

    private Combat.CombatController _combat;

    public override void _Process(double delta)
    {
        _f++;
        if (_combat == null && GetTree().GetFirstNodeInGroup("player") is Node p)
            _combat = p.GetNodeOrNull<Combat.CombatController>("CombatController");

        if (_f == 10)
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            _skel = FindFirst<Skeleton3D>(player);
            if (_skel == null) { GD.Print("[MOTION] RESULT: FAIL (no skeleton)"); GetTree().Quit(); return; }

            // A limb, not the root: the root barely moves in a run cycle, so
            // sampling it would report "frozen" for a perfectly good animation.
            for (int i = 0; i < _skel.GetBoneCount(); i++)
            {
                string n = _skel.GetBoneName(i).ToLower();
                if (n.Contains("leg") || n.Contains("thigh") || n.Contains("arm"))
                {
                    _bone = i;
                    GD.Print($"[MOTION] sampling bone '{_skel.GetBoneName(i)}'");
                    break;
                }
            }
            if (_bone < 0) _bone = _skel.GetBoneCount() / 2;

            // A LEG specifically, and never an arm: the arm is what the attack
            // clip drives, so measuring one would report the swing as proof
            // that the run survived it.
            for (int i = 0; i < _skel.GetBoneCount(); i++)
            {
                string n = _skel.GetBoneName(i).ToLower();
                if (n.Contains("leg") || n.Contains("thigh") || n.Contains("shin"))
                {
                    _legBone = i;
                    GD.Print($"[MOTION] sampling leg '{_skel.GetBoneName(i)}' during the swing");
                    break;
                }
            }
            if (_legBone >= 0) _legPrevious = _skel.GetBonePoseRotation(_legBone);

            Input.ActionPress("move_right");
            _previous = _skel.GetBonePoseRotation(_bone);
            return;
        }

        if (_f < WarmUp || _skel == null) return;

        // Attack on a cadence while running, so the swing overlaps the run
        // rather than replacing it. Pressed and released: the buffer fills from
        // an edge, and a held button produces none.
        int phase = (_f - WarmUp) % 40;
        if (phase == 0) Input.ActionPress("attack_light");
        if (phase == 3) Input.ActionRelease("attack_light");

        if (_legBone >= 0 && _combat != null && _combat.IsSwinging)
        {
            var leg = _skel.GetBonePoseRotation(_legBone);
            _legMotionWhileSwinging += _legPrevious.AngleTo(leg);
            _legPrevious = leg;
            _legSamples++;
            _swingFrames++;
        }
        else if (_legBone >= 0)
        {
            // The control, and it is the half that gives the claim teeth. An
            // absolute floor on the swinging figure passes on a leg that merely
            // twitches: replacing the whole body with the attack clip still
            // measured 0.023 rad, which clears any threshold small enough to be
            // safe. Running legs against running-and-swinging legs does not.
            var leg = _skel.GetBonePoseRotation(_legBone);
            _legMotionWhileFree += _legPrevious.AngleTo(leg);
            _legPrevious = leg;
            _legFreeSamples++;
        }

        var now = _skel.GetBonePoseRotation(_bone);
        double moved = _previous.AngleTo(now);
        _previous = now;

        int since = _f - WarmUp;
        if (since < 60) { _earlyMotion += moved; _earlySamples++; }
        else if (since >= Window - 60 && since < Window) { _lateMotion += moved; _lateSamples++; }

        if (since == Window)
        {
            Input.ActionRelease("move_right");
            double early = _earlyMotion / Mathf.Max(1, _earlySamples);
            double late = _lateMotion / Mathf.Max(1, _lateSamples);

            GD.Print($"[MOTION] pose change per frame — first second: {early:F5} rad, " +
                     $"third second: {late:F5} rad");

            double legs = _legMotionWhileSwinging / Mathf.Max(1, _legSamples);
            double legsFree = _legMotionWhileFree / Mathf.Max(1, _legFreeSamples);
            GD.Print($"[MOTION] legs while swinging {legs:F5} rad/frame vs {legsFree:F5} " +
                     $"running free — {legs / Mathf.Max(legsFree, 1e-6):P0} of it, " +
                     $"over {_swingFrames} swinging frames");

            // The late window must still be moving, and comparably so. A tiny
            // absolute threshold alone would pass on a model that twitches.
            // The leg bound is absolute, and it took two tries to make it mean
            // anything. A floor of 0.001 rad passed on a body wholly replaced by
            // the attack clip, which still measured 0.023. A RATIO against legs
            // running free passed too, and worse: that mutation collapses both
            // figures, so the ratio read 289% while nothing was running at all.
            // A control only controls for what it does not share.
            //
            // 0.05 rad/frame sits between a measured run cycle at 0.104-0.108
            // and a measured collapse at 0.023, and states what the mechanic is
            // for: a leg still sweeping a run cycle while the arms swing.
            bool ok = late > 0.001 && late > early * 0.4
                   && _swingFrames > 30 && _legFreeSamples > 30
                   && legs > 0.05;

            GD.Print(ok
                ? "[MOTION] RESULT: PASS (the run animation is still advancing three seconds in)"
                : "[MOTION] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from == null) return null;
        if (from is T t) return t;
        foreach (var c in from.GetChildren())
        {
            var f = FindFirst<T>(c);
            if (f != null) return f;
        }
        return null;
    }
}
