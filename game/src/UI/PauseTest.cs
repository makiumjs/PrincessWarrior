using Godot;

namespace LostCrownlike.UI;

/// TEST-SCENE ONLY: pauses mid-run and checks the world actually stops, then
/// unpauses and checks it resumes. A pause menu that draws but does not pause
/// looks correct in a screenshot and is broken in play.
public partial class PauseTest : Node
{
    private int _f;
    private CharacterBody3D _player;
    private float _xAtPause, _xAfterPause, _xAfterResume;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as CharacterBody3D;
        if (_player == null) return;

        if (_f == 10) Input.ActionPress("move_right");

        if (_f == 60)
        {
            _xAtPause = _player.GlobalPosition.X;
            GetTree().Paused = true;
            GD.Print($"[PAUSE] paused at x={_xAtPause:F2}");
        }

        if (_f == 120)
        {
            _xAfterPause = _player.GlobalPosition.X;
            GetTree().Paused = false;
            GD.Print($"[PAUSE] 60 frames later x={_xAfterPause:F2}; resuming");
        }

        if (_f == 180)
        {
            _xAfterResume = _player.GlobalPosition.X;
            float driftWhilePaused = Mathf.Abs(_xAfterPause - _xAtPause);
            float movedAfterResume = _xAfterResume - _xAfterPause;
            GD.Print($"[PAUSE] after resume x={_xAfterResume:F2} " +
                     $"(drift while paused {driftWhilePaused:F3}, moved after resume {movedAfterResume:F2})");
            GD.Print(driftWhilePaused < 0.05f && movedAfterResume > 1f
                ? "[PAUSE] RESULT: PASS (world stops while paused and resumes after)"
                : "[PAUSE] RESULT: FAIL");
            GetTree().Quit();
        }
    }
}
