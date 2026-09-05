using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.UI;

/// TEST-SCENE ONLY: the options screen is reachable, rebinding works, and it
/// survives a restart.
///
/// Reachability is asserted first and on purpose. Every menu this project has
/// written was correct before it was reachable, and the pause menu spent
/// months existing only inside a test scene. So this presses the button on the
/// real Title.tscn rather than instancing OptionsMenu on its own.
///
/// The rest is about the rule that a key belongs to one action. Rebinding onto
/// an occupied key is REFUSED rather than swapped: a swap silently moves a
/// binding the player was not editing, and this project has already shipped
/// two actions on one button once.
public partial class OptionsTest : Node
{
    private int _f;
    private TitleScreen _title;
    private OptionsMenu _options;

    private bool _hiddenAtStart;
    private bool _openedFromTitle;
    private Key _defaultJump = Key.None;
    private bool _reboundFree;
    private bool _refusedOccupied;
    private Key _afterRefusal = Key.None;
    private bool _survivedReload;
    private bool _resetRestored;
    private bool _resetSurvivedReload;
    private bool _closedCleanly;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Start from a known state: a leftover file from a previous run would
        // make "the default is Space" depend on what someone did last time.
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://settings.cfg"));
        InputSettings.ResetToDefaults();
    }

    public override void _Process(double delta)
    {
        _f++;
        _title ??= FindFirst<TitleScreen>(GetTree().Root);
        _options ??= FindFirst<OptionsMenu>(GetTree().Root);

        if (_title == null || _options == null)
        {
            if (_f > 60) Done(false, "no title screen, or no options menu in it");
            return;
        }

        if (_f == 10)
        {
            _hiddenAtStart = !_options.Visible;
            _defaultJump = InputSettings.KeyOf("jump");
            _title.GetNode<Button>("%OptionsButton").EmitSignal(BaseButton.SignalName.Pressed);
            return;
        }

        if (_f == 14)
        {
            _openedFromTitle = _options.Visible;
            GD.Print($"[OPT] hidden at start={_hiddenAtStart} opened from title={_openedFromTitle} " +
                     $"default jump={InputSettings.KeyName(_defaultJump)}");

            // Y is bound to nothing in the project file.
            _reboundFree = InputSettings.Rebind("jump", Key.Y);
            GD.Print($"[OPT] jump -> Y: accepted={_reboundFree} now={InputSettings.KeyName(InputSettings.KeyOf("jump"))}");

            // J is the light attack.
            _refusedOccupied = !InputSettings.Rebind("jump", Key.J);
            _afterRefusal = InputSettings.KeyOf("jump");
            GD.Print($"[OPT] jump -> J (taken by {InputSettings.ActionUsing(Key.J, "jump")}): " +
                     $"refused={_refusedOccupied} still={InputSettings.KeyName(_afterRefusal)}");
            return;
        }

        // A fresh session reads the file back. This is the half that a check
        // asserting only InputMap would miss: the rebind can be applied and
        // never written, and nobody notices until the next launch.
        if (_f == 20)
        {
            InputSettings.Reload();
            _survivedReload = InputSettings.KeyOf("jump") == Key.Y;
            GD.Print($"[OPT] after a reload from disk: jump={InputSettings.KeyName(InputSettings.KeyOf("jump"))}");
            return;
        }

        if (_f == 26)
        {
            InputSettings.ResetToDefaults();
            _resetRestored = InputSettings.KeyOf("jump") == _defaultJump;
            InputSettings.Reload();
            _resetSurvivedReload = InputSettings.KeyOf("jump") == _defaultJump;
            GD.Print($"[OPT] after reset: jump={InputSettings.KeyName(InputSettings.KeyOf("jump"))} " +
                     $"and still that after a reload={_resetSurvivedReload}");

            // Found by walking rather than by path: the rows are built in code
            // and nest under containers, so a fixed path here would be a second
            // description of a layout that is allowed to change.
            FindNamed<Button>(_options, "BackButton")?.EmitSignal(BaseButton.SignalName.Pressed);
            return;
        }

        if (_f == 32)
        {
            _closedCleanly = !_options.Visible;
            GD.Print($"[OPT] Back: options visible={_options.Visible}");

            bool ok = _hiddenAtStart
                   && _openedFromTitle          // it is reachable from the game's front door
                   && _defaultJump == Key.Space
                   && _reboundFree              // a free key is taken
                   && _refusedOccupied          // an occupied one is refused
                   && _afterRefusal == Key.Y    // and refusing changed nothing
                   && _survivedReload           // the change is on disk, not just in memory
                   && _resetRestored
                   && _resetSurvivedReload      // and reset really cleared the file
                   && _closedCleanly;

            Done(ok, ok ? "rebinding works, refuses a taken key, and survives a restart" : "");
        }
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[OPT] RESULT: PASS ({why})"
                    : $"[OPT] RESULT: FAIL{(why.Length > 0 ? " (" + why + ")" : "")}");
        GetTree().Quit();
    }

    private static T FindNamed<T>(Node from, string name) where T : Node
    {
        if (from is T t && from.Name == name) return t;
        foreach (var c in from.GetChildren()) { var f = FindNamed<T>(c, name); if (f != null) return f; }
        return null;
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
