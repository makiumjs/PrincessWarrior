using Godot;
using LostCrownlike.Core;
using LostCrownlike.Save;

namespace LostCrownlike.Save;

/// TEST-SCENE ONLY: verifies checkpoint position capture + respawn round-trip.
public partial class RespawnTestRunner : Node
{
    private CharacterBody3D _player;
    private int _frame;

    public override void _Ready()
    {
        _player = new CharacterBody3D { Name = "FakePlayer" };
        _player.AddToGroup("player");
        AddChild(_player);
        _player.GlobalPosition = new Vector3(12.5f, 3.25f, 0f);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame == 5)
        {
            EventBus.Instance.EmitCheckpointReached("shrine_test");
            GD.Print($"[RT] checkpoint fired at pos={_player.GlobalPosition}");
        }
        else if (_frame == 10)
        {
            GD.Print($"[RT] saved PlayerPosition={SaveManager.Instance.Current.PlayerPosition}");
            _player.GlobalPosition = new Vector3(-99f, -99f, 0f);
            GD.Print($"[RT] moved player away to {_player.GlobalPosition}");
            EventBus.Instance.EmitPlayerDied();
        }
        else if (_frame == 15)
        {
            var reloaded = SaveManager.Instance.LoadSave();
            bool posOk = _player.GlobalPosition.DistanceTo(new Vector3(12.5f, 3.25f, 0f)) < 0.01f;
            bool diskOk = reloaded.PlayerPosition.DistanceTo(new Vector3(12.5f, 3.25f, 0f)) < 0.01f;
            GD.Print($"[RT] after death pos={_player.GlobalPosition} respawnOk={posOk}");
            GD.Print($"[RT] reloaded-from-disk PlayerPosition={reloaded.PlayerPosition} diskOk={diskOk}");
            GD.Print(posOk && diskOk ? "[RT] RESULT: PASS" : "[RT] RESULT: FAIL");
            GetTree().Quit();
        }
    }
}
