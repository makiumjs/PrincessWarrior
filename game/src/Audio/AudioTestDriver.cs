using System;
using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Audio;

/// <summary>
/// Debug driver for scenes/AudioTestScene.tscn. Fires each EventBus signal
/// AudioManager listens to, one at a time on a timer, so every procedural
/// SFX plays in sequence and can be heard/verified individually. Not part of
/// any other subsystem's scene tree — self-contained test scene only.
/// </summary>
public partial class AudioTestDriver : Node
{
    [Export] public float IntervalSeconds = 1.25f;

    private (string Name, Action Fire)[] _steps;
    private int _step;
    private Timer _timer;

    public override void _Ready()
    {
        _steps = new (string, Action)[]
        {
            ("Jumped", () => EventBus.Instance.EmitJumped()),
            ("DoubleJumped", () => EventBus.Instance.EmitDoubleJumped()),
            ("Dashed", () => EventBus.Instance.EmitDashed()),
            ("WallJumped", () => EventBus.Instance.EmitWallJumped()),
            ("Landed", () => EventBus.Instance.EmitLanded()),
            ("PlayerDamaged", () => EventBus.Instance.EmitPlayerDamaged(new DamageInfo { Amount = 10, IsCritical = false })),
            ("EnemyDamaged", () => EventBus.Instance.EmitEnemyDamaged(null, new DamageInfo { Amount = 5, IsCritical = false })),
        };

        _timer = new Timer
        {
            WaitTime = IntervalSeconds,
            OneShot = false,
            Autostart = true,
        };
        AddChild(_timer);
        _timer.Timeout += OnTimeout;

        GD.Print($"AudioTestDriver: ready, firing {_steps.Length} EventBus SFX events every {IntervalSeconds}s (looping).");
    }

    private void OnTimeout()
    {
        if (EventBus.Instance == null)
        {
            GD.PushError("AudioTestDriver: EventBus.Instance is null, cannot fire test events.");
            return;
        }

        var (name, fire) = _steps[_step];
        GD.Print($"AudioTestDriver: firing '{name}' ({_step + 1}/{_steps.Length}).");
        fire();

        _step = (_step + 1) % _steps.Length;
    }
}
