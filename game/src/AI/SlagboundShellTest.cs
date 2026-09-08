using Godot;
using LostCrownlike.Core;
using LostCrownlike.PlayerCamera;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the shell punishes the answer every other enemy rewards,
/// and only a perfect parry opens it.
///
/// Three claims, and the middle one is the one that makes this type worth
/// having. Absorbing damage on its own is the Warden's trick; hitting BACK at
/// the player who swung is what turns "keep hitting it" from slow into costly.
///
/// The blows are dealt directly rather than swung by the player, for the reason
/// every check in this suite teleports: a test that had to land ten real sword
/// swings would be measuring the combo timing, and would go red the day someone
/// retuned the recovery frames.
public partial class SlagboundShellTest : Node
{
    private const int Blows = 10;
    private const int BlowDamage = 25;

    private int _f;
    private PlayerController _player;
    private Slagbound _slag;

    private int _enemyBefore = -1, _enemyAfterSealed = -1, _enemyAfterOpen = -1;
    private int _playerBefore = -1, _playerAfterSealed = -1;
    private bool _sealedBefore, _sealedAfterParry;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_player == null) { if (_f > 180) Done(false, "no player"); return; }

        if (_f == 30)
        {
            var scene = GD.Load<PackedScene>("res://scenes/enemies/Slagbound.tscn");
            if (scene == null) { Done(false, "no Slagbound scene"); return; }
            _slag = scene.Instantiate<Slagbound>();
            GetTree().Root.AddChild(_slag);
            // Inside BurnReach: the shell burns whoever touched it, and the
            // distance is what stands in for "whoever".
            _slag.GlobalPosition = _player.GlobalPosition + new Vector3(1.5f, 0f, 0f);
            _slag.SetPatrolPoints(_slag.GlobalPosition, _slag.GlobalPosition);
            return;
        }

        if (_slag == null) return;

        if (_f == 60)
        {
            _sealedBefore = _slag.IsSealed;
            _enemyBefore = _slag.CurrentHealth;
            _playerBefore = _player.CurrentHealth;

            for (int i = 0; i < Blows; i++) Strike(BlowDamage);

            _enemyAfterSealed = _slag.CurrentHealth;
            _playerAfterSealed = _player.CurrentHealth;
            return;
        }

        // The shell opens only on a PERFECT parry, so a blocked one is fired
        // first: without it the check would pass on an implementation that
        // opened for any parry at all.
        if (_f == 70) { EventBus.Instance?.EmitParried(false, _slag.GlobalPosition); return; }
        if (_f == 75) { _sealedAfterParry = _slag.IsSealed; return; }

        if (_f == 80)
        {
            EventBus.Instance?.EmitParried(true, _slag.GlobalPosition);
            return;
        }

        if (_f == 85)
        {
            int before = _slag.CurrentHealth;
            Strike(BlowDamage);
            _enemyAfterOpen = before - _slag.CurrentHealth;
            return;
        }

        if (_f == 95)
        {
            int chipped = _enemyBefore - _enemyAfterSealed;
            int burned = _playerBefore - _playerAfterSealed;

            GD.Print($"[SHELL] sealed at the start: {_sealedBefore}");
            GD.Print($"[SHELL] {Blows} blows of {BlowDamage} on the shell removed {chipped} health (cap {Blows * _slag.ShellChipDamage})");
            GD.Print($"[SHELL] the same {Blows} blows cost the player {burned} health (burn {_slag.ShellBurnDamage}, gated to one per {_slag.BurnCooldownSeconds:0.0}s)");
            GD.Print($"[SHELL] a BLOCKED parry left it sealed: {_sealedAfterParry}");
            GD.Print($"[SHELL] one blow through the open shell landed {_enemyAfterOpen} of {BlowDamage}");

            bool absorbed = _sealedBefore && chipped > 0 && chipped <= Blows * _slag.ShellChipDamage;
            bool burnsBack = burned > 0;
            bool blockedDoesNotOpen = _sealedAfterParry;
            bool perfectOpens = _enemyAfterOpen == BlowDamage;

            bool ok = absorbed && burnsBack && blockedDoesNotOpen && perfectOpens;
            Done(ok, ok ? "the shell chips, burns the hand that struck it, ignores a block and opens to a perfect parry" : "");
        }
    }

    private void Strike(int amount) => _slag.TakeDamage(new DamageInfo
    {
        Amount = amount,
        SourcePosition = _player.GlobalPosition,
        Knockback = Vector3.Zero,
        IsCritical = false,
        Telegraph = AttackTelegraphType.StandardWhite,
    });

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[SHELL] RESULT: PASS ({why})" : "[SHELL] RESULT: FAIL");
        GetTree().Quit();
    }
}
