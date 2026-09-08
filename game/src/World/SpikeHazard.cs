using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// A floor trap that damages whatever stands on it.
///
/// This is what `PhysicsLayers.Hazard` was reserved for: the constant was
/// declared from the start and nothing in the game ever sat on that layer, so
/// the whole category of environmental danger existed only in an enum.
/// </summary>
public partial class SpikeHazard : Area3D
{
    [Export] public int Damage = 15;
    /// Seconds between ticks while standing on spikes
    [Export] public float DamageIntervalSeconds = 0.8f;
    [Export] public float Knockback = 5f;
    /// Distance at which the spikes detect player approach and prepare to spring
    [Export] public float TriggerDistance = 3.8f;

    private enum TrapState
    {
        Retracted,
        Telegraph,
        Extended,
        Retracting,
    }

    private TrapState _state = TrapState.Retracted;
    private float _stateTimer;
    private float _damageCooldown;
    private float _reArmCooldown;
    private float _time;
    private Node3D _spikesMesh;
    private Node3D _player;

    private const float RetractedY = -0.55f;
    private const float ExtendedY = 0f;

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Hazard;
        CollisionMask = PhysicsLayers.Player | PhysicsLayers.Enemy;
        Monitoring = true;

        if (GetChildCount() == 0)
            AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.6f, 0.8f, 3.0f) } });

        var scene = GD.Load<PackedScene>("res://assets/kaykit/dungeon/floor_tile_big_spikes.gltf");
        if (scene != null)
        {
            var visual = scene.Instantiate<Node3D>();
            visual.Position = new Vector3(0f, -0.35f, 0f);
            AddChild(visual);

            _spikesMesh = visual.FindChild("spikes", true, false) as Node3D;
            if (_spikesMesh == null)
            {
                foreach (var child in visual.GetChildren())
                {
                    if (child is Node3D n3d && n3d.Name.ToString().ToLower().Contains("spike"))
                    {
                        _spikesMesh = n3d;
                        break;
                    }
                }
            }

            if (_spikesMesh != null)
                _spikesMesh.Position = new Vector3(0f, RetractedY, 0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;

        switch (_state)
        {
            case TrapState.Retracted:
                if (_reArmCooldown > 0f)
                {
                    _reArmCooldown -= (float)delta;
                    break;
                }

                if (CheckTriggerProximity())
                {
                    TriggerTrap();
                }
                break;

            case TrapState.Telegraph:
                _stateTimer -= (float)delta;
                if (_spikesMesh != null)
                {
                    float shake = Mathf.Sin(_time * 60f) * 0.04f;
                    _spikesMesh.Position = new Vector3(shake, RetractedY + 0.08f, 0f);
                }

                if (_stateTimer <= 0f)
                {
                    ExtendSpikes();
                }
                break;

            case TrapState.Extended:
                _stateTimer -= (float)delta;
                _damageCooldown -= (float)delta;

                if (_spikesMesh != null)
                    _spikesMesh.Position = new Vector3(0f, ExtendedY, 0f);

                if (_damageCooldown <= 0f)
                {
                    ApplyDamage();
                    _damageCooldown = DamageIntervalSeconds;
                }

                if (_stateTimer <= 0f)
                {
                    _state = TrapState.Retracting;
                    _stateTimer = 0.35f;
                }
                break;

            case TrapState.Retracting:
                _stateTimer -= (float)delta;
                if (_spikesMesh != null)
                {
                    float progress = 1f - Mathf.Clamp(_stateTimer / 0.35f, 0f, 1f);
                    float curY = Mathf.Lerp(ExtendedY, RetractedY, progress);
                    _spikesMesh.Position = new Vector3(0f, curY, 0f);
                }

                if (_stateTimer <= 0f)
                {
                    _state = TrapState.Retracted;
                    _reArmCooldown = 0.45f;
                    if (_spikesMesh != null)
                        _spikesMesh.Position = new Vector3(0f, RetractedY, 0f);
                }
                break;
        }
    }

    private bool CheckTriggerProximity()
    {
        if (GetOverlappingBodies().Count > 0)
            return true;

        _player ??= GetTree()?.GetFirstNodeInGroup("player") as Node3D;
        if (_player != null && IsInstanceValid(_player))
        {
            float dx = Mathf.Abs(_player.GlobalPosition.X - GlobalPosition.X);
            float dy = Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y);
            if (dx <= TriggerDistance && dy <= 2.2f)
                return true;
        }

        return false;
    }

    private void TriggerTrap()
    {
        _state = TrapState.Telegraph;
        _stateTimer = 0.16f;
    }

    private void ExtendSpikes()
    {
        _state = TrapState.Extended;
        _stateTimer = 1.1f;
        _damageCooldown = DamageIntervalSeconds;
        if (_spikesMesh != null)
            _spikesMesh.Position = new Vector3(0f, ExtendedY, 0f);

        ApplyDamage();
    }

    private void ApplyDamage()
    {
        foreach (var body in GetOverlappingBodies())
        {
            if (body is not IDamageable damageable) continue;

            float dir = Mathf.Sign(body.GlobalPosition.X - GlobalPosition.X);
            if (dir == 0f) dir = 1f;

            bool isEnemy = body.IsInGroup("enemy");
            int amount = isEnemy ? 50 : Damage;

            damageable.TakeDamage(new DamageInfo
            {
                Amount = amount,
                SourcePosition = GlobalPosition,
                Knockback = new Vector3(dir * Knockback, Knockback * 0.7f, 0f),
                IsCritical = isEnemy,
            });
        }
    }
}
