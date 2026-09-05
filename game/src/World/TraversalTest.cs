using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: drives the player rightward, jumping whenever it is about
/// to run out of floor, and reports how far through the generated room it got.
/// Proves the micro-chunk sizing is actually beatable rather than merely
/// arithmetically derived.
public partial class TraversalTest : Node
{
    private CharacterBody3D _player;
    private float _maxX, _startX;
    private int _frame;
    private float _lowestY = 999f;

    public override void _Ready() => SetProcess(true);

    public override void _Process(double delta)
    {
        _frame++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as CharacterBody3D;
        if (_player == null) return;

        if (_frame == 1) { _startX = _player.GlobalPosition.X; Input.ActionPress("move_right"); }

        float x = _player.GlobalPosition.X;
        float y = _player.GlobalPosition.Y;
        _maxX = Mathf.Max(_maxX, x);
        _lowestY = Mathf.Min(_lowestY, y);

        // Jump whenever airborne-and-falling or standing at an edge; a crude
        // bot, deliberately not frame-perfect, so clearing the room proves the
        // gaps have real margin.
        if (_frame % 22 == 0) { Input.ActionPress("jump"); }
        if (_frame % 22 == 3) { Input.ActionRelease("jump"); }
        if (_frame % 67 == 0) { Input.ActionPress("dash"); }
        if (_frame % 67 == 3) { Input.ActionRelease("dash"); }

        if (_frame == 900)
        {
            GD.Print($"[TRAVERSE] startX={_startX:F1} maxX={_maxX:F1} advanced={_maxX - _startX:F1}m lowestY={_lowestY:F1}");
            GD.Print(_lowestY > -5f ? "[TRAVERSE] did not fall out of the world" : "[TRAVERSE] FELL OUT OF WORLD");
            GetTree().Quit();
        }
    }
}
