using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Save;

/// <summary>
/// Save subsystem autoload — see ARCHITECTURE.md "### Save".
/// Owns the in-memory SaveData instance and persists it to user:// whenever
/// EventBus reports a checkpoint or an ability unlock. Autoloaded as
/// "/root/SaveManager" (see project.godot), after EventBus.
/// </summary>
public partial class SaveManager : Node
{
    public static SaveManager Instance { get; private set; }

    public const string SavePath = "user://savegame.tres";

    /// <summary>The save currently held in memory (loaded at startup, or a fresh SaveData if none existed).</summary>
    public SaveData Current { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        Current = LoadSave();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.CheckpointReached += OnCheckpointReached;
            EventBus.Instance.PlayerDied += OnPlayerDied;
            EventBus.Instance.AbilityUnlocked += OnAbilityUnlocked;
            EventBus.Instance.LevelTransitionRequested += OnLevelTransitionRequested;
            EventBus.Instance.RunCompleted += OnRunCompleted;
        }
        else
        {
            GD.PushError("SaveManager: EventBus.Instance is null in _Ready — autoload order in project.godot must list EventBus before SaveManager.");
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.CheckpointReached -= OnCheckpointReached;
            EventBus.Instance.PlayerDied -= OnPlayerDied;
            EventBus.Instance.AbilityUnlocked -= OnAbilityUnlocked;
            EventBus.Instance.LevelTransitionRequested -= OnLevelTransitionRequested;
            EventBus.Instance.RunCompleted -= OnRunCompleted;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Loads the save from user://savegame.tres if it exists, otherwise returns a fresh SaveData.
    /// Public so other subsystems (or a test harness) can force a reload from disk.
    /// </summary>
    public SaveData LoadSave()
    {
        if (ResourceLoader.Exists(SavePath))
        {
            var loaded = ResourceLoader.Load<SaveData>(SavePath, cacheMode: ResourceLoader.CacheMode.Ignore);
            if (loaded != null)
            {
                return loaded;
            }

            GD.PushWarning($"SaveManager: {SavePath} exists but failed to load as SaveData — starting a fresh save.");
        }

        return new SaveData();
    }

    private bool _abilitiesReplayed;

    /// <summary>
    /// Re-announces the abilities recorded in the save, once, as soon as a
    /// player exists.
    ///
    /// Without this a saved run silently loses its progression: SaveManager
    /// accumulated UnlockedAbilities correctly and wrote them to disk, but
    /// nothing ever pushed them back — PlayerController only ever gains
    /// abilities from live AbilityUnlocked events, and its own starting value
    /// comes from the scene (None). Load a game and the file says you have
    /// Dash while the character does not.
    ///
    /// Replaying the existing event rather than assigning the field directly
    /// means the HUD reveals its icons and World refreshes its ability gates
    /// too, with no new coupling and no new signal.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_abilitiesReplayed) return;
        if (Current == null || Current.UnlockedAbilities == AbilityFlags.None)
        {
            _abilitiesReplayed = true;
            SetProcess(false);
            return;
        }
        if (GetPlayerNode() == null) return;   // scene still building

        _abilitiesReplayed = true;
        SetProcess(false);

        foreach (AbilityFlags flag in System.Enum.GetValues<AbilityFlags>())
        {
            if (flag == AbilityFlags.None) continue;
            if (Current.UnlockedAbilities.HasFlag(flag))
                EventBus.Instance?.EmitAbilityUnlocked(flag);
        }
        GD.Print($"SaveManager: restored {Current.UnlockedAbilities} from the save");
    }

    /// Which room the next build should open at, set by the title screen and
    /// consumed once by the room builder.
    ///
    /// It lives here rather than in a static or on the bus because this is the
    /// autoload that already owns run progression, and because it has to
    /// survive a scene change: the title screen calls ChangeSceneToFile, so a
    /// signal emitted before the game exists has nobody listening, and a field
    /// on the builder does not exist yet either.
    public int ResumeFromRoom { get; set; }

    /// Reads the intent and clears it, so a later rebuild in the same session
    /// starts where it is told to rather than jumping back to the resume point.
    public int TakeResumeRoom()
    {
        int room = ResumeFromRoom;
        ResumeFromRoom = 0;
        return room;
    }

    /// Records how far the run has got. Only generated rooms are counted, and
    /// only upward: RoomsCleared is a high-water mark, so dying back to an
    /// earlier checkpoint does not push the ending further away.
    private void OnLevelTransitionRequested(string scenePath, string spawnPointId)
    {
        if (Current == null) return;
        if (scenePath == null || !scenePath.StartsWith("generated:")) return;
        if (!int.TryParse(scenePath.Substring("generated:".Length), out int index)) return;
        if (index <= Current.RoomsCleared) return;
        Current.RoomsCleared = index;
        Persist();
    }

    private void OnRunCompleted(int roomsCleared)
    {
        if (Current == null) return;
        Current.RoomsCleared = Mathf.Max(Current.RoomsCleared, roomsCleared);
        Current.RunCompleted = true;
        Persist();
        GD.Print($"SaveManager: run completed, {Current.RoomsCleared} rooms cleared");
    }

    private void OnCheckpointReached(string checkpointId)
    {
        Current.LastCheckpointId = checkpointId;

        // Record WHERE the player was, not just which checkpoint id fired.
        // Round-1 critics on both Save and World found the same gap from two
        // ends: SaveData.PlayerPosition had no producer anywhere in the
        // codebase, and LevelManager's checkpoint bookkeeping had no consumer,
        // so "reload" restored abilities and a checkpoint id but never put the
        // player back anywhere.
        var player = GetPlayerNode();
        if (player != null)
            Current.PlayerPosition = player.GlobalPosition;

        Persist();
    }

    /// <summary>
    /// Closes the checkpoint loop: on death, put the player back at the last
    /// recorded checkpoint position. Uses only existing EventBus events and the
    /// "player" group convention, so Save gains no compile-time dependency on
    /// PlayerCamera or World.
    /// </summary>
    private void OnPlayerDied()
    {
        // Reviving must happen even when there is nothing to restore TO.
        // Previously this returned silently if no checkpoint had been banked
        // (PlayerPosition still Vector3.Zero), so Respawn() was never called
        // and the player stayed dead at 0 health with no way to continue — a
        // softlock on any death before the first checkpoint.
        if (RestorePlayerPosition())
            return;

        var player = GetPlayerNode();
        if (player == null)
            return;

        if (player.HasMethod("ReturnToSpawn"))
            player.Call("ReturnToSpawn");
        if (player.HasMethod("Respawn"))
            player.Call("Respawn");
    }

    public bool RestorePlayerPosition()
    {
        var player = GetPlayerNode();
        if (player == null || Current.PlayerPosition == Vector3.Zero)
            return false;
        player.GlobalPosition = Current.PlayerPosition;

        // Duck-typed on purpose: Save must not take a compile-time dependency
        // on PlayerCamera. Without this the player is moved back but stays
        // dead, so the run cannot resume.
        if (player.HasMethod("Respawn"))
            player.Call("Respawn");

        return true;
    }

    private Node3D GetPlayerNode() => GetTree()?.GetFirstNodeInGroup("player") as Node3D;

    private void OnAbilityUnlocked(int abilityBits)
    {
        var ability = (AbilityFlags)abilityBits;
        Current.UnlockedAbilities |= ability;
        Persist();
    }

    public bool GetWorldFlag(string flag)
    {
        if (Current?.WorldFlags == null || string.IsNullOrEmpty(flag))
            return false;
        return Current.WorldFlags.TryGetValue(flag, out bool val) && val;
    }

    public void SetWorldFlag(string flag, bool value = true)
    {
        if (Current == null || string.IsNullOrEmpty(flag))
            return;
        Current.WorldFlags[flag] = value;
        Persist();
    }

    /// Starts a fresh save. Without this a completed run left RunCompleted true
    /// on disk, so the next session began already finished.
    public void ResetSave()
    {
        Current = new SaveData();
        Persist();
        GD.Print("SaveManager: save reset for a new run.");
    }

    private void Persist()
    {
        var err = ResourceSaver.Save(Current, SavePath);
        if (err != Error.Ok)
        {
            GD.PushError($"SaveManager: ResourceSaver.Save failed with {err} while writing {SavePath}");
        }
    }
}
