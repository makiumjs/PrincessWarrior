using Godot;

namespace LostCrownlike.Core;

/// <summary>
/// Key rebinding, persisted to user://settings.cfg.
///
/// Not an autoload. The bindings have to be in place before anything reads
/// them, and there are two doors into the game -- the title screen and, in
/// tests, Main.tscn directly -- so both call EnsureApplied() and the second
/// call is free. An autoload would work too and would be one more global that
/// has to be present for the game to be correct; this cannot be forgotten,
/// because forgetting it means the caller does not exist.
///
/// Overrides replace the KEYBOARD event of an action and leave its mouse
/// events alone: the mouse bindings are part of the combat layout, and a
/// rebinding screen that silently dropped them would be a regression the
/// player never asked for.
/// </summary>
public static class InputSettings
{
    private const string SettingsPath = "user://settings.cfg";
    private const string BindSection = "bindings";
    private const string AudioSection = "audio";

    private static bool _applied;

    /// Every action the options screen may touch, in display order. Actions
    /// outside this list keep whatever the project file gives them.
    public static readonly (string Action, string Label)[] Rebindable =
    {
        ("move_left", "Move left"),
        ("move_right", "Move right"),
        ("jump", "Jump"),
        ("dash", "Dash"),
        ("attack_light", "Light attack"),
        ("attack_heavy", "Heavy attack"),
        ("parry", "Parry"),
        ("pause", "Pause"),
    };

    private const string GraphicsSection = "graphics";

    public static float MasterVolume { get; private set; } = 1f;
    public static bool GlowEnabled { get; private set; } = true;

    public static void EnsureApplied()
    {
        if (_applied) return;
        _applied = true;
        Reload();
    }

    /// Project defaults first, then the overrides on top. Doing it in that
    /// order means a saved file with one stale action cannot leave the rest of
    /// the map half-written from a previous session.
    public static void Reload()
    {
        InputMap.LoadFromProjectSettings();

        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok)
        {
            ApplyVolume(1f);
            GlowEnabled = true;
            ApplyGlow(true);
            return;
        }

        foreach (var (action, _) in Rebindable)
        {
            if (!cfg.HasSectionKey(BindSection, action)) continue;
            var key = (Key)(int)cfg.GetValue(BindSection, action, (int)Key.None);
            if (key != Key.None) SetKey(action, key, persist: false);
        }

        ApplyVolume((float)cfg.GetValue(AudioSection, "master", 1f));
        GlowEnabled = (bool)cfg.GetValue(GraphicsSection, "glow", true);
        ApplyGlow(GlowEnabled);
    }

    /// The physical keycode currently bound to an action, or None.
    public static Key KeyOf(string action)
    {
        if (!InputMap.HasAction(action)) return Key.None;
        foreach (var ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventKey k)
                return k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        return Key.None;
    }

    /// The action already using this key, or "". This is the rule that was
    /// missing when parry and attack_light both sat on mouse-left: every press
    /// fired both, each handler looked correct in isolation, and nothing but
    /// listing them side by side showed it. Enforced here so it cannot be
    /// created by hand either.
    public static string ActionUsing(Key key, string ignoring)
    {
        foreach (var a in InputMap.GetActions())
        {
            string name = a.ToString();
            if (name == ignoring || name.StartsWith("ui_")) continue;
            foreach (var ev in InputMap.ActionGetEvents(name))
                if (ev is InputEventKey k
                    && (k.PhysicalKeycode == key || (k.PhysicalKeycode == Key.None && k.Keycode == key)))
                    return name;
        }
        return "";
    }

    /// Replaces the action's keyboard binding. Returns false, changing
    /// nothing, if another action already holds the key.
    public static bool Rebind(string action, Key key)
    {
        if (!InputMap.HasAction(action) || key == Key.None) return false;
        if (ActionUsing(key, action).Length > 0) return false;

        SetKey(action, key, persist: true);
        return true;
    }

    private static void SetKey(string action, Key key, bool persist)
    {
        // Only the keyboard events go. Mouse bindings survive, because the
        // player rebinding "heavy attack" to a different letter did not ask to
        // lose the right mouse button as well.
        foreach (var ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventKey)
                InputMap.ActionEraseEvent(action, ev);

        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });

        if (!persist) return;
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);              // missing file is fine: we write it
        cfg.SetValue(BindSection, action, (int)key);
        cfg.Save(SettingsPath);
    }

    public static void SetMasterVolume(float linear)
    {
        ApplyVolume(linear);
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue(AudioSection, "master", linear);
        cfg.Save(SettingsPath);
    }

    private static void ApplyVolume(float linear)
    {
        MasterVolume = Mathf.Clamp(linear, 0f, 1f);
        // Silence is -80dB, not the -inf that a linear 0 converts to; the bus
        // is muted outright instead, because setting an infinite negative
        // volume is an engine error rather than a quiet game.
        if (MasterVolume <= 0.001f)
        {
            AudioServer.SetBusMute(0, true);
            return;
        }
        AudioServer.SetBusMute(0, false);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(MasterVolume));
    }

    public static void SetGlow(bool enabled, bool persist = true)
    {
        GlowEnabled = enabled;
        ApplyGlow(enabled);
        if (persist)
        {
            var cfg = new ConfigFile();
            cfg.Load(SettingsPath);
            cfg.SetValue(GraphicsSection, "glow", enabled);
            cfg.Save(SettingsPath);
        }
    }

    public static void ApplyGlow(bool enabled)
    {
        if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
        {
            var we = FindWorldEnv(tree.Root);
            if (we?.Environment != null)
                we.Environment.GlowEnabled = enabled;
        }
    }

    private static WorldEnvironment FindWorldEnv(Node from)
    {
        if (from == null) return null;
        if (from is WorldEnvironment we) return we;
        foreach (var c in from.GetChildren())
        {
            var found = FindWorldEnv(c);
            if (found != null) return found;
        }
        return null;
    }

    public static void ResetToDefaults()
    {
        InputMap.LoadFromProjectSettings();
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) == Error.Ok)
        {
            if (cfg.HasSection(BindSection)) cfg.EraseSection(BindSection);
            if (cfg.HasSection(GraphicsSection)) cfg.EraseSection(GraphicsSection);
            cfg.Save(SettingsPath);
        }
        GlowEnabled = true;
        ApplyGlow(true);
    }

    /// Human-readable name of a key, translated through the active keyboard
    /// layout when there is one. Headless has none and answers with an engine
    /// error per call, so it is asked only when a real display server is up.
    public static string KeyName(Key key)
    {
        if (key == Key.None) return "—";
        if (DisplayServer.GetName() == "headless") return OS.GetKeycodeString(key);
        var mapped = DisplayServer.KeyboardGetKeycodeFromPhysical(key);
        return OS.GetKeycodeString(mapped == Key.None ? key : mapped);
    }
}
