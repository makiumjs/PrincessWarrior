using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: the invulnerability window during a dash.
///
/// Found by mutation testing: zeroing DashIFrameDuration broke no check at all.
/// Dashing through an attack is a defensive option, and losing it silently
/// changes how every fight is played.
///
/// Damage is applied directly mid-dash rather than staged through an enemy, so
/// the result depends only on the invulnerability window and not on whether an
/// AI happened to swing at the right moment.
public partial class DashIFrameTest : Node
{
    private int _f;
    private PlayerController _player;
    private bool _damagedDuringDash;
    private bool _damagedAfterWindow;
    private bool _dashed;
    private float _sinceDash = -1f;
    private bool _triedDuring;

    public override void _Ready()
    {
        EventBus.Instance.Dashed += () => { _dashed = true; _sinceDash = 0f; GD.Print($"[IFRAME] dash started at frame {_f}"); };
        EventBus.Instance.PlayerDamaged += (amount, _, _, _) =>
        {
            if (_sinceDash >= 0f && _sinceDash < 0.5f) _damagedDuringDash = true;
            else _damagedAfterWindow = true;
            GD.Print($"[IFRAME] took {amount} at {_sinceDash:F3}s after the dash");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_player == null) return;

        if (_f == 10) EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash);
        if (_f == 30) { Input.ActionPress("move_right"); Input.ActionPress("dash"); }
        if (_f == 34) Input.ActionRelease("dash");

        if (_sinceDash >= 0f) _sinceDash += (float)delta;

        // Fixed instant, NOT a fraction of the value under test. Deriving the
        // moment from DashIFrameDuration meant that zeroing it made the window
        // `> 0 && < 0` — never true — so the hit never landed and the test
        // passed on a mechanic that no longer existed.
        const float HitAt = 0.05f;   // comfortably inside a healthy 0.15s window
        if (_dashed && !_triedDuring && _sinceDash >= HitAt && _player is IDamageable during)
        {
            _triedDuring = true;
            during.TakeDamage(new DamageInfo { Amount = 7, SourcePosition = _player.GlobalPosition + Vector3.Left });
        }

        if (_dashed && _sinceDash > 2.0f && !_damagedAfterWindow && _player is IDamageable after)
            after.TakeDamage(new DamageInfo { Amount = 7, SourcePosition = _player.GlobalPosition + Vector3.Left });

        if (_f == 400)
        {
            GD.Print($"[IFRAME] dashed={_dashed} damagedDuringWindow={_damagedDuringDash} damagedAfter={_damagedAfterWindow}");
            // Both halves matter: taking no damage at all would also pass a
            // player who is simply never hit.
            GD.Print(_dashed && !_damagedDuringDash && _damagedAfterWindow
                ? "[IFRAME] RESULT: PASS (immune during the dash, vulnerable again after)"
                : "[IFRAME] RESULT: FAIL");
            GetTree().Quit();
        }
    }
}
