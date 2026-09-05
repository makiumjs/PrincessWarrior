using Godot;
using LostCrownlike.AI;
using LostCrownlike.Core;
using LostCrownlike.PlayerCamera;
using LostCrownlike.World;

namespace LostCrownlike.Combat;

/// TEST-SCENE ONLY: blows leave a mark, absorbed blows leave a DIFFERENT one,
/// and none of them accumulate.
///
/// Checked without a renderer on purpose. What matters is not that pixels
/// appear -- the windowed check already covers whether the drawn layer throws
/// -- but that the spawn actually happens on every path that claims it does,
/// and that a burst frees itself. This project has shipped a visual that was
/// correct and never instantiated, and separately an entity that outlived its
/// room; both are visible here as counts.
///
/// The colours are asserted to DIFFER rather than to be specific values. A
/// check pinned to (1, 0.34, 0.26) would fail on a palette tweak and pass on
/// the one change that matters -- the two cases collapsing into one colour,
/// which is exactly the state the boss's armour was in before this existed.
public partial class ImpactFxTest : Node
{
    private int _f;
    private DungeonRoomBuilder _room;
    private SideScrollCamera _camera;

    private Color _hitTint;
    private Color _absorbedTint;
    private bool _sawHitBurst;
    private bool _sawAbsorbedBurst;
    private bool _sawParryBurst;
    private float _shakeAfterParry;
    private int _burstsAtPeak;
    private int _burstsAfterLife = -1;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        _camera ??= FindFirst<SideScrollCamera>(GetTree().Root);
        if (_room == null || _camera == null) return;

        // A normal enemy, in a normal room.
        if (_f == 20)
        {
            var enemy = FindFirst<EnemyController>(_room);
            if (enemy == null) { Done(false, "no enemy to hit in room 0"); return; }
            Hit(enemy, 20);
            return;
        }

        if (_f == 22)
        {
            var burst = FindFirst<ImpactBurst>(_room);
            _sawHitBurst = burst != null;
            if (burst != null) _hitTint = burst.Tint;
            GD.Print($"[FX] hit on a normal enemy: burst={_sawHitBurst} tint={_hitTint}");
            return;
        }

        // The boss room, where the same blow is absorbed.
        if (_f == 60) { _room.RebuildAs(_room.RunLength - 1); return; }

        if (_f == 80)
        {
            var boss = FindFirst<Warden>(_room);
            if (boss == null) { Done(false, "no boss in the last room"); return; }
            Hit(boss, 55);
            return;
        }

        if (_f == 82)
        {
            var burst = FindFirst<ImpactBurst>(_room);
            _sawAbsorbedBurst = burst != null;
            if (burst != null) _absorbedTint = burst.Tint;
            GD.Print($"[FX] 55 absorbed by armour: burst={_sawAbsorbedBurst} tint={_absorbedTint}");
            return;
        }

        // A perfect parry: its own burst, and a camera that moves. Driven with
        // the real input, not by announcing Parried on the bus -- announcing it
        // would prove the camera listens and prove nothing about the player's
        // own burst, which is spawned on the path that DECIDES a parry landed.
        //
        // Input.ActionPress is right here and wrong for a menu: the player
        // controller POLLS the action's state, and ActionPress sets exactly
        // that. The pause menu reads _UnhandledInput and never saw it.
        if (_f == 116) { Input.ActionPress("parry"); return; }

        if (_f == 119)
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            if (player == null) { Done(false, "no player"); return; }
            // Inside the 0.14s perfect window: pressed three frames ago.
            Hit(player, 30, from: player.GlobalPosition + new Vector3(1.5f, 0f, 0f));
            Input.ActionRelease("parry");
            return;
        }

        if (_f == 120)
        {
            _burstsAtPeak = Count<ImpactBurst>(_room) + Count<ImpactBurst>(GetTree().Root);
            _sawParryBurst = _burstsAtPeak > 0;
            _shakeAfterParry = _camera.ShakeMagnitude;
            GD.Print($"[FX] perfect parry: bursts={_burstsAtPeak} cameraShake={_shakeAfterParry:F3}");
            return;
        }

        // Every burst lives 0.32s at most. 60 frames at 60fps is a full second.
        if (_f == 190)
        {
            _burstsAfterLife = Count<ImpactBurst>(_room);
            GD.Print($"[FX] one second later: bursts still alive={_burstsAfterLife}");

            bool ok = _sawHitBurst
                   && _sawAbsorbedBurst
                   && _hitTint != _absorbedTint      // absorbed does not look like landed
                   && _sawParryBurst
                   && _shakeAfterParry > 0.1f        // the parry is felt, not only heard
                   && _burstsAfterLife == 0;         // and nothing is left behind

            Done(ok, ok ? "impacts are visible, absorbed ones look different, and none linger" : "");
        }
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[FX] RESULT: PASS ({why})"
                    : $"[FX] RESULT: FAIL{(why.Length > 0 ? " (" + why + ")" : "")}");
        GetTree().Quit();
    }

    private void Hit(Node3D target, int amount, Vector3? from = null)
    {
        var src = from ?? target.GlobalPosition + new Vector3(-1.5f, 0f, 0f);
        if (target is IDamageable d)
            d.TakeDamage(new DamageInfo
            {
                Amount = amount,
                SourcePosition = src,
                Knockback = new Vector3(2f, 1f, 0f),
                IsCritical = false,
            });
    }

    private static int Count<T>(Node from) where T : Node
    {
        int n = from is T ? 1 : 0;
        foreach (var c in from.GetChildren()) n += Count<T>(c);
        return n;
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
