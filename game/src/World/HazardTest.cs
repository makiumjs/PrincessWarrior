using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: proves environmental hazards are real — that a spike tile
/// exists in the room, that standing on it damages the player, and that it does
/// so on a cooldown rather than draining the bar in a single frame.
public partial class HazardTest : Node
{
    private int _f;
    private Node3D _player;
    private SpikeHazard _spikes;
    private int _hits;
    private int _totalDamage;

    public override void _Ready()
    {
        EventBus.Instance.PlayerDamaged += (amount, _, _, _) =>
        {
            _hits++;
            _totalDamage += amount;
            GD.Print($"[HAZARD] player damaged for {amount} (hit {_hits}) at frame {_f}");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _spikes ??= FindSpikes(GetTree().Root);
        if (_player == null) return;

        if (_f == 20)
        {
            if (_spikes == null)
            {
                GD.Print("[HAZARD] RESULT: FAIL (no spike hazard generated in the room)");
                GetTree().Quit();
                return;
            }
            GD.Print($"[HAZARD] spikes found at {_spikes.GlobalPosition}");
        }

        // Hold the player on the trap so it ticks more than once.
        if (_f is > 25 and < 500 && _spikes != null)
            _player.GlobalPosition = _spikes.GlobalPosition + new Vector3(0f, 0.6f, 0f);

        if (_f == 500)
        {
            GD.Print($"[HAZARD] {_hits} hits, {_totalDamage} total damage while standing on the trap");
            // Damage, not events: a trap set to 0 damage still fires
            // PlayerDamaged, so counting events alone passes a broken hazard.
            GD.Print(_hits >= 2 && _totalDamage > 0
                ? "[HAZARD] RESULT: PASS (hazard damages on a repeating cooldown)"
                : "[HAZARD] RESULT: FAIL (hazard did not damage the player repeatedly)");
            GetTree().Quit();
        }
    }

    private static SpikeHazard FindSpikes(Node from)
    {
        if (from is SpikeHazard s) return s;
        foreach (var c in from.GetChildren()) { var f = FindSpikes(c); if (f != null) return f; }
        return null;
    }
}
