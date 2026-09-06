using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: a room must not offer an ability the player already owns,
/// and must not offer the same one twice.
///
/// The first half was found by mutation testing: removing the "already owned"
/// filter broke no check. A crystal on the floor that does nothing is the
/// clearest possible signal that a game is not paying attention to what the
/// player has done.
///
/// The second half was found by reading the code after the rooms tripled in
/// length. The owned-filter is read ONCE before the loop, which was enough
/// while a room held one of each obstacle -- the tripled layouts hold three
/// DashGaps and three Chimneys, and every one of them asks for its ability.
/// Measured before the fix: room 0 laid out **three Dash crystals and two
/// Double Jump crystals**, and room 1 seven pickups where three would do.
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
            var perAbility = new System.Collections.Generic.Dictionary<AbilityFlags, int>();
            foreach (var c in _room.GetChildren())
                if (c is AbilityPickup p)
                {
                    if (p.Ability == AbilityFlags.Dash) dashPickups++;
                    else otherPickups++;
                    perAbility[p.Ability] = perAbility.TryGetValue(p.Ability, out int n) ? n + 1 : 1;
                }

            int duplicated = 0;
            var counts = new System.Collections.Generic.List<string>();
            foreach (var kv in perAbility)
            {
                counts.Add($"{kv.Key}x{kv.Value}");
                if (kv.Value > 1) duplicated++;
            }

            GD.Print($"[PICKUP] after granting Dash and rebuilding: {dashPickups} Dash pickup(s), " +
                     $"{otherPickups} other(s) [{string.Join(" ", counts)}]");

            // Three halves now. Zero Dash crystals, because the player has
            // Dash; at least one of something else, or "no redundant pickup"
            // would also be satisfied by a room that stopped placing any; and
            // no ability offered twice in the same room.
            if (otherPickups == 0)
                GD.Print("[PICKUP] RESULT: FAIL (no pickups at all - the room stopped placing them)");
            else if (dashPickups > 0)
                GD.Print($"[PICKUP] RESULT: FAIL ({dashPickups} Dash crystal(s) offered to a player who already has Dash)");
            else if (duplicated > 0)
                GD.Print($"[PICKUP] RESULT: FAIL ({duplicated} ability offered more than once in one room)");
            else
                GD.Print("[PICKUP] RESULT: PASS (an owned ability is not offered again, others once each)");

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
