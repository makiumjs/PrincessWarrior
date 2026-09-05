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

    public override void _Process(double delta)
    {
        _f++;

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

            Input.ActionPress("move_right");
            _previous = _skel.GetBonePoseRotation(_bone);
            return;
        }

        if (_f < WarmUp || _skel == null) return;

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

            // The late window must still be moving, and comparably so. A tiny
            // absolute threshold alone would pass on a model that twitches.
            bool ok = late > 0.001 && late > early * 0.4;

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
