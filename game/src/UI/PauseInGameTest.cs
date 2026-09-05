using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.UI;

/// TEST-SCENE ONLY: the pause menu is IN the game, and its buttons work.
///
/// A PauseMenu with Resume and Quit had existed for a long time and appeared
/// only in a UI test scene -- it was never added to Main.tscn. So in the actual
/// game the pause key froze the world and showed nothing, with no way out but
/// pressing it again. That is this project's recurring failure: a thing that
/// exists, is correct, and is not connected to anything.
///
/// Checked here on the REAL Main.tscn for that reason. A check that instances
/// the menu itself would have passed throughout.
public partial class PauseInGameTest : Node
{
    private int _f;
    private Control _menu;
    private DungeonRoomBuilder _room;
    private bool _sawMenu;
    private bool _resumedCleanly;
    private int _roomAfterRestart = -1;

    /// The test must keep running while the tree is paused, for the same reason
    /// the menu does: a paused tree stops _Process on everything that has not
    /// opted out, so a check that pauses the game and then waits for its own
    /// next frame waits forever. It hung silently rather than failing.
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _f++;
        _menu ??= FindFirst<PauseMenu>(GetTree().Root);
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_menu == null || _room == null)
        {
            if (_f > 60) { GD.Print("[PAUSEUI] RESULT: FAIL (no pause menu in the game scene)"); GetTree().Quit(); }
            return;
        }

        // Move to a later room so a restart has something to undo.
        if (_f == 30) _room.RebuildAs(3);

        // A synthesised EVENT, not Input.ActionPress. ActionPress sets the
        // action's state but produces no InputEvent, so handlers written against
        // _UnhandledInput -- which is how a menu must read a key, since polling
        // would fire every frame the key is held -- never see it. The player
        // controller polls, which is why one works under ActionPress and the
        // other does not.
        if (_f == 90) PressPause();

        if (_f == 110)
        {
            _sawMenu = _menu.Visible && GetTree().Paused;
            GD.Print($"[PAUSEUI] after pressing pause: menu visible={_menu.Visible} tree paused={GetTree().Paused}");
            _menu.GetNode<Button>("%ResumeButton").EmitSignal(BaseButton.SignalName.Pressed);
        }

        if (_f == 130)
        {
            _resumedCleanly = !_menu.Visible && !GetTree().Paused;
            GD.Print($"[PAUSEUI] after Resume: menu visible={_menu.Visible} tree paused={GetTree().Paused}");
            PressPause();
        }

        if (_f == 150)
            _menu.GetNode<Button>("%RestartButton").EmitSignal(BaseButton.SignalName.Pressed);

        if (_f == 260)
        {
            _roomAfterRestart = _room.RoomIndex;
            var save = Save.SaveManager.Instance?.Current;
            GD.Print($"[PAUSEUI] after Restart: room={_roomAfterRestart} paused={GetTree().Paused} " +
                     $"savedRooms={save?.RoomsCleared}");

            bool ok = _sawMenu                       // it is in the game and it shows
                   && _resumedCleanly                // Resume both hides it and unpauses
                   && _roomAfterRestart == 0         // Restart really starts over
                   && !GetTree().Paused;             // and does not leave a frozen tree

            GD.Print(ok
                ? "[PAUSEUI] RESULT: PASS (the pause menu is in the game, resumes, and restarts the run)"
                : "[PAUSEUI] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private static void PressPause()
    {
        var ev = new InputEventAction { Action = "pause", Pressed = true };
        Input.ParseInputEvent(ev);
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
