using Godot;

namespace LostCrownlike.World;

/// <summary>
/// One node ("room") in the map graph. Pure data — see WorldGraph.cs.
/// </summary>
[GlobalClass]
public partial class WorldRoomNode : Resource
{
    [Export] public string RoomId = "";
    [Export] public string ScenePath = "";
    [Export] public string DisplayName = "";
}
