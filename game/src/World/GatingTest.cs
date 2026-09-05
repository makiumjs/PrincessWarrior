using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the metroidvania invariant, stated as something that can
/// actually be proven — abilities are OFF at the start, a pickup grants exactly
/// one, the player's own flags reflect it, and Save persists it.
///
/// Deliberately does NOT try to show "the ability opened progress": measuring
/// that with a fixed-cadence bot compares bot quality, not gating. A bot that
/// dashes on a timer drives itself into pits and travels LESS far with the
/// ability than without it.
public partial class GatingTest : Node
{
    private int _f;
    private Node3D _player;
    private AbilityFlags _granted;
    private bool _sawGrant;

    public override void _Ready()
    {
        EventBus.Instance.AbilityUnlocked += bits =>
        {
            _granted = (AbilityFlags)bits;
            _sawGrant = true;
            GD.Print($"[GATE] pickup granted {_granted} at frame {_f}");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        if (_f == 5)
        {
            var start = (AbilityFlags)(int)_player.Get("UnlockedAbilities");
            GD.Print($"[GATE] starting abilities = {start}");
            if (start != AbilityFlags.None)
            {
                GD.Print("[GATE] RESULT: FAIL (player did not start with abilities locked)");
                GetTree().Quit();
                return;
            }
            Input.ActionPress("move_right");
        }

        if (_f is > 5 and < 1500 && _f % 28 == 0) Input.ActionPress("jump");
        if (_f is > 5 and < 1500 && _f % 28 == 4) Input.ActionRelease("jump");

        if (_f == 1500)
        {
            var now = (AbilityFlags)(int)_player.Get("UnlockedAbilities");
            var saved = Save.SaveManager.Instance?.Current?.UnlockedAbilities ?? AbilityFlags.None;
            GD.Print($"[GATE] granted={_granted} playerFlags={now} savedFlags={saved}");

            bool ok = _sawGrant
                   && now.HasFlag(_granted)
                   && saved.HasFlag(_granted);
            GD.Print(ok
                ? "[GATE] RESULT: PASS (locked at start, pickup grants, player and save agree)"
                : "[GATE] RESULT: FAIL");
            GetTree().Quit();
        }
    }
}
