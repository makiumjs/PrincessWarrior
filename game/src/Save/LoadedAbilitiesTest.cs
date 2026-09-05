using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Save;

/// TEST-SCENE ONLY, two-phase. Run once with --seed to grant an ability and
/// let it persist; run again with no argument to boot fresh and check the
/// player actually has it.
///
/// A single-process test cannot catch this class of bug: the failure only
/// appears across a restart, which is exactly the path a player takes and the
/// one nothing else here covers.
public partial class LoadedAbilitiesTest : Node
{
    private int _f;
    private bool _seeding;

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--seed") _seeding = true;
    }

    public override void _Process(double delta)
    {
        _f++;
        if (_seeding)
        {
            if (_f == 10)
            {
                EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash);
                GD.Print("[LOADED] seeded Dash into the save");
            }
            if (_f == 30) GetTree().Quit();
            return;
        }

        if (_f != 30) return;

        var saved = SaveManager.Instance?.Current?.UnlockedAbilities ?? AbilityFlags.None;
        var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
        var live = player == null ? AbilityFlags.None : (AbilityFlags)(int)player.Get("UnlockedAbilities");

        GD.Print($"[LOADED] save says {saved}, player has {live}");
        GD.Print(saved.HasFlag(AbilityFlags.Dash) && live.HasFlag(AbilityFlags.Dash)
            ? "[LOADED] RESULT: PASS (a saved ability is restored on boot)"
            : "[LOADED] RESULT: FAIL (the save records the ability but the player does not have it)");
        GetTree().Quit();
    }
}
