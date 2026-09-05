using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: falling out of the level must kill and respawn.
///
/// Found by mutation testing: disabling FallDeathY entirely left every gate
/// check green, because the loop test forces death with direct damage and never
/// exercises the fall path. Removing that plane silently restores the original
/// bug — a missed jump falls forever with no death, no respawn, and no way to
/// continue.
public partial class FallDeathTest : Node
{
    private int _f;
    private Node3D _player;
    private bool _died;
    private float _lowestY = 999f;

    public override void _Ready() =>
        EventBus.Instance.PlayerDied += () => { _died = true; GD.Print($"[FALL] died at frame {_f}, y={_lowestY:F1}"); };

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        // Drop the player into empty space, well clear of any platform.
        if (_f == 20)
            _player.GlobalPosition = new Vector3(-40f, 5f, 0f);

        if (_f > 20 && !_died)
            _lowestY = Mathf.Min(_lowestY, _player.GlobalPosition.Y);

        if (_f == 400)
        {
            var p = _player.GlobalPosition;
            var hp = (int)_player.Get("CurrentHealth");
            GD.Print($"[FALL] died={_died} lowest y reached={_lowestY:F1} now at {p} hp={hp}");
            GD.Print(_died && p.Y > -20f && hp > 0
                ? "[FALL] RESULT: PASS (falling out of the level kills, and the run resumes)"
                : "[FALL] RESULT: FAIL (fell without dying, or did not recover)");
            GetTree().Quit();
        }
    }
}
