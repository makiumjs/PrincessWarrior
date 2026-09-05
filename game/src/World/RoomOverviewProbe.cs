using Godot;

namespace LostCrownlike.World;

/// CAPTURE-ONLY: frames a whole room at once, so its shape can be looked at
/// rather than inferred from numbers. The play camera shows about 19 units of
/// a room that is 70 long, which is why gaps in the floor and stacked ledges
/// were reported from play and never appeared in any capture here.
public partial class RoomOverviewProbe : Node
{
    [Export] public int Room = 5;
    [Export] public float ViewSize = 46f;
    [Export] public float CamX = -1f;
    [Export] public float CamY = -1f;

    private int _f;
    private bool _done;

    public override void _Ready()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--room=") && int.TryParse(a.Substring(7), out int r)) Room = r;
            if (a.StartsWith("--view=") && float.TryParse(a.Substring(7), out float v)) ViewSize = v;
            if (a.StartsWith("--camx=") && float.TryParse(a.Substring(7), out float cx)) CamX = cx;
            if (a.StartsWith("--camy=") && float.TryParse(a.Substring(7), out float cy)) CamY = cy;
        }
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        if (++_f < 10) return;

        var room = Find<DungeonRoomBuilder>(GetTree().Root);
        var cam = Find<Camera3D>(GetTree().Root);
        if (room == null || cam == null) return;

        _done = true;
        room.RebuildAs(Room);

        // HUD ability icons only appear once earned, so a capture of an
        // untouched room shows an empty corner.
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a != "--abilities") continue;
            foreach (Core.AbilityFlags f in System.Enum.GetValues<Core.AbilityFlags>())
                if (f != Core.AbilityFlags.None)
                    Core.EventBus.Instance?.EmitAbilityUnlocked(f);
        }

        // Stop the follow script instead of removing it. SetScript(default) tore
        // the node's script out from under the engine and printed 31 errors.
        cam.SetProcess(false);
        cam.SetPhysicsProcess(false);
        cam.Projection = Camera3D.ProjectionType.Orthogonal;
        cam.Size = ViewSize;
        cam.GlobalPosition = new Vector3(CamX >= 0f ? CamX : ViewSize * 0.42f,
                                        CamY >= 0f ? CamY : ViewSize * 0.22f, 40f);
        cam.Rotation = Vector3.Zero;
        GD.Print($"[VIEW] room {Room}, camera size {ViewSize}");

        // Numbers as well as a picture: "the ground is missing in much of the
        // level" needs to be read as ranges, not estimated from a screenshot.
        var m = new PlayerMetrics();
        float prevEnd = float.NaN, prevY = float.NaN;
        float covered = 0f, gapTotal = 0f;
        foreach (var child in room.GetChildren())
        {
            if (child is not StaticBody3D body) continue;
            if (body.GetChildOrNull<CollisionShape3D>(0)?.Shape is not BoxShape3D box) continue;
            float half = box.Size.X * 0.5f;
            float x0 = body.Position.X - half, x1 = body.Position.X + half;
            covered += box.Size.X;
            if (!float.IsNaN(prevEnd))
            {
                float gap = x0 - prevEnd;
                if (gap > 0.2f)
                {
                    gapTotal += gap;
                    GD.Print($"[VIEW]   GAP {gap:F1} wide at x {prevEnd:F1}..{x0:F1} " +
                             $"(dy {body.Position.Y - prevY:F1}) — safeGap {m.SafeGap:F1}, safeDashGap {m.SafeDashGap:F1}");
                }
            }
            prevEnd = x1; prevY = body.Position.Y;
        }
        GD.Print($"[VIEW] floor covers {covered:F1} units; gaps total {gapTotal:F1}");
    }

    private static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) { var f = Find<T>(c); if (f != null) return f; }
        return null;
    }
}
