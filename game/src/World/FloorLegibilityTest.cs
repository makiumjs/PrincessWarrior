using System.Collections.Generic;
using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the floor has a visible underside, and no hole in it is
/// unlit.
///
/// Reported as "the floor seems to vanish and the platforms look fragmented in
/// the void". Two separate causes, both invisible to a suite that only asks
/// where the colliders are: a walkable surface was a 15cm plank with nothing
/// under it, so every edge showed a thin line and then background; and the
/// torches are at y=2.4 with nothing below, so the space a player has to jump
/// across was the darkest part of the frame.
///
/// Asserted against what was BUILT -- the colliders and the batched geometry --
/// not against the composer's plan. The plan has been right and the build wrong
/// before now: the first attempt at this used the pack's foundation piece on
/// assumed dimensions, and it is 2.2 wide on a 4-unit grid with its origin at
/// its base, so it stood 1.5 metres ABOVE the floor it was meant to be under
/// and left a hole between every block.
public partial class FloorLegibilityTest : Node
{
    private const float ExpectedDrop = 4f;      // WallHeight
    private const float MinWidth = 3.5f;        // MinWidthForFoundation
    private const float MinGap = 1.0f;

    private int _f;
    private int _room;
    private DungeonRoomBuilder _builder;

    private int _platformsChecked;
    private int _platformsUnsupported;
    private int _gapsChecked;
    private int _gapsUnlit;
    private int _placementMismatch;
    private bool _hasEnvironment;
    private bool _glowOn;
    private float _ambient = -1f;

    public override void _Process(double delta)
    {
        _f++;
        _builder ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_builder == null || _builder.IsRebuilding) return;
        if (_f % 25 != 0) return;

        if (_room == 0) AuditEnvironment();

        Audit(_room);
        _room++;

        if (_room >= _builder.RunLength)
        {
            GD.Print($"[FLOOR] platforms wide enough to need a face: {_platformsChecked}, " +
                     $"without one: {_platformsUnsupported}");
            GD.Print($"[FLOOR] holes in the floor: {_gapsChecked}, unlit: {_gapsUnlit}");
            GD.Print($"[FLOOR] rooms where placed and in-scene counts disagreed: {_placementMismatch}");
            GD.Print($"[FLOOR] world environment present={_hasEnvironment} glow={_glowOn} ambient={_ambient:F2}");

            bool ok = _platformsChecked > 0
                   && _gapsChecked > 0
                   && _platformsUnsupported == 0
                   && _gapsUnlit == 0
                   && _placementMismatch == 0
                   && _hasEnvironment && _glowOn && _ambient > 0f;

            GD.Print(ok
                ? "[FLOOR] RESULT: PASS (every wide platform stands on stone and every hole is lit)"
                : "[FLOOR] RESULT: FAIL");
            GetTree().Quit();
            return;
        }

        _builder.RebuildAs(_room);
    }

    /// The lighting the room is read THROUGH, not just the geometry in it.
    ///
    /// The game ran without a WorldEnvironment at all for most of its life: the
    /// viewport cleared to a colour set in project.godot as a workaround, and
    /// every emissive surface in the game -- the torches, the boss's seal, the
    /// impact sparks, the arena barrier -- was emissive and did not glow,
    /// because nothing was there to bloom it. Asserted here rather than trusted
    /// to the scene file, since a node that exists in one scene and not the one
    /// that ships is this project's oldest failure.
    private void AuditEnvironment()
    {
        var we = FindFirst<WorldEnvironment>(GetTree().Root);
        _hasEnvironment = we?.Environment != null;
        if (!_hasEnvironment) return;
        _glowOn = we.Environment.GlowEnabled;
        _ambient = we.Environment.AmbientLightEnergy;
    }

    private void Audit(int index)
    {
        var rects = _builder.LastComposedRects;
        var foundations = _builder.LastFoundations;

        // How many pieces actually reached the scene. This is the half the
        // builder cannot lie about: it is read off the MultiMesh in the tree.
        // Its transforms cannot be read -- the buffer is empty under the
        // headless renderer -- so WHERE they went is checked against what the
        // builder recorded placing, and HOW MANY against the scene.
        int inScene = CountFoundationInstances(_builder);

        int unsupported = 0, wide = 0;
        foreach (var r in rects)
        {
            if (r.Width < MinWidth) continue;
            wide++;
            bool supported = false;
            foreach (var f in foundations)
                if (f.X >= r.X - 2.1f && f.X <= r.X + r.Width + 2.1f
                    && Mathf.Abs(f.Y - (r.Y - ExpectedDrop)) < 0.3f)
                { supported = true; break; }
            if (!supported) { unsupported++; _platformsUnsupported++; }
        }
        _platformsChecked += wide;

        int unlit = 0;
        var torches = GetTree().GetNodesInGroup(DungeonRoomBuilder.GapTorchGroup);
        for (int i = 0; i < rects.Count - 1; i++)
        {
            float lip = rects[i].X + rects[i].Width;
            float gap = rects[i + 1].X - lip;
            if (gap < MinGap) continue;
            _gapsChecked++;
            bool lit = false;
            foreach (var t in torches)
                if (t is Node3D n && n.Position.X > lip - 0.6f && n.Position.X < rects[i + 1].X + 0.6f)
                { lit = true; break; }
            if (!lit) { unlit++; _gapsUnlit++; }
        }

        if (foundations.Count != inScene) _placementMismatch++;

        GD.Print($"[FLOOR] room {index}: platforms={rects.Count} wide={wide} " +
                 $"foundation placed={foundations.Count} in scene={inScene} " +
                 $"gap torches={torches.Count} unsupported={unsupported} unlit gaps={unlit}");
    }

    /// Instances of the foundation batch present in the room. Counting is all
    /// a headless run can do here, and it is worth doing: it is the difference
    /// between "the builder decided to place stone" and "stone is in the room".
    private static int CountFoundationInstances(Node n)
    {
        int total = 0;
        if (n is MultiMeshInstance3D mmi
            && mmi.Name.ToString().StartsWith("Batch_wall_foundation")
            && mmi.Multimesh != null)
            total += mmi.Multimesh.InstanceCount;
        foreach (var c in n.GetChildren()) total += CountFoundationInstances(c);
        return total;
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
