using Godot;

namespace LostCrownlike.ProceduralArt;

/// <summary>
/// Builds Animation resources at runtime by sampling closed-form pose
/// formulas (sine waves per limb) at a handful of keyframes, rather than
/// hand-placed editor keyframe tracks. Tracks target the rotation of the
/// named rig pivots created by <see cref="ProceduralCharacter"/>, so the
/// same rig can play either clip through an AnimationPlayer/AnimationTree.
/// See ARCHITECTURE.md "ProceduralArt" — code-driven procedural posing.
/// </summary>
public static class ProceduralAnimationBuilder
{
    private const string Hips = "Hips";
    private const string Torso = "Hips/Torso";
    private const string Head = "Hips/Torso/Head";
    private const string UpperArmL = "Hips/Torso/UpperArmL";
    private const string LowerArmL = "Hips/Torso/UpperArmL/LowerArmL";
    private const string UpperArmR = "Hips/Torso/UpperArmR";
    private const string LowerArmR = "Hips/Torso/UpperArmR/LowerArmR";
    private const string UpperLegL = "Hips/UpperLegL";
    private const string LowerLegL = "Hips/UpperLegL/LowerLegL";
    private const string UpperLegR = "Hips/UpperLegR";
    private const string LowerLegR = "Hips/UpperLegR/LowerLegR";

    private static int AddRotTrack(Animation anim, string nodePath)
    {
        int idx = anim.AddTrack(Animation.TrackType.Value);
        anim.TrackSetPath(idx, new NodePath($"{nodePath}:rotation"));
        anim.TrackSetInterpolationType(idx, Animation.InterpolationType.Cubic);
        anim.ValueTrackSetUpdateMode(idx, Animation.UpdateMode.Continuous);
        return idx;
    }

    private static int AddPosTrack(Animation anim, string nodePath)
    {
        int idx = anim.AddTrack(Animation.TrackType.Value);
        anim.TrackSetPath(idx, new NodePath($"{nodePath}:position"));
        anim.TrackSetInterpolationType(idx, Animation.InterpolationType.Cubic);
        anim.ValueTrackSetUpdateMode(idx, Animation.UpdateMode.Continuous);
        return idx;
    }

    /// <summary>Idle: slow breathing sway, arms at rest with a small counter-sway.</summary>
    public static Animation BuildIdleAnimation(Vector3 hipsRestPos)
    {
        var anim = new Animation { Length = 2.4f, LoopMode = Animation.LoopModeEnum.Linear };

        int hipsPos = AddPosTrack(anim, Hips);
        int torsoRot = AddRotTrack(anim, Torso);
        int headRot = AddRotTrack(anim, Head);
        int upperArmL = AddRotTrack(anim, UpperArmL);
        int upperArmR = AddRotTrack(anim, UpperArmR);

        const int samples = 9;
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float time = t * (float)anim.Length;
            float phase = t * Mathf.Tau;

            float breathe = Mathf.Sin(phase);
            anim.TrackInsertKey(hipsPos, time, hipsRestPos + new Vector3(0f, breathe * 0.015f, 0f));
            anim.TrackInsertKey(torsoRot, time, new Vector3(breathe * 0.03f, 0f, Mathf.Sin(phase * 0.5f) * 0.02f));
            anim.TrackInsertKey(headRot, time, new Vector3(-breathe * 0.02f, Mathf.Sin(phase * 0.33f) * 0.05f, 0f));
            anim.TrackInsertKey(upperArmL, time, new Vector3(0.05f, 0f, 0.18f + breathe * 0.02f));
            anim.TrackInsertKey(upperArmR, time, new Vector3(0.05f, 0f, -0.18f - breathe * 0.02f));
        }

        return anim;
    }

    /// <summary>Run: opposite arm/leg swing plus a hip bounce, classic walk-cycle sines.</summary>
    public static Animation BuildRunAnimation(Vector3 hipsRestPos)
    {
        var anim = new Animation { Length = 0.55f, LoopMode = Animation.LoopModeEnum.Linear };

        int hipsPos = AddPosTrack(anim, Hips);
        int hipsRot = AddRotTrack(anim, Hips);
        int torsoRot = AddRotTrack(anim, Torso);
        int upperArmL = AddRotTrack(anim, UpperArmL);
        int lowerArmL = AddRotTrack(anim, LowerArmL);
        int upperArmR = AddRotTrack(anim, UpperArmR);
        int lowerArmR = AddRotTrack(anim, LowerArmR);
        int upperLegL = AddRotTrack(anim, UpperLegL);
        int lowerLegL = AddRotTrack(anim, LowerLegL);
        int upperLegR = AddRotTrack(anim, UpperLegR);
        int lowerLegR = AddRotTrack(anim, LowerLegR);

        const int samples = 12;
        const float swing = 0.9f;
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float time = t * (float)anim.Length;
            float phase = t * Mathf.Tau;

            float legL = Mathf.Sin(phase);
            float legR = Mathf.Sin(phase + Mathf.Pi);
            float bounce = Mathf.Abs(Mathf.Sin(phase)) ;

            anim.TrackInsertKey(hipsPos, time, hipsRestPos + new Vector3(0f, bounce * 0.06f, 0f));
            anim.TrackInsertKey(hipsRot, time, new Vector3(0f, 0f, Mathf.Sin(phase * 2f) * 0.03f));
            anim.TrackInsertKey(torsoRot, time, new Vector3(0.12f, 0f, -legL * 0.06f));

            // Arms swing opposite to same-side legs.
            anim.TrackInsertKey(upperArmL, time, new Vector3(-legR * swing * 0.6f, 0f, 0.15f));
            anim.TrackInsertKey(lowerArmL, time, new Vector3(Mathf.Max(0.2f, -legR * 0.5f + 0.6f), 0f, 0f));
            anim.TrackInsertKey(upperArmR, time, new Vector3(-legL * swing * 0.6f, 0f, -0.15f));
            anim.TrackInsertKey(lowerArmR, time, new Vector3(Mathf.Max(0.2f, -legL * 0.5f + 0.6f), 0f, 0f));

            // Legs: thigh swings on sine, knee bends more while the leg is
            // forward-swinging (recovering) than while planted/pushing.
            float kneeBendL = Mathf.Max(0f, legL) * 1.1f + 0.05f;
            float kneeBendR = Mathf.Max(0f, legR) * 1.1f + 0.05f;
            anim.TrackInsertKey(upperLegL, time, new Vector3(legL * swing * 0.8f, 0f, 0f));
            anim.TrackInsertKey(lowerLegL, time, new Vector3(kneeBendL, 0f, 0f));
            anim.TrackInsertKey(upperLegR, time, new Vector3(legR * swing * 0.8f, 0f, 0f));
            anim.TrackInsertKey(lowerLegR, time, new Vector3(kneeBendR, 0f, 0f));
        }

        return anim;
    }
}
