using Godot;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: the camera must track the player.
///
/// Found by mutation testing: freezing the camera entirely broke no check at
/// all — and a camera that did not follow was a real defect here once, caught
/// by a human looking at screenshots rather than by anything automated. If it
/// regressed again nothing would notice until someone played the game.
///
/// Asserts both halves: the camera moves when the player travels, and it stays
/// near the player rather than merely drifting somewhere.
public partial class CameraFollowTest : Node
{
    private int _f;
    private Node3D _player;
    private Camera3D _camera;
    private float _playerStartX, _cameraStartX;
    private float _worstGap;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _camera ??= FindCamera(GetTree().Root);
        if (_player == null || _camera == null) return;

        if (_f == 20)
        {
            _playerStartX = _player.GlobalPosition.X;
            _cameraStartX = _camera.GlobalPosition.X;
            Input.ActionPress("move_right");
        }

        if (_f > 40)
            _worstGap = Mathf.Max(_worstGap, Mathf.Abs(_camera.GlobalPosition.X - _player.GlobalPosition.X));

        if (_f == 300)
        {
            float playerMoved = _player.GlobalPosition.X - _playerStartX;
            float cameraMoved = _camera.GlobalPosition.X - _cameraStartX;
            GD.Print($"[CAMERA] player moved {playerMoved:F1}m, camera moved {cameraMoved:F1}m, worst gap {_worstGap:F1}m");

            // Both halves matter. A camera pinned to the player would pass the
            // first and fail nothing; a camera drifting on its own would pass
            // the second.
            bool travelled = playerMoved > 5f;
            bool followed = cameraMoved > playerMoved * 0.5f;
            bool stayedClose = _worstGap < 12f;

            if (!travelled)
                GD.Print("[CAMERA] RESULT: FAIL (the player never moved - nothing was tested)");
            else if (!followed)
                GD.Print($"[CAMERA] RESULT: FAIL (camera moved {cameraMoved:F1}m while the player moved {playerMoved:F1}m)");
            else if (!stayedClose)
                GD.Print($"[CAMERA] RESULT: FAIL (camera fell {_worstGap:F1}m behind)");
            else
                GD.Print("[CAMERA] RESULT: PASS (camera follows the player and keeps it framed)");
            GetTree().Quit();
        }
    }

    private static Camera3D FindCamera(Node from)
    {
        if (from is Camera3D c) return c;
        foreach (var ch in from.GetChildren()) { var f = FindCamera(ch); if (f != null) return f; }
        return null;
    }
}
