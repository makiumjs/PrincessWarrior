using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// A directed edge in the map graph: from one room to another, optionally
/// gated by an ability. SpawnPointId identifies the Node3D (named
/// "SpawnPoint_&lt;id&gt;") inside the target room's scene that the player
/// should be placed at after traversing this connection.
/// </summary>
[GlobalClass]
public partial class WorldConnection : Resource
{
    [Export] public string FromRoomId = "";
    [Export] public string ToRoomId = "";
    [Export] public string SpawnPointId = "";
    [Export] public AbilityFlags RequiredAbility = AbilityFlags.None;
}
