using System.Collections.Generic;
using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.UI;

/// <summary>
/// Rebinding and volume. Reachable from the title screen and from the pause
/// menu, and it is the same node in both — instanced twice, not written twice.
///
/// The rows are built in code, one per entry in InputSettings.Rebindable.
/// A scene with eight hand-authored rows would be a second copy of that list,
/// and this project has already been bitten by a second copy of the bindings:
/// the title's controls text was going to be typed out until it was clear it
/// would keep naming the old key.
/// </summary>
public partial class OptionsMenu : Control
{
    [Signal] public delegate void ClosedEventHandler();

    private readonly Dictionary<string, Button> _rows = new();
    private Label _message;
    private string _listeningFor = "";

    public override void _Ready()
    {
        // The pause menu opens this with the tree paused; without Always it
        // would appear and then never process an input.
        ProcessMode = ProcessModeEnum.Always;
        InputSettings.EnsureApplied();
        Build();
        Refresh();
    }

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var bg = new ColorRect { Color = new Color(0.03f, 0.035f, 0.05f, 0.92f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        centre.AddChild(col);

        var title = new Label { Text = "OPTIONS", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", new Color(0.93f, 0.79f, 0.55f));
        col.AddChild(title);

        var volRow = new HBoxContainer();
        volRow.AddThemeConstantOverride("separation", 12);
        col.AddChild(volRow);
        volRow.AddChild(new Label { Text = "Volume", CustomMinimumSize = new Vector2(190, 0) });
        var slider = new HSlider
        {
            Name = "VolumeSlider",
            MinValue = 0, MaxValue = 1, Step = 0.05,
            Value = InputSettings.MasterVolume,
            CustomMinimumSize = new Vector2(190, 20),
        };
        slider.ValueChanged += v => InputSettings.SetMasterVolume((float)v);
        volRow.AddChild(slider);

        col.AddChild(new HSeparator());

        foreach (var (action, label) in InputSettings.Rebindable)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            col.AddChild(row);

            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(190, 0) });

            var button = new Button { Name = $"Bind_{action}", CustomMinimumSize = new Vector2(190, 26) };
            string captured = action;
            button.Pressed += () => StartListening(captured);
            row.AddChild(button);
            _rows[action] = button;
        }

        _message = new Label
        {
            Name = "Message",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 22),
        };
        _message.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.4f));
        col.AddChild(_message);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        col.AddChild(buttons);

        var reset = new Button { Name = "ResetButton", Text = "Reset to defaults", CustomMinimumSize = new Vector2(190, 30) };
        reset.Pressed += OnResetPressed;
        buttons.AddChild(reset);

        var back = new Button { Name = "BackButton", Text = "Back", CustomMinimumSize = new Vector2(190, 30) };
        back.Pressed += OnBackPressed;
        buttons.AddChild(back);
    }

    /// The next key press goes to this action instead of the game.
    public void StartListening(string action)
    {
        _listeningFor = action;
        _message.Text = "press a key, or Escape to cancel";
        Refresh();
    }

    public override void _Input(InputEvent @event)
    {
        if (_listeningFor.Length == 0) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        GetViewport().SetInputAsHandled();
        var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;

        if (code == Key.Escape && _listeningFor != "pause")
        {
            _listeningFor = "";
            _message.Text = "";
            Refresh();
            return;
        }

        string clash = InputSettings.ActionUsing(code, _listeningFor);
        if (clash.Length > 0)
        {
            // Refused rather than swapped. A swap looks helpful and silently
            // moves a binding the player was not editing; a refusal tells them
            // what is in the way.
            _message.Text = $"{InputSettings.KeyName(code)} is already {clash}";
            _listeningFor = "";
            Refresh();
            return;
        }

        InputSettings.Rebind(_listeningFor, code);
        _listeningFor = "";
        _message.Text = "";
        Refresh();
    }

    private void OnResetPressed()
    {
        InputSettings.ResetToDefaults();
        _listeningFor = "";
        _message.Text = "";
        Refresh();
    }

    private void OnBackPressed()
    {
        _listeningFor = "";
        Visible = false;
        EmitSignal(SignalName.Closed);
    }

    public void Refresh()
    {
        foreach (var (action, button) in _rows)
            button.Text = action == _listeningFor
                ? "..."
                : InputSettings.KeyName(InputSettings.KeyOf(action));
    }
}
