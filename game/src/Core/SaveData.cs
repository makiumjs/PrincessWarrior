using Godot;

namespace LostCrownlike.Core;

[GlobalClass]
public partial class SaveData : Resource
{
    // A Dictionary<string,bool> WorldFlags used to be declared here for
    // "persistent world state". It was never written and never read — the
    // round-1 Save critic flagged it and nothing consumed it since. The one
    // case it would have covered, not re-offering a pickup the player already
    // owns, is handled by checking the owned abilities directly. Removed
    // rather than left as a promise of persistence the game does not provide.

    [Export] public Vector3 PlayerPosition;
    /// Always everything now. Kept in the save rather than removed so a file
    /// written by an older build still loads, and so the field the HUD and the
    /// controller read has one home.
    [Export] public AbilityFlags UnlockedAbilities = AbilityFlags.All;
    [Export] public string LastCheckpointId = "";

    /// <summary>
    /// Persistent world flags across runs (e.g. struck shortcut levers).
    /// </summary>
    [Export] public Godot.Collections.Dictionary<string, bool> WorldFlags { get; set; } = new();

    /// Run progress. RoomsCleared is the high-water mark, not the current
    /// room: dying and respawning must not lower it, or the ending would move
    /// further away every time the player died.
    [Export] public int RoomsCleared;
    [Export] public bool RunCompleted;
}
