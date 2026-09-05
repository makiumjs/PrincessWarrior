using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Save;

/// <summary>
/// TEMPORARY verification harness for the Save subsystem — not part of the
/// public interface. Attached to scenes/SaveTestScene.tscn only; never
/// referenced by Main.tscn or any other subsystem. Drives EventBus signals
/// that SaveManager listens to, then checks the resulting on-disk file and
/// a fresh reload round-trip. Prints SAVE_TEST_RESULT: PASS|FAIL and exits.
/// </summary>
public partial class SaveTestRunner : Node
{
    public override void _Ready()
    {
        CallDeferred(nameof(RunTest));
    }

    private void RunTest()
    {
        bool ok = true;

        // Start from a clean slate so the test is deterministic.
        if (FileAccess.FileExists(SaveManager.SavePath))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SaveManager.SavePath));
        }
        // Re-point SaveManager's in-memory state at a fresh SaveData, as if this were a new game.
        var fresh = SaveManager.Instance.LoadSave();
        ok &= Check("fresh save has no checkpoint", fresh.LastCheckpointId == "");
        ok &= Check("fresh save has no abilities", fresh.UnlockedAbilities == AbilityFlags.None);

        // Force SaveManager.Current to the fresh instance via the same path _Ready() uses,
        // by re-invoking the private reload through the public LoadSave() + reflection-free trick:
        // simplest is to just drive events — SaveManager.Current was already set at boot by its
        // own _Ready(), and since no save file existed yet at boot, Current already equals a fresh SaveData.

        GD.Print($"SAVE_TEST: initial Current.LastCheckpointId='{SaveManager.Instance.Current.LastCheckpointId}' UnlockedAbilities={SaveManager.Instance.Current.UnlockedAbilities}");

        // 1. Trigger a checkpoint.
        EventBus.Instance.EmitCheckpointReached("shrine_01");
        ok &= Check("Current.LastCheckpointId updated", SaveManager.Instance.Current.LastCheckpointId == "shrine_01");
        ok &= Check("file exists on disk after checkpoint", FileAccess.FileExists(SaveManager.SavePath));

        // 2. Trigger two ability unlocks and confirm OR-ing.
        EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.DoubleJump);
        EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash);
        var expectedFlags = AbilityFlags.DoubleJump | AbilityFlags.Dash;
        ok &= Check("Current.UnlockedAbilities OR'd correctly", SaveManager.Instance.Current.UnlockedAbilities == expectedFlags);

        // 3. Reload from disk into a separate instance and confirm round-trip.
        var reloaded = SaveManager.Instance.LoadSave();
        ok &= Check("reloaded.LastCheckpointId round-trips", reloaded.LastCheckpointId == "shrine_01");
        ok &= Check("reloaded.UnlockedAbilities round-trips", reloaded.UnlockedAbilities == expectedFlags);
        ok &= Check("reloaded is a distinct instance from Current", reloaded != SaveManager.Instance.Current);

        // 4. Report the resolved absolute path for external (shell-side) verification.
        GD.Print($"SAVE_TEST: absolute save path = {ProjectSettings.GlobalizePath(SaveManager.SavePath)}");

        GD.Print(ok ? "SAVE_TEST_RESULT: PASS" : "SAVE_TEST_RESULT: FAIL");
        GetTree().Quit(ok ? 0 : 1);
    }

    private static bool Check(string label, bool condition)
    {
        GD.Print($"SAVE_TEST_CHECK: {(condition ? "OK  " : "FAIL")} - {label}");
        return condition;
    }
}
