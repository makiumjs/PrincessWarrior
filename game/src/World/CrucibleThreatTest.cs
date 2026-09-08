using Godot;
using LostCrownlike.PlayerCamera;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: how dangerous the Colata actually is, measured rather than
/// claimed.
///
/// The branch's whole bargain is fewer enemies that hit harder. That is a
/// number, and until it is measured it is a hope: a Crucible that turned out
/// gentler than the Catacombs would be a longer walk with a fire theme.
///
/// The bound is TWO-SIDED, like the threat check this is modelled on. Under the
/// floor the branch is not a branch; over the ceiling a room the player is
/// asked to CHOOSE is one that kills them for choosing it.
///
/// And it runs a CONTROL. Total damage over four seconds is not the roster's
/// damage -- the room also has spikes, sweeping blades, and (once the bar is
/// past 70) a floor that burns. Measuring with SpawnEnemies on and then off and
/// subtracting is the only way to say what the enemies did, and this project
/// has already been wrong once by skipping exactly that step: a threat figure
/// stayed green at 56 damage with the melee grunt disarmed, and the tempting
/// explanation -- spike traps -- was not the true one.
public partial class CrucibleThreatTest : Node
{
    /// Measured against the real 1.0s HurtInvulnerability window: a passive
    /// player who does not attack triggers no death detonations and absorbs
    /// ~1 hit/second, averaging 27 damage over 4 seconds.
    private const int FloorDamage = 20;
    private const int CeilingDamage = 40;

    private const int ExposureFrames = 240;   // 4s at 60fps

    private int _f;
    private DungeonRoomBuilder _rooms;
    private PlayerController _player;

    private int _pass;
    private int _phaseStart;
    private int _healthAtStart = -1;
    private int _withEnemies = -1, _withoutEnemies = -1;
    private Vector3 _stand;

    public override void _Process(double delta)
    {
        _f++;
        _rooms ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_rooms == null || _player == null) { if (_f > 180) Done(false, "no room builder or player"); return; }
        if (_rooms.IsRebuilding) return;

        if (_f == 60)
        {
            HeatManager.Instance?.DebugForceActivate(HeatManager.Instance.EntryHeat);
            _rooms.SpawnEnemies = true;
            _rooms.RebuildAs(DungeonRoomBuilder.CrucibleFirstRoom);
            return;
        }

        if (_f == 110) { StartExposure(); return; }

        if (_pass == 1 && _f == _phaseStart + ExposureFrames)
        {
            _withEnemies = Taken();
            // Same room, same coordinates, same four seconds, enemies off. The
            // difference is the roster and nothing else.
            HeatManager.Instance?.DebugForceActivate(HeatManager.Instance.EntryHeat);
            _rooms.SpawnEnemies = false;
            _rooms.RebuildAs(DungeonRoomBuilder.CrucibleFirstRoom);
            _pass = 2;
            _phaseStart = _f;
            return;
        }

        if (_pass == 2 && _f == _phaseStart + 40) { StartExposure(); return; }

        if (_pass == 3 && _f == _phaseStart + ExposureFrames)
        {
            _withoutEnemies = Taken();
            Report();
            return;
        }

        // Pinned. A player that walks off the encounter ground is measuring a
        // different place every run, and this figure has to be comparable
        // between the two passes to mean anything at all.
        if (_pass is 1 or 3 && _player != null)
        {
            _player.GlobalPosition = _stand;
            _player.Velocity = Vector3.Zero;
        }
    }

    private void StartExposure()
    {
        // The second encounter point, not the first: the first is where the
        // player spawns and the builder deliberately leaves it empty.
        var points = _rooms.LastComposedRects;
        var arena = points.Count > 2 ? points[2] : points[points.Count - 1];
        _stand = new Vector3(arena.X + arena.Width * 0.5f, arena.Y + 1.0f, 0f);

        _player.Respawn();
        _player.GlobalPosition = _stand;
        _healthAtStart = _player.CurrentHealth;
        _phaseStart = _f;
        _pass = _pass == 2 ? 3 : 1;
    }

    private int Taken() => Mathf.Max(0, _healthAtStart - _player.CurrentHealth);

    private void Report()
    {
        int attributable = _withEnemies - _withoutEnemies;

        GD.Print($"[CRUTHREAT] 4s exposure with enemies: {_withEnemies} damage");
        GD.Print($"[CRUTHREAT] 4s exposure without enemies (control): {_withoutEnemies} damage");
        GD.Print($"[CRUTHREAT] attributable to the Crucible roster: {attributable} (band {FloorDamage}-{CeilingDamage})");

        bool inBand = attributable >= FloorDamage && attributable <= CeilingDamage;
        // The control is the part that matters. If hazards alone account for
        // the whole figure, the roster is decoration and the band is a
        // coincidence.
        bool rosterIsTheSource = attributable > _withoutEnemies;

        bool ok = inBand && rosterIsTheSource;
        Done(ok, ok ? $"the Colata's roster deals {attributable} in four seconds of standing still" : "");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[CRUTHREAT] RESULT: PASS ({why})" : "[CRUTHREAT] RESULT: FAIL");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
