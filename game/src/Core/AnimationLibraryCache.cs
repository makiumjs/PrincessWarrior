using System.Collections.Generic;
using Godot;

namespace LostCrownlike.Core;

/// Loads the shared KayKit animation libraries once, not once per character.
///
/// Every enemy and the player each instantiated the two animation GLBs, walked
/// them for an AnimationPlayer and deep-duplicated its libraries. Measured on a
/// room rebuild after the phase timings were cleaned up: spawning three enemies
/// cost about 100ms of the 231ms build, and it is this.
///
/// An AnimationLibrary is a Resource and several AnimationPlayers can share
/// one -- playback position is per player, not per library. The only mutation
/// this project makes to them is setting LoopMode on the continuing clips, and
/// that is now done once here rather than per character, which is also why it
/// cannot drift between the player and the enemies any more.
public static class AnimationLibraryCache
{
    private static readonly Dictionary<string, List<AnimationLibrary>> Cache = new();

    /// Clips that describe a CONTINUING state. The KayKit GLBs import every
    /// clip with LoopMode.None, so a run cycle stopped after 0.80s and the
    /// model held its last pose while the body kept moving.
    public static readonly HashSet<string> Looping = new()
    {
        // KayKit
        "Idle_A", "Idle_B", "Running_A", "Running_B",
        "Walking_A", "Walking_B", "Walking_C", "Jump_Idle",
        // Quaternius
        "Crouch_Fwd", "Crouch_Idle", "Dance", "Driving", "Idle", "Idle_Talking", "Idle_Torch",
        "Jog_Fwd", "Jump", "Pistol_Idle", "Push", "Sitting_Idle", "Sitting_Talking",
        "Spell_Simple_Idle", "Sprint", "Swim_Fwd", "Swim_Idle", "Walk", "Walk_Formal",
        "Idle_FoldArms", "Idle_Lantern", "Idle_No", "Idle_Rail", "Idle_Shield",
        "Idle_TalkingPhone", "NinjaJump_Idle", "Slide", "TreeChopping", "Walk_Carry",
        "Zombie_Idle", "Zombie_Walk_Fwd",
    };

    public static IReadOnlyList<AnimationLibrary> Get(string scenePath)
    {
        if (Cache.TryGetValue(scenePath, out var cached)) return cached;

        var libs = new List<AnimationLibrary>();
        var scene = GD.Load<PackedScene>(scenePath);
        if (scene != null)
        {
            var root = scene.Instantiate<Node3D>();
            var source = FindAnimationPlayer(root);
            if (source != null)
            {
                foreach (var libName in source.GetAnimationLibraryList())
                {
                    var lib = (AnimationLibrary)source.GetAnimationLibrary(libName).Duplicate(true);
                    ApplyLooping(lib);
                    libs.Add(lib);
                }
            }
            root.QueueFree();
        }

        Cache[scenePath] = libs;
        return libs;
    }

    private static void ApplyLooping(AnimationLibrary lib)
    {
        foreach (string key in lib.GetAnimationList())
        {
            if (!Looping.Contains(key)) continue;
            lib.GetAnimation(key).LoopMode = Animation.LoopModeEnum.Linear;
        }
    }

    private static AnimationPlayer FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var child in node.GetChildren())
        {
            var found = FindAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }
}
