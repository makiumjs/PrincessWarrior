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
    private Button _quitButton;

    public override void _Ready()
    {
        _playButton = GetNode<Button>("%PlayButton");
        _quitButton = GetNode<Button>("%QuitButton");

        _playButton.Pressed += OnPlayPressed;
        _quitButton.Pressed += OnQuitPressed;

        GetNode<Label>("%ControlsLabel").Text = BuildControlsText();

        // Without this the keyboard has nothing to act on and Play can only be
        // reached with the mouse.
        _playButton.GrabFocus();
    }

    public override void _ExitTree()
    {
        if (_playButton != null) _playButton.Pressed -= OnPlayPressed;
        if (_quitButton != null) _quitButton.Pressed -= OnQuitPressed;
    }

    private void OnPlayPressed()
    {
        // The tree can still be paused if the player reached the title from a
        // paused game; starting into a paused tree looks like a frozen game,
        // which is exactly the bug the pause menu's Restart had.
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(GameScene);
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
