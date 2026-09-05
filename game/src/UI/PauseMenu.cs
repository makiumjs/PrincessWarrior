using Godot;

namespace LostCrownlike.UI;

/// <summary>
/// Pause menu toggled by the "pause" input action. Runs with ProcessMode.Always
/// (set in PauseMenu.tscn) so it keeps receiving input while the SceneTree is
/// paused. Pure UI — never references PlayerController or other subsystems.
/// </summary>
public partial class PauseMenu : Control
{
    private Button _resumeButton;
    private Button _restartButton;
    private Button _optionsButton;
    private Button _quitButton;
    private OptionsMenu _options;

    public override void _Ready()
    {
        Visible = false;

        _resumeButton = GetNode<Button>("%ResumeButton");
        _restartButton = GetNode<Button>("%RestartButton");
        _optionsButton = GetNode<Button>("%OptionsButton");
        _quitButton = GetNode<Button>("%QuitButton");
        _options = GetNode<OptionsMenu>("%OptionsMenu");

        _resumeButton.Pressed += OnResumePressed;
        _restartButton.Pressed += OnRestartPressed;
        _optionsButton.Pressed += OnOptionsPressed;
        _quitButton.Pressed += OnQuitPressed;
        _options.Closed += OnOptionsClosed;
    }

    public override void _ExitTree()
    {
        if (_resumeButton != null) _resumeButton.Pressed -= OnResumePressed;
        if (_restartButton != null) _restartButton.Pressed -= OnRestartPressed;
        if (_optionsButton != null) _optionsButton.Pressed -= OnOptionsPressed;
        if (_options != null) _options.Closed -= OnOptionsClosed;
        if (_quitButton != null) _quitButton.Pressed -= OnQuitPressed;
    }

    private void OnOptionsPressed()
    {
        _options.Refresh();
        _options.Visible = true;
    }

    private void OnOptionsClosed() => _resumeButton.GrabFocus();

    public override void _UnhandledInput(InputEvent @event)
    {
        // While options is open the pause key belongs to it -- it may be the
        // key being rebound. Toggling the menu underneath would close both and
        // leave the rebind half-done.
        if (_options != null && _options.Visible) return;

        if (@event.IsActionPressed("pause"))
        {
            TogglePause();
            GetViewport().SetInputAsHandled();
        }
    }

    private void TogglePause()
    {
        if (_options != null) _options.Visible = false;
        bool paused = !GetTree().Paused;
        GetTree().Paused = paused;
        Visible = paused;
    }

    private void OnResumePressed()
    {
        GetTree().Paused = false;
        Visible = false;
    }

    /// Starts the run again from room 0. Announced on the bus rather than
    /// wired to the room builder directly: this is UI, and the rule here is
    /// that it never references a gameplay subsystem's type. Unpausing first
    /// matters -- a restart into a paused tree looks like a frozen game.
    private void OnRestartPressed()
    {
        GetTree().Paused = false;
        Visible = false;
        Core.EventBus.Instance?.EmitRestartRequested();
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
