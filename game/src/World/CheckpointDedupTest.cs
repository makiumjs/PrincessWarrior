using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: standing in, or respawning into, the same checkpoint must
/// not re-announce it.
///
/// Found by mutation testing: removing the de-duplication broke no check. It
/// matters twice over — the HUD prompt sticks on screen forever, and Save
/// rewrites savegame.tres to disk on every single re-entry, so a player dying
/// repeatedly at one shrine hammers the disk.
public partial class CheckpointDedupTest : Node
{
    private int _f;
    private Node3D _player;
    private int _announcements;
    private string _id = "";
    private Vector3 _spawn;

    public override void _Ready() =>
        EventBus.Instance.CheckpointReached += id =>
        {
            _announcements++;
            _id = id;
            GD.Print($"[DEDUP] checkpoint '{id}' announced ({_announcements}) at frame {_f}");
        };

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        // Physically LEAVE and RE-ENTER the trigger. Respawning does not do
        // it: a teleport that lands inside an Area3D the body never left fires
        // no BodyEntered at all, so a de-duplication bug is invisible that way.
        if (_f == 40) _spawn = _player.GlobalPosition;
        if (_f is 60 or 120 or 180 or 240)
            _player.GlobalPosition = _spawn + new Vector3(14f, 0f, 0f);   // out
        if (_f is 90 or 150 or 210 or 270)
            _player.GlobalPosition = _spawn;                              // back in

        if (_f == 320)
        {
            GD.Print($"[DEDUP] '{_id}' announced {_announcements} time(s) across 4 exits and re-entries");
            if (_announcements == 0)
                GD.Print("[DEDUP] RESULT: FAIL (no checkpoint was ever reached - nothing was tested)");
            else
                GD.Print(_announcements <= 2
                    ? "[DEDUP] RESULT: PASS (re-entering the same checkpoint does not re-announce it)"
                    : $"[DEDUP] RESULT: FAIL (announced {_announcements} times - every re-entry re-fires it)");
            GetTree().Quit();
        }
    }
}
