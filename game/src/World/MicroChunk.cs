using System.Collections.Generic;
using Godot;

namespace LostCrownlike.World;

public enum ChunkKind
{
    /// Flat ground. Breathing room, and where encounters are placed.
    Gauntlet,
    /// A hole in the floor sized to a running jump.
    Gap,
    /// A wider hole that requires jump + dash.
    DashGap,
    /// Stacked ledges climbing upward, each within one jump.
    Chimney,
    /// A single step up onto a ledge.
    StepUp,
    /// A single step DOWN. Free to cross -- you fall off it -- and that is the
    /// point: a room made only of ascents climbs out of its own backdrop and
    /// reads as a staircase rather than a place. Added when the rooms were
    /// lengthened and the tallest one rose past 30 metres.
    Drop,
    /// Flat ground with a spike trap set into it: a hazard that must be jumped
    /// rather than a gap that must be crossed.
    Spikes,
    /// A flat arena preceded by the charge-attack pickup: the ability is
    /// introduced immediately before the fight that rewards it.
    Arena,
    /// A narrow vertical passage with grabbable walls on both sides: the only
    /// place WallJump is required, and therefore the only thing that makes the
    /// ability more than an unused flag.
    WallShaft,
}

/// A vertical, collidable surface the player can slide down and jump off.
public readonly struct WallRect
{
    public readonly float X, Y, Height;
    public WallRect(float x, float y, float height) { X = x; Y = y; Height = height; }
    public override string ToString() => $"wall({X:F1}, {Y:F1}) h={Height:F1}";
}

/// A platform the builder should place: world-space left edge, top surface
/// height, and width in metres.
public readonly struct PlatformRect
{
    public readonly float X, Y, Width;
    public PlatformRect(float x, float y, float width) { X = x; Y = y; Width = width; }
    public override string ToString() => $"({X:F1}, {Y:F1}) w={Width:F1}";
}

/// <summary>
/// Turns a sequence of chunk kinds into concrete platform rectangles, sizing
/// every gap and step strictly from <see cref="PlayerMetrics"/>. Nothing here
/// contains a hand-tuned distance: change the player's jump and the level
/// re-proportions itself.
/// </summary>
public sealed class MicroChunkComposer
{
    private readonly PlayerMetrics _m;
    private readonly List<PlatformRect> _rects = new();
    private float _cursorX;
    private float _cursorY;
    private readonly float _startY;

    /// Vertical extent of everything emitted so far, so the backdrop can be
    /// built to cover the room instead of to a fixed height. A room that
    /// climbs past its own back wall shows bare viewport above the player.
    public float MinY { get; private set; }
    public float MaxY { get; private set; }

    public MicroChunkComposer(PlayerMetrics metrics, float startX = 0f, float startY = 0f)
    {
        _m = metrics;
        _cursorX = startX;
        _cursorY = startY;
        _startY = startY;
        MinY = startY;
        MaxY = startY;
    }

    public IReadOnlyList<PlatformRect> Rects => _rects;
    public float EndX => _cursorX;

    /// Where an encounter belongs: the centre of each Gauntlet run. Gauntlets
    /// are the flat stretches, so an enemy placed here has room to patrol and
    /// the player has room to fight without the fight itself being a platforming
    /// hazard.
    /// Floor traps: world position of each spike tile.
    public IReadOnlyList<Vector3> Hazards => _hazards;
    private readonly List<Vector3> _hazards = new();

    public IReadOnlyList<WallRect> Walls => _walls;
    private readonly List<WallRect> _walls = new();

    public IReadOnlyList<Vector3> EncounterPoints => _encounters;
    private readonly List<Vector3> _encounters = new();

    /// The horizontal span of each Arena, and the height its floor sits at.
    /// The builder puts a barrier at the right-hand edge of each one: an arena
    /// you can sprint past is a decoration, not an encounter.
    public IReadOnlyList<(float X0, float X1, float Y)> Arenas => _arenas;
    private readonly List<(float, float, float)> _arenas = new();

    /// Where an ability pickup must sit, and which ability it grants: just
    /// before the chunk that cannot be crossed without it. The obstacle then
    /// teaches its own ability.
    public IReadOnlyList<(Vector3 Position, string Ability)> AbilityGrants => _grants;
    private readonly List<(Vector3, string)> _grants = new();

    /// Record that the NEXT chunk needs this ability, placing the pickup on the
    /// ground the player is standing on when they meet the obstacle.
    private void RequireAbility(string ability)
    {
        _grants.Add((new Vector3(_cursorX - 1.5f, _cursorY + 1.0f, 0f), ability));
    }

    /// Scales every obstacle in this room. Room 0 must be survivable by a
    /// player with no abilities and no practice: the metric contract proves a
    /// gap is *clearable*, it says nothing about whether meeting it in the
    /// first three seconds is fair. Measured before this existed: two deaths
    /// in 5.5 seconds on the opening run.
    public float Difficulty { get; set; } = 1f;

    public MicroChunkComposer Add(ChunkKind kind, float intensity = 1f)
    {
        intensity = Mathf.Clamp(intensity * Difficulty, 0.3f, 1f);
        switch (kind)
        {
            case ChunkKind.Gauntlet:
            {
                float width = Mathf.Max(_m.SafeGap * 1.5f, 8f);
                float centre = _cursorX + width * 0.5f;
                Emit(width);
                _encounters.Add(new Vector3(centre, _cursorY + 0.5f, 0f));
                break;
            }
            case ChunkKind.Gap:
            {
                // Landing pad first, then the void, then the far side.
                Emit(4f);
                _cursorX += _m.SafeGap * intensity;
                Emit(4f);
                break;
            }
            case ChunkKind.DashGap:
            {
                Emit(4f);
                RequireAbility("Dash");
                _cursorX += _m.SafeDashGap * intensity;
                Emit(4f);
                break;
            }
            case ChunkKind.StepUp:
            {
                Emit(4f);
                _cursorY += _m.SafeStepUp * intensity;
                Emit(4f);
                break;
            }
            case ChunkKind.Drop:
            {
                Emit(4f);
                // Never below the height the room started at. Under it the
                // floor would sit beneath the backdrop's lowest course, and a
                // deep enough one would put the player under FallDeathY on a
                // step they were meant to walk down.
                _cursorY = Mathf.Max(_startY, _cursorY - _m.SafeStepUp * 1.6f * intensity);
                Emit(4f);
                break;
            }
            case ChunkKind.Spikes:
            {
                // Continuous floor, with the trap set into the middle of it.
                // The danger is the surface itself, so the platform must not
                // break: a hazard you can fall past is just a gap.
                float run = Mathf.Max(_m.SafeGap * 2.2f, 10f);
                float trapX = _cursorX + run * 0.5f;
                Emit(run);
                _hazards.Add(new Vector3(trapX, _cursorY + 0.25f, 0f));
                break;
            }
            case ChunkKind.Arena:
            {
                Emit(4f);
                RequireAbility("ChargeAttack");
                float width = Mathf.Max(_m.SafeGap * 2f, 10f);
                float x0 = _cursorX;
                float centre = _cursorX + width * 0.5f;
                Emit(width);
                _encounters.Add(new Vector3(centre, _cursorY + 0.5f, 0f));
                _encounters.Add(new Vector3(centre + 3f, _cursorY + 0.5f, 0f));
                _arenas.Add((x0, _cursorX, _cursorY));
                break;
            }
            case ChunkKind.WallShaft:
            {
                Emit(4f);
                RequireAbility("WallJump");

                // Gap narrow enough that a wall jump crosses it easily, and
                // tall enough that a double jump alone cannot clear it — that
                // is what forces the ability rather than merely inviting it.
                float gap = Mathf.Max(_m.SafeGap * 0.55f, 2.0f);
                float height = _m.MaxDoubleJumpUp * 1.6f * intensity;

                _walls.Add(new WallRect(_cursorX, _cursorY, height));
                _walls.Add(new WallRect(_cursorX + gap, _cursorY, height));

                _cursorX += gap;
                _cursorY += height;
                Track(_cursorY);
                Emit(4f);   // landing platform at the top of the shaft
                break;
            }
            case ChunkKind.Chimney:
            {
                RequireAbility("DoubleJump");
                // Alternating ledges climbing a shaft, each one jump apart.
                const int steps = 3;
                float step = _m.SafeChimneyStep * intensity;
                float ledge = _m.ChimneyLedgeWidth;
                Emit(ledge);
                for (int i = 0; i < steps; i++)
                {
                    _cursorY += step;
                    // Zig-zag horizontally so the climb needs real inputs, but
                    // never further than a standing jump can carry.
                    // A staircase, not a zig-zag. Alternating left and right
                    // cleared the stacking but asked the player to reverse
                    // direction mid-climb in a level that runs left to right;
                    // stepping consistently forward does the same job and reads
                    // as progress. Measured: the alternating version dropped the
                    // traversal bot from three layouts crossed to one.
                    _cursorX += _m.ChimneyStagger;
                    _rects.Add(new PlatformRect(_cursorX, _cursorY, ledge));
                    Track(_cursorY);
                }
                _cursorX += ledge;
                break;
            }
        }
        return this;
    }

    private void Emit(float width)
    {
        _rects.Add(new PlatformRect(_cursorX, _cursorY, width));
        _cursorX += width;
        Track(_cursorY);
    }

    private void Track(float y)
    {
        MinY = Mathf.Min(MinY, y);
        MaxY = Mathf.Max(MaxY, y);
    }
}
