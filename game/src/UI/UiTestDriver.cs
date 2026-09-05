using System;
using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.UI;

/// <summary>
/// Debug-only driver, scoped to scenes/ui/UiTestScene.tscn. Fires synthetic
/// EventBus events on a timer so the HUD/pause menu can be verified visually
/// without any other subsystem (PlayerCamera, World, Combat, ...) existing.
/// Not part of the shipping UI subsystem contract.
/// </summary>
public partial class UiTestDriver : Node
{
    [Export] public float IntervalSeconds = 2.0f;

    private Timer _timer;
    private int _step;
    private Action[] _steps;

    public override void _Ready()
    {
        _steps = new Action[]
        {
            () => EventBus.Instance.EmitPlayerDamaged(new DamageInfo
            {
                Amount = 15,
                SourcePosition = Vector3.Zero,
                Knockback = Vector3.Zero,
                IsCritical = false,
            }),
            () => EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.DoubleJump),
            () => EventBus.Instance.EmitCheckpointReached("shrine_01"),
            () => EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash),
            () => EventBus.Instance.EmitPlayerDamaged(new DamageInfo
            {
                Amount = 25,
                SourcePosition = Vector3.Zero,
                Knockback = Vector3.Zero,
                IsCritical = true,
            }),
            () => EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.WallJump),
            () => EventBus.Instance.EmitCheckpointReached("shrine_02"),
            () => EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.ChargeAttack),
        };

        _timer = new Timer
        {
            WaitTime = IntervalSeconds,
            OneShot = false,
            Autostart = true,
        };
        AddChild(_timer);
        _timer.Timeout += OnTimerTimeout;
    }

    public override void _ExitTree()
    {
        if (_timer != null) _timer.Timeout -= OnTimerTimeout;
    }

    private void OnTimerTimeout()
    {
        if (EventBus.Instance == null || _steps == null || _steps.Length == 0) return;

        _steps[_step]?.Invoke();
        GD.Print($"[UiTestDriver] fired step {_step}");
        _step = (_step + 1) % _steps.Length;
    }
}
