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
    /// Dash across, then climb out. TWO abilities in one breath, which is the
    /// thing the first nine kinds never asked for: every one of them teaches a
    /// verb and then tests that verb alone, so by the room that grants the last
    /// ability the game has said everything it has to say and can only repeat
    /// itself with bigger numbers.
    Chasm,
    /// A corridor crossed by blades that move on a cycle. The first chunk whose
    /// difficulty is a QUESTION OF TIMING rather than of size: every other kind
    /// here is a static distance measured against the player's jump, so the only
    /// axis the generator had for a later room was "bigger".
    Sweep,
    /// A floor that moves. The moving-platform idea that does NOT require
    /// waiting -- which is the whole reason there was none until now: a
    /// platform you must ride is a rhythm the traversal bot cannot prove,
    /// because it holds right and never stops, and a bot taught to wait stops
    /// being the deliberately clumsy witness the metric contract rests on.
    ///
    /// A moving FLOOR composes with holding right instead of fighting it. It
    /// drags you back over the first stretch and throws you forward over the
    /// second, so the same button produces two different speeds and the gap at
    /// the end is entered carrying something.
    Current,
    /// A barrier with a lever in front of it. The one chunk that asks the
    /// player to ACT on the level rather than cross it: everything else here is
    /// walk-into-it, and nothing in ten rooms has ever asked for a decision
    /// that is not "when do I jump".
    Latch,
    /// A drop straight into a gap, with no runway at the bottom. Nothing else
    /// here asks the player to keep momentum THROUGH a height change: a Drop is
    /// free and a DashGap is entered at a walk.
    Rift,
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

    /// Moving hazards: where each blade is anchored, and how far into its cycle
    /// it starts. The phase is carried here rather than set in the builder
    /// because it is a property of the CHUNK -- a row of blades all swinging
    /// together is a wall, and staggered they are a rhythm.
    public IReadOnlyList<(Vector3 Position, float Phase)> Sweeps => _sweeps;
    private readonly List<(Vector3, float)> _sweeps = new();

    /// Lever-and-barrier pairs: where the lever stands, and where the slab is.
    /// Emitted together because they are one obstacle -- a barrier whose lever
    /// lives in another chunk is a locked door with the key in another room,
    /// which is the metroidvania this game deliberately is not.
    public IReadOnlyList<(Vector3 Lever, Vector3 Gate)> Latches => _latches;
    private readonly List<(Vector3, Vector3)> _latches = new();

    /// Stretches of floor that carry whatever stands on them, as
    /// (from, to, metres per second). Recorded as SPANS rather than as a flag
    /// on a rect because a conveyor is a length of corridor, and the builder
    /// slices corridors into four-metre tiles on a grid the composer does not
    /// know about.
    public IReadOnlyList<(float X0, float X1, float Velocity)> Conveyors => _conveyors;
    private readonly List<(float, float, float)> _conveyors = new();

    public IReadOnlyList<WallRect> Walls => _walls;
    private readonly List<WallRect> _walls = new();

    public IReadOnlyList<Vector3> EncounterPoints => _encounters;
    private readonly List<Vector3> _encounters = new();

    public IReadOnlyList<PlatformRect> ElevatedPlatforms => _elevatedPlatforms;
    private readonly List<PlatformRect> _elevatedPlatforms = new();

    /// The horizontal span of each Arena, and the height its floor sits at.
    /// The builder puts a barrier at the right-hand edge of each one: an arena
    /// you can sprint past is a decoration, not an encounter.
    public IReadOnlyList<(float X0, float X1, float Y)> Arenas => _arenas;
    private readonly List<(float, float, float)> _arenas = new();

    /// Where an ability pickup must sit, and which ability it grants: just
    /// before the chunk that cannot be crossed without it. The obstacle then

    /// Record that the NEXT chunk needs this ability, placing the pickup on the
    /// ground the player is standing on when they meet the obstacle.
    
    /// Scales every obstacle in this room. Room 0 must be survivable by a
    /// player with no abilities and no practice: the metric contract proves a
    /// gap is *clearable*, it says nothing about whether meeting it in the
    /// first three seconds is fair. Measured before this existed: two deaths
    /// in 5.5 seconds on the opening run.
    public float Difficulty { get; set; } = 1f;

    /// Every kind this composition actually built, in order. Recorded so a
    /// check can ask which room contains a chunk instead of remembering the
    /// answer: "room 1 has the wall shaft" was true until the ability gate
    /// moved it to room 4, and two checks failed on a design decision rather
    /// than on a defect.
    public IReadOnlyList<ChunkKind> Kinds => _kinds;
    private readonly List<ChunkKind> _kinds = new();

    public MicroChunkComposer Add(ChunkKind kind, float intensity = 1f)
    {
        _kinds.Add(kind);

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
            case ChunkKind.Sweep:
            {
                // Solid ground the whole way: the danger is timing, not
                // spacing, and a blade you can fall past is just a gap with
                // decoration. Three blades, each a third of a cycle behind the
                // one before, so crossing at a constant walk meets them at
                // different points of their travel.
                const int blades = 3;
                float run = Mathf.Max(_m.SafeGap * 3.4f, 16f);
                float x0 = _cursorX;
                Emit(run);
                for (int i = 0; i < blades; i++)
                {
                    float at = x0 + run * (i + 1) / (blades + 1);
                    // The PIVOT height: the axe hangs 2.4m below it, so the
                    // head passes about 0.6m off the floor at the bottom of the
                    // swing -- low enough to be a threat, high enough that
                    // standing under the pivot is the safe place an arc needs,
                    // and low enough that the whole pendulum fits inside the
                    // band the side-on camera actually frames.
                    _sweeps.Add((new Vector3(at, _cursorY + 3.0f, 0f), i / (float)blades));
                }
                break;
            }
            case ChunkKind.Current:
            {
                // Against you, then with you, then a gap. The speeds are
                // fractions of the player's own run rather than metres picked
                // by eye: at 8 m/s a 3 m/s drag still lets you advance -- it
                // has to, or a bot holding right would stall and the chunk
                // would be unprovable -- and a 4 m/s push is half again as
                // fast as walking.
                float back = -_m.MoveSpeed * 0.375f;
                float fwd = _m.MoveSpeed * 0.5f;

                Emit(4f);
                float againstFrom = _cursorX;
                Emit(12f);
                _conveyors.Add((againstFrom, _cursorX, back));

                float withFrom = _cursorX;
                Emit(8f);
                _conveyors.Add((withFrom, _cursorX, fwd));

                // Between a running jump and a dash jump: reachable off the
                // push, and still reachable with a dash if you arrive slow.
                _cursorX += Mathf.Lerp(_m.SafeGap, _m.SafeDashGap, 0.45f) * intensity;
                Emit(4f);
                break;
            }
            case ChunkKind.Latch:
            {
                // Ground, lever, barrier, ground. The lever stands BEFORE the
                // slab and on the same floor, which is the modest version on
                // purpose: putting it up a ledge or across a gap is better
                // design and would make the chunk unprovable, because the
                // traversal bot swings at what blocks it and does not go
                // looking. The verb comes first; where the verb is asked for
                // can move once the bot can be trusted to search.
                Emit(6f);
                float leverX = _cursorX - 2.2f;
                Emit(4f);
                float gateX = _cursorX - 1.0f;
                _latches.Add((new Vector3(leverX, _cursorY, 0f),
                              new Vector3(gateX, _cursorY, 0f)));
                Emit(6f);
                break;
            }
            case ChunkKind.Arena:
            {
                Emit(4f);
                float width = Mathf.Max(_m.SafeGap * 2f, 10f);
                float x0 = _cursorX;
                float centre = _cursorX + width * 0.5f;
                Emit(width);

                // Multi-tiered vertical arena architecture:
                // Suspended high perch platform at +3.2m height, width 4m.
                // 3.2m vertical headroom ensures bot running & ground combat knockback pass seamlessly,
                // while double-jump (reach 3.8m) grants intentional high-ground advantage for player.
                float platWidth = 4.0f;
                _elevatedPlatforms.Add(new PlatformRect(centre - platWidth * 0.5f, _cursorY + 3.2f, platWidth));

                _encounters.Add(new Vector3(centre, _cursorY + 0.5f, 0f));
                _encounters.Add(new Vector3(centre + 3f, _cursorY + 0.5f, 0f));
                _arenas.Add((x0, _cursorX, _cursorY));
                break;
            }
            case ChunkKind.WallShaft:
            {
                Emit(4f);

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
            case ChunkKind.Chasm:
            {
                // A DashGap whose far side is a WallShaft. The dash is not
                // enough on its own and neither is the climb: the launch pad
                // and the shaft floor are separated by a full dash, and the
                // shaft is taller than a double jump.
                //
                // The shaft floor is emitted before the walls for the same
                // reason WallShaft does it: walls rise FROM a platform, and a
                // shaft with no floor is a pit with decoration on the sides.
                Emit(4f);
                _cursorX += _m.SafeDashGap * intensity;

                float shaft = Mathf.Max(_m.SafeGap * 0.55f, 2.0f);
                float climb = _m.MaxDoubleJumpUp * 1.3f * intensity;
                float baseX = _cursorX, baseY = _cursorY;
                Emit(shaft);
                _walls.Add(new WallRect(baseX, baseY, climb));
                _walls.Add(new WallRect(baseX + shaft, baseY, climb));

                _cursorY += climb;
                Track(_cursorY);
                Emit(4f);
                break;
            }
            case ChunkKind.Rift:
            {
                // Drop, then dash, with a landing too short to walk up speed on.
                // The gap is shortened to 0.85 because it is entered from a fall
                // rather than from a run, and the metric contract sizes
                // SafeDashGap for a player who is already moving.
                Emit(4f);
                float drop = _m.SafeStepUp * 1.4f * intensity;
                _cursorY = Mathf.Max(_startY, _cursorY - drop);
                Emit(2.5f);
                _cursorX += _m.SafeDashGap * 0.85f * intensity;
                Emit(4f);
                break;
            }
            case ChunkKind.Chimney:
            {
                const int steps = 3;
                float step = Mathf.Clamp(_m.SafeStepUp * intensity, 1.3f, 1.8f);
                float ledge = 4f;
                Emit(ledge);
                for (int i = 0; i < steps; i++)
                {
                    _cursorY += step;
                    Emit(ledge);
                }
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
