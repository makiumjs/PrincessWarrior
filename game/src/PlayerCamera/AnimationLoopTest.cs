using Godot;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: clips that describe a CONTINUING state actually loop.
///
/// Reported from play, not caught by any check: after the first 0.80s of
/// Running_A the model froze in its last pose and the character appeared to
/// slide. Every KayKit GLB imports with LoopMode.None, and nothing here had
/// ever looked at an animation after its first cycle -- the captures in this
/// project are single frames, so a clip that stops is indistinguishable from
/// one that plays.
///
/// Checked on the player AND on an enemy, because they build the same
/// libraries from the same files and the fix was applied to the player first.
/// A one-shot clip is checked too: looping Jump_Start or Hit_A would be its own
/// bug, so the check has to fail in both directions.
public partial class AnimationLoopTest : Node
{
    private int _f;

    public override void _Process(double delta)
    {
        if (++_f != 40) return;

        var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
        var enemy = FindFirst<AI.EnemyController>(GetTree().Root);

        var pAnim = FindFirst<AnimationPlayer>(player);
        var eAnim = FindFirst<AnimationPlayer>(enemy);

        int pLooping = CountLooping(pAnim, out int pOneShotWronglyLooped);
        int eLooping = CountLooping(eAnim, out int eOneShotWronglyLooped);

        GD.Print($"[ANIM] player: {pLooping} continuing clips loop, " +
                 $"{pOneShotWronglyLooped} one-shot clips wrongly looped");
        GD.Print($"[ANIM] enemy:  {eLooping} continuing clips loop, " +
                 $"{eOneShotWronglyLooped} one-shot clips wrongly looped");

        bool ok = pAnim != null && eAnim != null
               && pLooping == PlayerVisual.Looping.Count
               && eLooping == PlayerVisual.Looping.Count
               && pOneShotWronglyLooped == 0
               && eOneShotWronglyLooped == 0;

        GD.Print(ok
            ? "[ANIM] RESULT: PASS (continuing clips loop on both player and enemies; one-shots do not)"
            : "[ANIM] RESULT: FAIL");
        GetTree().Quit();
    }

    private static int CountLooping(AnimationPlayer ap, out int oneShotLooped)
    {
        oneShotLooped = 0;
        if (ap == null) return -1;

        int looping = 0;
        foreach (string key in ap.GetAnimationList())
        {
            string name = key;
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);

            bool loops = ap.GetAnimation(key).LoopMode != Animation.LoopModeEnum.None;
            if (PlayerVisual.Looping.Contains(name)) { if (loops) looping++; }
            else if (loops) oneShotLooped++;
        }
        return looping;
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
