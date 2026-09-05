using System.Collections.Generic;
using Godot;
using LostCrownlike.World;

namespace LostCrownlike.UI;

/// TEST-SCENE ONLY: the game BOOTS into the title, and Play reaches the game.
///
/// The scene path is read from ProjectSettings, not typed here. That is the
/// whole point of the check: a title screen that exists and looks right but is
/// not what run/main_scene points at is invisible to the player, and this
/// project has shipped that exact shape three times (the ability nobody could
/// be granted, the checkpoint tally nobody read, the pause menu that was only
/// in a test scene). Hardcoding "res://scenes/ui/Title.tscn" here would pass
/// while the game still booted straight into room 0.
///
/// It also survives its own scene change: the script node reparents itself to
/// the root, because ChangeSceneToFile frees the current scene and would take
/// the test with it.
public partial class TitleBootTest : Node
{
    private int _f;
    private string _bootScene = "";
    private bool _titleBooted;
    private bool _worldAbsentAtTitle;
    private int _controlLines;
    private bool _playStartedTheGame;
    private bool _titleGone;
    private string _bindingConflict = "?";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        CallDeferred(nameof(DetachFromScene));
    }

    private void DetachFromScene()
    {
        var root = GetTree().Root;
        var parent = GetParent();
        if (parent == root) return;
        parent.RemoveChild(this);
        root.AddChild(this);
    }

    public override void _Process(double delta)
    {
        _f++;

        if (_f == 5)
        {
            _bootScene = (string)ProjectSettings.GetSetting("application/run/main_scene", "");
            GD.Print($"[TITLE] run/main_scene = {_bootScene}");
            if (string.IsNullOrEmpty(_bootScene))
            {
                GD.Print("[TITLE] RESULT: FAIL (no main scene configured)");
                GetTree().Quit();
                return;
            }
            GetTree().ChangeSceneToFile(_bootScene);
        }

        if (_f == 40)
        {
            var title = FindFirst<TitleScreen>(GetTree().Root);
            _titleBooted = title != null;
            // The world must not be running behind the title. A title drawn
            // over a live room would still show a menu, and would still be
            // wrong: enemies would be walking, and the first room would
            // already have burned its build time.
            _worldAbsentAtTitle = FindFirst<DungeonRoomBuilder>(GetTree().Root) == null;

            if (!_titleBooted)
            {
                GD.Print($"[TITLE] booted into {_bootScene}: no TitleScreen in it");
                GD.Print("[TITLE] RESULT: FAIL (the game does not boot into a title screen)");
                GetTree().Quit();
                return;
            }

            // The list is the first place the bindings are ever read back, and
            // reading them back is what found parry and attack_light both
            // firing on mouse-left. Asserted here so it cannot return.
            _bindingConflict = FindBindingConflict();
            if (_bindingConflict.Length > 0)
                GD.Print($"[TITLE] BINDING CONFLICT: {_bindingConflict}");

            string controls = title.GetNode<Label>("%ControlsLabel").Text;
            _controlLines = controls.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Length;
            GD.Print($"[TITLE] world present behind title={!_worldAbsentAtTitle} controlLines={_controlLines}");
            GD.Print($"[TITLE] controls:\n{controls}");

            title.GetNode<Button>("%PlayButton").EmitSignal(BaseButton.SignalName.Pressed);
        }

        if (_f == 180)
        {
            var room = FindFirst<DungeonRoomBuilder>(GetTree().Root);
            _playStartedTheGame = room != null && room.RoomIndex == 0 && room.GetChildCount() > 0;
            _titleGone = FindFirst<TitleScreen>(GetTree().Root) == null;
            GD.Print($"[TITLE] after Play: room={(room == null ? "none" : room.RoomIndex.ToString())} " +
                     $"pieces={(room == null ? 0 : room.GetChildCount())} titleGone={_titleGone} " +
                     $"paused={GetTree().Paused}");

            bool ok = _titleBooted            // the configured boot scene IS the title
                   && _worldAbsentAtTitle     // and the game is not running behind it
                   && _controlLines >= 5      // the controls list was really built from InputMap
                   && _bindingConflict.Length == 0  // no key drives two actions at once
                   && _playStartedTheGame     // Play lands in a built room 0
                   && _titleGone              // and the title is gone, not just covered
                   && !GetTree().Paused;

            GD.Print(ok
                ? "[TITLE] RESULT: PASS (the game boots into the title and Play starts the run)"
                : "[TITLE] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    /// Two actions on one key means every press fires both. It is invisible
    /// in code -- each handler reads its own action and looks correct -- and
    /// only shows up when the bindings are listed side by side.
    private static string FindBindingConflict()
    {
        var actions = new List<string>();
        foreach (var a in InputMap.GetActions())
        {
            string name = a.ToString();
            if (!name.StartsWith("ui_")) actions.Add(name);
        }

        for (int i = 0; i < actions.Count; i++)
            for (int j = i + 1; j < actions.Count; j++)
                foreach (var e1 in InputMap.ActionGetEvents(actions[i]))
                    foreach (var e2 in InputMap.ActionGetEvents(actions[j]))
                        if (e1.IsMatch(e2, true))
                            return $"{actions[i]} and {actions[j]} share {e2.AsText()}";
        return "";
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
