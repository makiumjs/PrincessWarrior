using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: a room must not offer an ability the player already owns.
///
/// Found by mutation testing: removing the "already owned" filter broke no
/// check. A crystal on the floor that does nothing is the clearest possible
/// signal that a game is not paying attention to what the player has done.
public partial class PickupSkipTest : Node
{
    private int _f;
    private DungeonRoomBuilder _room;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindRoom(GetTree().Root);
        if (_room == null) return;

        if (_f == 20) EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash);

        // Rebuild AFTER the grant: the room that gets built must notice.
        if (_f == 40) _room.RebuildAs(0);

        if (_f == 80)
        {
            int dashPickups = 0, otherPickups = 0;
            foreach (var c in _room.GetChildren())
                if (c is AbilityPickup p)
                {
                    if (p.Ability == AbilityFlags.Dash) dashPickups++;
                    else otherPickups++;
                }

            GD.Print($"[PICKUP] after granting Dash and rebuilding: {dashPickups} Dash pickup(s), {otherPickups} other(s)");

            // Both halves: zero Dash crystals, but the room must still place the
            // ones the player has NOT earned, or "no redundant pickup" would
            // also be satisfied by a room that stopped placing any.
            if (otherPickups == 0)
                GD.Print("[PICKUP] RESULT: FAIL (no pickups at all - the room stopped placing them)");
            else
                GD.Print(dashPickups == 0
                    ? "[PICKUP] RESULT: PASS (an owned ability is not offered again, others still are)"
                    : $"[PICKUP] RESULT: FAIL ({dashPickups} Dash crystal(s) offered to a player who already has Dash)");
            GetTree().Quit();
        }
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
