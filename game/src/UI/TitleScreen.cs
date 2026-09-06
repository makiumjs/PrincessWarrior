using System.Collections.Generic;
using Godot;

namespace LostCrownlike.UI;

/// <summary>
/// The screen the game boots into. Pure UI: it knows one scene path and the
/// InputMap, and nothing about the player, the room builder, or the save.
///
/// The controls list is built from InputMap at runtime rather than typed into
/// the scene. A hand-written list is a second copy of the bindings that nobody
/// updates -- a typed label keeps telling the player the old key long after
/// the binding moved.
/// </summary>
public partial class TitleScreen : Control
{
    /// The scene Play loads. Exported so the boot path is one editable value
    /// and not a string buried in a method.
    [Export] public string GameScene = "res://scenes/Main.tscn";

    /// Action name -> the words the player reads. Order is the display order.
    private static readonly (string Action, string Label)[] Shown =
    {
        ("move_left", "Move"),
        ("jump", "Jump / double jump"),
        ("dash", "Dash"),
        ("attack_light", "Light attack"),
        ("attack_heavy", "Heavy attack (hold to charge)"),
        ("parry", "Parry"),
        ("pause", "Pause"),
    };

    private Button _playButton;
    private Button _continueButton;
    private Button _optionsButton;
    private Button _quitButton;
    private OptionsMenu _options;

    public override void _Ready()
    {
        // Saved bindings before anything reads them, including the controls
        // list below. Without this the title would confidently print the
        // project defaults over a rebound keyboard.
        Core.InputSettings.EnsureApplied();

        _playButton = GetNode<Button>("%PlayButton");
        _continueButton = GetNode<Button>("%ContinueButton");
        _optionsButton = GetNode<Button>("%OptionsButton");
        _quitButton = GetNode<Button>("%QuitButton");
        _options = GetNode<OptionsMenu>("%OptionsMenu");

        _playButton.Pressed += OnPlayPressed;
        _continueButton.Pressed += OnContinuePressed;
        _optionsButton.Pressed += OnOptionsPressed;

        // Offered only when there is something to continue. A Continue that
        // starts room 0 with nothing unlocked is a second New run wearing a
        // different label, and the player finds that out by pressing it.
        _continueButton.Visible = ResumeRoom() > 0;
        _quitButton.Pressed += OnQuitPressed;
        _options.Closed += OnOptionsClosed;

        GetNode<Label>("%ControlsLabel").Text = BuildControlsText();

        // Without this the keyboard has nothing to act on and Play can only be
        // reached with the mouse.
        _playButton.GrabFocus();
    }

    public override void _ExitTree()
    {
        if (_playButton != null) _playButton.Pressed -= OnPlayPressed;
        if (_continueButton != null) _continueButton.Pressed -= OnContinuePressed;
        if (_optionsButton != null) _optionsButton.Pressed -= OnOptionsPressed;
        if (_quitButton != null) _quitButton.Pressed -= OnQuitPressed;
        if (_options != null) _options.Closed -= OnOptionsClosed;
    }

    /// A NEW run, and that means new: the save is wiped before the game scene
    /// loads. Without this, "New run" started at room 0 with every ability
    /// already unlocked, because SaveManager restores them on boot -- which is
    /// not a new run, it is the old one with the level reset.
    private void OnPlayPressed()
    {
        LostCrownlike.Save.SaveManager.Instance?.ResetSave();
        StartGameScene();
    }

    /// Shared by both entry points. The tree can still be paused if the player
    /// reached the title from a paused game; starting into a paused tree looks
    /// like a frozen game, which is exactly the bug the pause menu had.
    private void StartGameScene()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(GameScene);
    }

    /// The room a Continue would open at: how far the run got, unless the run
    /// is finished, in which case there is nothing to go back to.
    private static int ResumeRoom()
    {
        var save = LostCrownlike.Save.SaveManager.Instance?.Current;
        if (save == null || save.RunCompleted) return 0;
        return save.RoomsCleared;
    }

    /// Picks up where the save left off, with the abilities it recorded --
    /// SaveManager already re-announces those on boot, so nothing extra is
    /// needed to restore them.
    private void OnContinuePressed()
    {
        var manager = LostCrownlike.Save.SaveManager.Instance;
        if (manager != null) manager.ResumeFromRoom = ResumeRoom();
        StartGameScene();
    }

    private void OnOptionsPressed()
    {
        _options.Refresh();
        _options.Visible = true;
    }

    /// The controls list is rebuilt on the way out, not only at startup: the
    /// player may have just changed the very keys it names, and a list that
    /// still shows the old ones is worse than no list.
    private void OnOptionsClosed()
    {
        GetNode<Label>("%ControlsLabel").Text = BuildControlsText();
        _playButton.GrabFocus();
    }

    private void OnQuitPressed() => GetTree().Quit();

    private static string BuildControlsText()
    {
        var lines = new List<string>();
        foreach (var (action, label) in Shown)
        {
            if (!InputMap.HasAction(action)) continue;
            string keys = DescribeAction(action);
            if (keys.Length > 0) lines.Add($"{keys}   {label}");
        }
        return string.Join("\n", lines);
    }

    private static string DescribeAction(string action)
    {
        var parts = new List<string>();
        foreach (var ev in InputMap.ActionGetEvents(action))
        {
            switch (ev)
            {
                case InputEventKey k:
                    parts.Add(KeyName(k));
                    break;
                case InputEventMouseButton m:
                    parts.Add(m.ButtonIndex switch
                    {
                        MouseButton.Left => "Mouse L",
                        MouseButton.Right => "Mouse R",
                        MouseButton.Middle => "Mouse M",
                        _ => $"Mouse {(int)m.ButtonIndex}",
                    });
                    break;
            }
        }
        return string.Join(" / ", parts);
    }

    /// The bindings are stored as PHYSICAL keycodes, so a stored value names a
    /// position on the keyboard, not a letter. Translating it through the
    /// active layout is what makes the label right on a non-US keyboard --
    /// otherwise an AZERTY player is told "A" for the key marked Q. Headless
    /// has no layout and returns None, so the untranslated name is the
    /// fallback rather than an empty label.
    private static string KeyName(InputEventKey k)
    {
        if (k.PhysicalKeycode == Key.None) return OS.GetKeycodeString(k.Keycode);
        if (!CanMapLayout) return OS.GetKeycodeString(k.PhysicalKeycode);
        var mapped = DisplayServer.KeyboardGetKeycodeFromPhysical(k.PhysicalKeycode);
        return OS.GetKeycodeString(mapped == Key.None ? k.PhysicalKeycode : mapped);
    }

    /// The headless display server answers the layout query with an engine
    /// ERROR, once per binding. Eight of them per boot is not cosmetic here:
    /// the gate fails any check whose run prints an engine error, so an
    /// unguarded call would have made the title screen unshippable in CI while
    /// looking perfectly fine on screen.
    private static readonly bool CanMapLayout = DisplayServer.GetName() != "headless";
}
