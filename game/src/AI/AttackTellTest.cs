using Godot;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: an incoming attack can be SEEN coming.
///
/// This is the prerequisite for a parry, and it is worth checking on its own:
/// until now every enemy wound up with no distinct pose, because
/// EnemyState.Attack mapped to the same Throw clip for all three types and the
/// free KayKit pack has no melee swing to map instead. An attack with no tell
/// can only be answered by luck, and a perfect-parry window hung off an
/// invisible wind-up would be a guess dressed as a mechanic.
///
/// Three things have to hold, and they fail independently:
///   - the wind-up lasts long enough to react to,
///   - the arm actually moves during it,
///   - and the weapon lights up, because at this camera distance a raised arm
///     on a character a few pixels tall is not a reliable signal by itself.
public partial class AttackTellTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private EnemyController _enemy;
    private Skeleton3D _skel;
    private int _bone = -1;
    private Quaternion _rest;

    private int _windupFrames;
    private float _maxArmSwing;
    private float _maxGlow;
    private bool _sawStrike;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 30)
        {
            _enemy = GD.Load<PackedScene>("res://scenes/enemies/BasicMelee.tscn")
                       .Instantiate<EnemyController>();
            _room.AddChild(_enemy);
            _enemy.GlobalPosition = _player.GlobalPosition + new Vector3(1.6f, 0f, 0f);
            return;
        }

        if (_enemy == null || !IsInstanceValid(_enemy)) return;

        if (_skel == null)
        {
            _skel = FindFirst<Skeleton3D>(_enemy);
            if (_skel == null) return;
            _bone = _skel.FindBone("upperarm.r");
            if (_bone >= 0) _rest = _skel.GetBonePoseRotation(_bone);
        }

        if (_enemy.IsWindingUp)
        {
            _windupFrames++;
            if (_bone >= 0)
                _maxArmSwing = Mathf.Max(_maxArmSwing, _rest.AngleTo(_skel.GetBonePoseRotation(_bone)));
            _maxGlow = Mathf.Max(_maxGlow, PeakGlow(_enemy));
        }
        if (_enemy.IsStriking) _sawStrike = true;

        if (_f == 420)
        {
            float windupSeconds = _windupFrames / 60f;
            GD.Print($"[TELL] wind-up seen for {windupSeconds:F2}s across the run, " +
                     $"arm moved up to {Mathf.RadToDeg(_maxArmSwing):F0} degrees, " +
                     $"weapon glow peaked at {_maxGlow:F2}, strike observed: {_sawStrike}");

            bool ok = _sawStrike
                   && _windupFrames >= 18          // at least 0.3s of warning, summed
                   && _maxArmSwing > 0.5f          // ~30 degrees: a real pose, not a twitch
                   && _maxGlow > 0.5f;             // and a cue that carries at camera distance

            GD.Print(ok
                ? "[TELL] RESULT: PASS (attacks telegraph: the arm draws back and the weapon lights up)"
                : "[TELL] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    /// Reads the glow off the weapon's own material, not off a flag: the check
    /// should fail if the overlay stops being applied for any reason.
    private static float PeakGlow(Node from)
    {
        float peak = 0f;
        if (from is MeshInstance3D mi && mi.MaterialOverlay is StandardMaterial3D m)
            peak = m.EmissionEnergyMultiplier;
        foreach (var c in from.GetChildren()) peak = Mathf.Max(peak, PeakGlow(c));
        return peak;
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
