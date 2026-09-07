using Godot;
using LostCrownlike.Core;
using LostCrownlike.Save;

namespace LostCrownlike.UI;

/// TEST-SCENE ONLY: the title has two doors and they lead to different places.
///
/// A run is ten rooms and twelve minutes now, so it cannot only be played in
/// one sitting -- but two entry points are only worth having if they actually
/// differ, and each fails in a way that looks fine from the outside:
///
///   Continue that starts at room 0 is a second New run with another label.
///   New run that keeps the save is the old run with the level reset -- which
///   is what it did, because SaveManager re-announces the saved abilities on
///   boot, so "start over" handed the player everything at room 0.
///
/// Driven through the buttons rather than by calling the methods: a button
/// wired to nothing is this project's oldest failure.
///
/// Two mechanics this check had to be built around, both learned the hard way
/// elsewhere in the suite:
///
///   It reparents itself to the root, because pressing either button calls
///   ChangeSceneToFile, which frees the current scene -- the first version was
///   taken with it and simply stopped, printing two of its five lines.
///
///   It reads the intent in the SAME frame as the press. Pressed is emitted
///   synchronously so the handler has already run, while the scene change is
///   deferred to the end of the frame -- one frame later the room builder has
///   consumed the value and reset it.
public partial class ContinueTest : Node
{
    private const int SavedRoom = 4;

    private int _f;
    private TitleScreen _title;

    private bool _hiddenWithNoSave;
    private bool _shownWithSave;
    private int _resumeAfterContinue = -1;
    private int _resumeAfterNewRun = -1;
    private AbilityFlags _abilitiesAfterNewRun = AbilityFlags.None;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SaveManager.Instance?.ResetSave();
        CallDeferred(nameof(DetachFromScene));
    }

    private void DetachFromScene()
    {
        var root = GetTree().Root;
        var parent = GetParent();
        if (parent == root) return;
        parent.RemoveChild(this);
        root.AddChild(this);
    }

    public override void _Process(double delta)
    {
        _f++;
        var save = SaveManager.Instance?.Current;
        if (save == null) { if (_f > 60) Done(false, "no save manager"); return; }

        // A fresh title AFTER the reset. The scene's own title node ran its
        // _Ready before this one did, so it had already read whatever the last
        // session left on disk -- the first version of this check asserted
        // "Continue is hidden" against a save that still had four rooms in it,
        // and blamed the game.
        if (_f == 10)
        {
            SaveManager.Instance.ResetSave();
            _title = FindFirst<TitleScreen>(GetTree().Root);
            SwapInFreshTitle();
            return;
        }

        if (_f == 14)
        {
            _hiddenWithNoSave = !Button("ContinueButton").Visible;
            GD.Print($"[CONT] with an empty save: Continue offered = {!_hiddenWithNoSave}");
            return;
        }

        // Progress, the way a real run records it, then a fresh title -- which
        // is what relaunching the game does.
        if (_f == 18)
        {
            save.RoomsCleared = SavedRoom;
            save.UnlockedAbilities = AbilityFlags.Dash | AbilityFlags.DoubleJump;
            save.RunCompleted = false;
            SwapInFreshTitle();
            return;
        }

        if (_f == 22)
        {
            _shownWithSave = Button("ContinueButton").Visible;
            GD.Print($"[CONT] with {SavedRoom} rooms cleared: Continue offered = {_shownWithSave}");

            Button("ContinueButton").EmitSignal(BaseButton.SignalName.Pressed);
            _resumeAfterContinue = SaveManager.Instance.ResumeFromRoom;
            GD.Print($"[CONT] Continue asked to resume at room {_resumeAfterContinue}");
            return;
        }

        if (_f == 28)
        {
            SaveManager.Instance.ResumeFromRoom = 0;
            save.RoomsCleared = SavedRoom;
            save.UnlockedAbilities = AbilityFlags.Dash | AbilityFlags.DoubleJump;
            SwapInFreshTitle();
            return;
        }

        if (_f == 32)
        {
            Button("PlayButton").EmitSignal(BaseButton.SignalName.Pressed);
            _resumeAfterNewRun = SaveManager.Instance.ResumeFromRoom;
            _abilitiesAfterNewRun = SaveManager.Instance.Current.UnlockedAbilities;
            GD.Print($"[CONT] New run: resume={_resumeAfterNewRun} " +
                     $"abilities={_abilitiesAfterNewRun} rooms={SaveManager.Instance.Current.RoomsCleared}");

            bool ok = _hiddenWithNoSave                          // nothing to continue, nothing offered
                   && _shownWithSave                             // progress, so it is offered
                   && _resumeAfterContinue == SavedRoom          // and it resumes where the run got to
                   && _resumeAfterNewRun == 0                    // New run starts at the beginning
                   // Abilities are no longer what separates the two doors: the
                   // player owns all four from the first frame, so a new run is
                   // new by starting at room 0 with nothing CLEARED, and the
                   // check would be vacuous if it kept asking about crystals.
                   && _abilitiesAfterNewRun == AbilityFlags.All;

            Done(ok, ok ? "Continue resumes the saved room, New run starts over with nothing" : "");
        }
    }

    /// A title added straight to the root, so the scene changes the buttons
    /// trigger cannot take it away mid-check.
    private void SwapInFreshTitle()
    {
        if (_title != null && IsInstanceValid(_title)) _title.QueueFree();
        _title = GD.Load<PackedScene>("res://scenes/ui/Title.tscn").Instantiate<TitleScreen>();
        GetTree().Root.AddChild(_title);
    }

    private Button Button(string name) => FindNamed<Button>(_title, name);

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[CONT] RESULT: PASS ({why})"
                    : $"[CONT] RESULT: FAIL{(why.Length > 0 ? " (" + why + ")" : "")}");
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
