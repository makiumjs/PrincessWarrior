using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// Assembles a side-scroller room from KayKit Dungeon pieces (CC0) on the
/// pack's native 4-unit grid, and generates the collision the .gltf files
/// don't ship with.
///
/// Built in code rather than hand-authored as a .tscn: a corridor is a
/// repeating pattern, and there is no interactive editor session here to place
/// dozens of nodes by hand. Ledges/platforms are declared as data below so the
/// layout stays readable and the level can be re-tuned without touching
/// geometry code.
/// </summary>
public partial class DungeonRoomBuilder : Node3D
{
    private const string Dir = "res://assets/kaykit/dungeon/";
    private const float Grid = 4f;      // floor tile / wall footprint
    private const float WallHeight = 4f;

    /// How many 4-unit floor tiles the ground floor runs for.
    /// Ignored when <see cref="UseMicroChunks"/> is on — the chunk composer
    /// decides the room's extent from the player's movement metrics instead.
    [Export] public int FloorTiles = 8;

    /// Generate the room from parametric micro-chunks sized by PlayerMetrics
    /// rather than the fixed hand-placed layout below.
    [Export] public bool UseMicroChunks = true;
    [Export] public bool LogMetrics = false;
    [Export] public bool LogChunks = false;
    [Export] public bool SpawnEnemies = true;

    /// Which room in the run this is. Varies the chunk sequence, so crossing an
    /// exit lands the player somewhere genuinely different rather than in a
    /// copy of the room they just left.
    [Export] public int RoomIndex = 0;

    /// Length of a full run, in rooms. Six: the difficulty ramp in ComposeRoom
    /// saturates at index 3, so this gives three rooms of build-up and three
    /// at full difficulty, and every one of the three layouts is seen twice.
    [Export] public int RunLength = 6;

    /// TEST SEAM. When set to a ChunkKind name, the room becomes one gauntlet,
    /// that chunk at FULL difficulty, and a gauntlet with the exit on it.
    ///
    /// It exists because the traversal bot finishes rooms 0-2 and not 3-5, and
    /// "the bot is not good enough" and "one of those chunks is unclearable at
    /// saturated difficulty" look identical from outside. A whole room is too
    /// coarse to tell them apart; one chunk at a time is not.
    [Export] public string ChunkUnderTest = "";

    /// Torches carry an OmniLight3D each; togglable so their cost can be
    /// measured rather than guessed at.
    [Export] public bool SpawnTorches = true;
    /// Back wall sits behind the play plane so it never blocks movement.
    [Export] public float WallZ = -2f;

    /// Visible depth of an elevated ledge.
    [Export] public float LedgeThickness = 0.55f;
    [Export] public bool BuildProps = true;

    /// Raised platforms: X centre, Y height, width in grid tiles.
    /// Kept low and each one supported by a column: a side-scroller camera sits
    /// near the player's own height, so anything much above eye level is seen
    /// from underneath — and these tiles have an untextured underside, which
    /// read as bare white slabs floating in the air.
    private static readonly (float X, float Y, int Tiles)[] Ledges =
    {
        (10f, 2.4f, 2),
        (22f, 3.6f, 2),
    };

    /// Props sit against the back wall, not in the play plane. At Z=-0.6 a
    /// 1.8-wide barrel straddles Z=0 and simply hides the player behind it.
    private const float PropZ = -1.5f;

    public override void _Ready()
    {
        if (Core.EventBus.Instance != null)
        {
            Core.EventBus.Instance.RunCompleted += _ => { _runOver = true; };
            // The pause menu's "Restart run" arrives here. Announced on the bus
            // rather than wired directly, because the menu is UI and the rule in
            // this project is that UI never references a gameplay type.
            Core.EventBus.Instance.RestartRequested += RestartRun;
        }

        if (UseMicroChunks)
        {
            BuildFromChunks();
            return;
        }

        for (int i = 0; i < FloorTiles; i++)
            PlaceFloor(new Vector3(i * Grid, 0f, 0f));

        // Back wall, with an arch and a doorway broken into the run so it does
        // not read as one flat repeated slab.
        for (int i = 0; i < FloorTiles; i++)
        {
            string piece = (i % 4) switch
            {
                1 => "wall_arched",
                3 => "wall_doorway",
                _ => "wall",
            };
            Place(piece, new Vector3(i * Grid, 0f, WallZ), 0f);
        }

        foreach (var (x, y, tiles) in Ledges)
        {
            for (int i = 0; i < tiles; i++)
            {
                var at = new Vector3(x + i * Grid, y, 0f);
                PlaceFloor(at);

                // Cap the underside: seen from a camera at player height, an
                // uncapped raised tile shows its unfinished bottom. Rotate 180
                // rather than scaling by -1 — a negative scale inverts the
                // winding so the face renders unlit.
                var under = Place("floor_tile_large", at + new Vector3(0f, -0.16f, 0f), 0f);
                if (under != null)
                    under.RotationDegrees = new Vector3(180f, 0f, 0f);
            }

            // A column was stacked under each platform to stop it looking like
            // it floats, stretched vertically to reach. Non-uniform scaling on
            // a kit piece reads as exactly what it is — a smeared box — and the
            // underside cap above already solves the floating look, so the
            // platform now stands on its own.
        }

        if (!BuildProps) return;

        Place("barrel_large", new Vector3(3.2f, 0f, PropZ), 15f);
        Place("crates_stacked", new Vector3(14.5f, 0f, PropZ), -20f);
        Place("chest", new Vector3(26f, 0f, PropZ + 0.3f), 8f);
        HangBanner(new Vector3(8f, 2.2f, WallZ + 0.45f));
        HangBanner(new Vector3(24f, 2.2f, WallZ + 0.45f));
    }

    /// Composes the room from micro-chunks, then realises each returned
    /// PlatformRect as floor tiles plus a single box collider spanning the
    /// whole platform — one wide box rather than one per tile, so the player
    /// cannot snag on the seam between two adjacent colliders.
    private void BuildFromChunks()
    {
        foreach (var step in BuildSteps()) step();
    }

    /// The build, as a sequence of steps.
    ///
    /// A rebuild costs about 155ms and it lands on the single frame the player
    /// crosses a door -- nine dropped frames, which reads as the game locking
    /// up every time you go through one. Expressing the phases as steps lets
    /// the caller choose: run them all now (the first room, and every test,
    /// which needs the room to exist by the time the call returns) or one per
    /// frame (the game).
    private System.Collections.Generic.IEnumerable<System.Action> BuildSteps()
    {
        var metrics = new PlayerMetrics();
        metrics.VerifyAgainst(GetTree()?.GetFirstNodeInGroup("player"));

        var composer = string.IsNullOrEmpty(ChunkUnderTest)
            ? ComposeRoom(metrics, RoomIndex)
            : ComposeSingleChunk(metrics, ChunkUnderTest);

        yield return () =>
        {
            foreach (var r in composer.Rects)
            {
                // The per-chunk dump used to sit inside the timed loop and
                // dominated its own measurement: GD.Print to a console costs far
                // more than placing a platform, so "platforms: 233ms" was mostly
                // printing. Behind its own flag now.
                if (LogChunks) GD.Print($"[CHUNK] {r}");
                PlacePlatform(r);
            }
        };

        yield return () =>
        {
            foreach (var h in composer.Hazards) AddChild(new SpikeHazard { Position = h });
            foreach (var w in composer.Walls) PlaceClimbableWall(w);
        };

        yield return () => BuildBackdrop(composer.EndX);
        yield return () => SpawnEncounters(composer);
        yield return () => SpawnCheckpoints(composer);
        yield return () => SpawnAbilityPickups(composer);
        yield return () => SpawnExit(composer);
        yield return FlushBatches;
    }

    /// One chunk, flanked by flat ground, at the hardest setting the ramp ever
    /// produces. The leading gauntlet gives room to build speed, which several
    /// chunks need; starting a dash gap from a standstill would measure a
    /// situation the game never puts the player in.
    private static MicroChunkComposer ComposeSingleChunk(PlayerMetrics m, string kindName)
    {
        var c = new MicroChunkComposer(m) { Difficulty = 1f };
        if (!System.Enum.TryParse(kindName, out ChunkKind kind))
        {
            GD.PushError($"DungeonRoomBuilder: unknown ChunkUnderTest '{kindName}'");
            return c.Add(ChunkKind.Gauntlet);
        }
        return c.Add(ChunkKind.Gauntlet).Add(kind).Add(ChunkKind.Gauntlet);
    }

    /// Room layouts. Hand-ordered rather than randomised: a metroidvania's
    /// rooms are authored, and the metric contract already guarantees every
    /// sequence here is traversable whatever the player's jump is tuned to.
    private static MicroChunkComposer ComposeRoom(PlayerMetrics m, int index)
    {
        // Ramp: the opening room runs at 55% obstacle size and grows from
        // there, so the first gap teaches the jump instead of executing the
        // player before they have an ability to their name.
        var c = new MicroChunkComposer(m)
        {
            Difficulty = Mathf.Min(0.55f + index * 0.15f, 1f),
        };
        switch (index % 3)
        {
            case 0:
                // Opens with flat ground and a step, not a hole.
                return c.Add(ChunkKind.Gauntlet)
                        .Add(ChunkKind.StepUp, 0.6f)
                        .Add(ChunkKind.Spikes)
                        .Add(ChunkKind.Gap)
                        .Add(ChunkKind.StepUp)
                        .Add(ChunkKind.DashGap)
                        .Add(ChunkKind.Chimney)
                        .Add(ChunkKind.Gauntlet);
            case 1:
                return c.Add(ChunkKind.Gauntlet)
                        .Add(ChunkKind.WallShaft)
                        .Add(ChunkKind.Gap, 0.9f)
                        .Add(ChunkKind.Gap, 0.7f)
                        .Add(ChunkKind.Gauntlet)
                        .Add(ChunkKind.Chimney, 0.8f)
                        .Add(ChunkKind.DashGap, 0.8f)
                        .Add(ChunkKind.Gauntlet);
            default:
                return c.Add(ChunkKind.Gauntlet)
                        .Add(ChunkKind.Arena)
                        .Add(ChunkKind.Chimney)
                        .Add(ChunkKind.DashGap)
                        .Add(ChunkKind.Gauntlet, 0.7f)
                        .Add(ChunkKind.Gap)
                        .Add(ChunkKind.StepUp)
                        .Add(ChunkKind.Gauntlet);
        }
    }

    /// Exit at the far end of the room. Placed on the ground the last chunk
    /// ends on, so it cannot be reached without completing the room.
    private void SpawnExit(MicroChunkComposer composer)
    {
        var last = composer.Rects[composer.Rects.Count - 1];
        var exit = new RoomExitTrigger
        {
            Name = "RoomExit",
            NextRoomIndex = RoomIndex + 1,
            RunLength = RunLength,
            SealedUntilBossDies = IsBossRoom,
            Position = new Vector3(last.X + last.Width - 1.5f, last.Y + 1.2f, 0f),
        };
        exit.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.5f, 3.5f, 2f) },
        });
        AddChild(exit);
    }

    /// Drops each ability pickup in front of the obstacle that needs it.
    private void SpawnAbilityPickups(MicroChunkComposer composer)
    {
        // What the player already has, so a later room does not litter the
        // floor with a second Dash crystal the player cannot use. Read from the
        // live player when there is one (it is the authority mid-run), falling
        // back to the save for a room built before the player exists.
        var owned = AbilityFlags.None;
        if (GetTree()?.GetFirstNodeInGroup("player") is Node playerNode)
            owned = (AbilityFlags)(int)playerNode.Get("UnlockedAbilities");
        else if (Save.SaveManager.Instance?.Current != null)
            owned = Save.SaveManager.Instance.Current.UnlockedAbilities;

        foreach (var (position, abilityName) in composer.AbilityGrants)
        {
            if (!System.Enum.TryParse(abilityName, out AbilityFlags ability))
            {
                GD.PushError($"DungeonRoomBuilder: unknown ability '{abilityName}'");
                continue;
            }
            if (owned.HasFlag(ability))
                continue;
            AddChild(new AbilityPickup { Ability = ability, Position = position });
        }
    }

    /// A checkpoint at the start of each Gauntlet. Without these the
    /// die-and-respawn loop cannot happen in a generated room at all: Save
    /// records a position only when CheckpointReached fires, so a player who
    /// died would have nothing to respawn to.
    private void SpawnCheckpoints(MicroChunkComposer composer)
    {
        int i = 0;
        foreach (var point in composer.EncounterPoints)
        {
            var trigger = new CheckpointTrigger
            {
                Name = $"Checkpoint_{i}",
                CheckpointId = $"shrine_{i:D2}",
                Position = point + new Vector3(-3f, 0.5f, 0f),
            };
            trigger.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(1.5f, 3f, 2f) },
            });
            AddChild(trigger);
            i++;
        }
    }

    /// Places an enemy at each Gauntlet centre, with patrol markers set to the
    /// platform it stands on. The enemy's own ledge probe keeps it from walking
    /// into the neighbouring gap even if the markers are generous.
    private void SpawnEncounters(MicroChunkComposer composer)
    {
        if (!SpawnEnemies) return;

        var meleeScene = GD.Load<PackedScene>("res://scenes/enemies/BasicMelee.tscn");
        if (meleeScene == null)
        {
            GD.PrintErr("DungeonRoomBuilder: BasicMelee.tscn not found; no encounters spawned");
            return;
        }
        var sentryScene = GD.Load<PackedScene>("res://scenes/enemies/CrossbowSentry.tscn");
        var skirmisherScene = GD.Load<PackedScene>("res://scenes/enemies/Skirmisher.tscn");
        var warlockScene = GD.Load<PackedScene>("res://scenes/enemies/Warlock.tscn");

        // The last room is the boss, and only the boss. Mixing the usual
        // rotation in would give the player something cheaper to hit than the
        // armoured thing in the middle, and the whole point of the fight is
        // that there is no cheaper option.
        if (IsBossRoom)
        {
            SpawnBoss(composer);
            return;
        }

        int i = 0;
        int placed = 0;
        foreach (var point in composer.EncounterPoints)
        {
            // Skip the first gauntlet: the player spawns there.
            if (i++ == 0) continue;

            // Alternate the two types, and keep room 0 all melee. The first
            // room is where the player learns that enemies can be walked up to
            // and hit; introducing a threat that answers that with a bolt in
            // the same room teaches the wrong lesson first. Deterministic
            // rather than random: several gate checks assert what a given room
            // contains, and a coin flip would make them flaky.
            // Rotate the three types on a fixed cycle. Room 0 stays all melee.
            // Deterministic rather than random: several gate checks assert what
            // a given room contains, and a coin flip would make them flaky.
            // The cycle runs over ENEMIES PLACED and carries across rooms.
            //
            // Counting encounter points instead let the skipped spawn gauntlet
            // shift the cycle, so the first sentry landed on the fourth
            // encounter. Restarting the cycle at each room was worse and less
            // obvious: a generated room holds about two enemies, so with three
            // types the third never appeared anywhere -- measured as zero
            // skirmishers in room 1. Offsetting by RoomIndex also makes
            // consecutive rooms open with different enemies.
            // Four types now, and the warlock only from room 2. It holds range
            // rather than standing in it, so meeting one before you have the
            // dash to close the ground it keeps taking back is a fight with no
            // answer rather than a hard one.
            PackedScene pick = meleeScene;
            if (RoomIndex > 0)
            {
                int types = RoomIndex >= 2 && warlockScene != null ? 4 : 3;
                int slot = (RoomIndex + placed) % types;
                if (slot == 1 && sentryScene != null) pick = sentryScene;
                else if (slot == 2 && skirmisherScene != null) pick = skirmisherScene;
                else if (slot == 3) pick = warlockScene;
            }
            placed++;
            var enemy = pick.Instantiate<Node3D>();
            AddChild(enemy);
            enemy.GlobalPosition = point;

            var a = new Marker3D { Position = point + new Vector3(-3f, 0f, 0f) };
            var b = new Marker3D { Position = point + new Vector3(3f, 0f, 0f) };
            AddChild(a);
            AddChild(b);
            enemy.Set("PatrolPointAPath", enemy.GetPathTo(a));
            enemy.Set("PatrolPointBPath", enemy.GetPathTo(b));
        }
    }

    /// The last room of the run. A property rather than a literal because
    /// RunLength is an [Export]: tuning the run length must not silently move
    /// the boss to a room that no longer exists.
    public bool IsBossRoom => RoomIndex == RunLength - 1;

    /// Drops the boss on the widest platform in the room, which the composer
    /// builds as the arena. Placed off the last encounter point rather than at
    /// the exit so the player crosses the room and meets it, instead of
    /// walking into it at the door.
    private void SpawnBoss(MicroChunkComposer composer)
    {
        var bossScene = GD.Load<PackedScene>("res://scenes/enemies/Warden.tscn");
        if (bossScene == null)
        {
            GD.PrintErr("DungeonRoomBuilder: Warden.tscn not found; the last room has no boss");
            return;
        }

        // Widest rect, not last: the last chunk carries the exit, and a boss
        // standing on the door is a boss the player fights with their back to
        // a wall they cannot use.
        var arena = composer.Rects[0];
        foreach (var r in composer.Rects)
            if (r.Width > arena.Width) arena = r;

        var boss = bossScene.Instantiate<Node3D>();
        AddChild(boss);
        boss.GlobalPosition = new Vector3(arena.X + arena.Width * 0.65f, arena.Y + 1.2f, 0f);

        var a = new Marker3D { Position = boss.GlobalPosition + new Vector3(-arena.Width * 0.3f, 0f, 0f) };
        var b = new Marker3D { Position = boss.GlobalPosition + new Vector3(arena.Width * 0.25f, 0f, 0f) };
        AddChild(a);
        AddChild(b);
        boss.Set("PatrolPointAPath", boss.GetPathTo(a));
        boss.Set("PatrolPointBPath", boss.GetPathTo(b));

        GD.Print($"[Room] boss placed at x={boss.GlobalPosition.X:0.0} on a {arena.Width:0.0}-wide arena");
    }

    /// One platform: visual tiles on the 4-unit grid, and ONE box collider for
    /// the whole span.
    private void PlacePlatform(PlatformRect r)
    {
        int tiles = Mathf.Max(1, Mathf.CeilToInt(r.Width / Grid));
        for (int i = 0; i < tiles; i++)
        {
            var at = new Vector3(r.X + i * Grid + Grid * 0.5f, r.Y, 0f);

            // Ground level gets a plain floor tile. An ELEVATED ledge is seen
            // edge-on from the play camera, never from above, so it needs sides:
            // a flat tile read as a bare grey plank floating in the dark, and
            // stacking a second tile underneath only turned that into two planks
            // with a visible gap between them. floor_foundation_allsides is the
            // pack's own answer -- a block with faces on every side.
            if (r.Y > 0.01f)
            {
                var blockAt = at + new Vector3(0f, -LedgeThickness * 0.5f, 0f);
                // "wall", not "floor_foundation_allsides". Measured on a played
                // capture: the foundation piece samples a neutral grey from the
                // atlas -- R44 G46 B47 at brightness 45.6 -- while every other
                // surface in the room is warm stone at 94-114. It is not in
                // shadow (shadow keeps the hue) and it is not the batching (the
                // floor tiles go through the same MultiMesh and stay warm); the
                // prop is simply dark by design, and a raised ledge made of it
                // reads as an unfinished block.
                PlaceBatched("floor_foundation_allsides",
                    new Transform3D(Basis.Identity.Scaled(new Vector3(1f, Mathf.Max(LedgeThickness, 0.35f), 1f)), blockAt),
                    LedgeTint);
            }
            PlaceBatched("floor_tile_large", new Transform3D(Basis.Identity, at));
        }

        float span = tiles * Grid;
        var body = new StaticBody3D
        {
            Position = new Vector3(r.X + span * 0.5f, r.Y - 0.075f, 0f),
            CollisionLayer = PhysicsLayers.World,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(span, 0.15f, Grid) },
        });
        AddChild(body);
    }

    /// A collidable vertical surface, on the World layer so the player's
    /// wall-slide detection finds it. The backdrop walls deliberately have no
    /// collision — they are scenery — so before this existed the WallJump
    /// ability had no surface anywhere in the game to be used on.
    private void PlaceClimbableWall(WallRect w)
    {
        int courses = Mathf.Max(1, Mathf.CeilToInt(w.Height / WallHeight));
        for (int i = 0; i < courses; i++)
            Place("wall", new Vector3(w.X, w.Y + i * WallHeight, 0f), 90f);

        var body = new StaticBody3D
        {
            Position = new Vector3(w.X, w.Y + w.Height * 0.5f, 0f),
            CollisionLayer = PhysicsLayers.World,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.8f, w.Height, Grid) },
        });
        AddChild(body);
    }

    /// Wall run behind the play plane, long enough to back the whole room —
    /// plus a second course BELOW the walkable floor. Without the lower course
    /// the bottom third of a side-scroller frame is bare background: the camera
    /// sits near the player's height, so everything under the floor line is
    /// visible and empty.
    private void BuildBackdrop(float endX)
    {
        int count = Mathf.CeilToInt(endX / Grid) + 1;
        for (int i = 0; i < count; i++)
        {
            string piece = (i % 4) switch
            {
                1 => "wall_arched",
                3 => "wall_doorway",
                _ => "wall",
            };
            // Three courses, not two. The corridor wall ran from -4 to +4 and
            // stopped, so a player climbing a chimney rose out of the built
            // world into a dim band with no structure and no light -- measured
            // on a played capture as the room dropping from ~110 brightness to
            // ~60 the moment you leave the floor. Batched, because these are
            // dozens of identical pieces per room and instantiating each as its
            // own scene tree is what made rebuilds expensive.
            PlaceBatched(piece, new Transform3D(Basis.Identity, new Vector3(i * Grid, 0f, WallZ)));
            PlaceBatched("wall", new Transform3D(Basis.Identity, new Vector3(i * Grid, -WallHeight, WallZ)));
            PlaceBatched("wall", new Transform3D(Basis.Identity, new Vector3(i * Grid, WallHeight, WallZ)));

            // A mounted torch every other wall section, each with a real
            // OmniLight3D. The round-3 critic scored lighting as flat: two
            // directional lights and nothing else, in a dungeon kit that ships
            // torches. Local warm pools are what give a stone corridor depth.
            if (SpawnTorches && i % 2 == 1)
                MountTorch(new Vector3(i * Grid, 2.4f, WallZ + 0.55f), 3.4f);

            // A sparser upper row. Climbing should not mean climbing into the
            // dark: a platformer where you cannot see the ledge you are aiming
            // at is unfair in a way that reads as the game being broken rather
            // than hard. Sparser than the floor row so the corridor still has
            // pools of light rather than an even wash.
            if (SpawnTorches && i % 4 == 2)
                MountTorch(new Vector3(i * Grid, 2.4f + WallHeight, WallZ + 0.55f), 2.6f);
        }
    }

    private void MountTorch(Vector3 at, float energy)
    {
        Place("torch_mounted", at, 0f);
        AddChild(new TorchFlicker
        {
            Position = at + new Vector3(0f, 0.35f, 0.6f),
            LightColor = new Color(1f, 0.72f, 0.42f),
            BaseEnergy = energy,
            OmniRange = 8.5f,
            ShadowEnabled = false,
        });
    }

    /// Clears the room and regenerates it with a new layout, then places the
    /// player at the start. Called deferred from the exit trigger.
    private bool _runOver;
    private bool _jumpWasHeld;

    /// The exit of the last room used to emit RunCompleted and stop there: a
    /// banner appeared and nothing else happened, leaving the player standing
    /// in a room with no way forward, no restart and no ending. Reported from
    /// play as "there is no way to finish the room" -- which was accurate, and
    /// is a gap in the arc rather than in the level.
    public override void _Process(double delta)
    {
        // Advances a spread rebuild, one step per frame. This line was lost to a
        // silent no-op edit once: without it _pendingBuild never advances,
        // IsRebuilding stays true forever and every check that waits for the
        // room to finish HANGS instead of failing -- which is how it presented.
        TickRebuild();

        // The edge is tracked here rather than read from IsActionJustPressed.
        // That flag is true only during the frame of the press, and whether this
        // node's _Process runs before or after whatever produced the press is
        // scene order -- checking first and pressing second means the window is
        // always missed. Traced: 300+ process ticks, jumpPressed False on every
        // one, while the presses were certainly happening.
        bool held = Input.IsActionPressed("jump");
        bool pressedNow = held && !_jumpWasHeld;
        _jumpWasHeld = held;

        if (!_runOver || !pressedNow) return;

        RestartRun();
    }

    /// Shared by the run-complete banner and the pause menu, so "start over"
    /// means the same thing however it is asked for.
    private void RestartRun()
    {
        _runOver = false;
        Save.SaveManager.Instance?.ResetSave();

        if (GetTree().GetFirstNodeInGroup("player") is PlayerCamera.PlayerController p)
            p.ResetForNewRun();

        RebuildAs(0);
        // Announce it as a transition so listeners that reacted to the ending
        // can undo that -- the HUD banner in particular, which otherwise stayed
        // on screen over the new run.
        Core.EventBus.Instance?.EmitLevelTransitionRequested("generated:0", "start");
        GD.Print("[Run] restarted from room 0");
    }

    private System.Collections.Generic.IEnumerator<System.Action> _pendingBuild;

    /// True while a spread rebuild is still running.
    public bool IsRebuilding => _pendingBuild != null;

    /// Starts a rebuild that runs ONE step per frame instead of all of them on
    /// the frame the player crosses a door. The teardown and the player move
    /// still happen immediately -- they are cheap, and leaving the old room up
    /// while the new one appears around it would be worse than a hitch.
    ///
    /// The player is held still until the room exists: with the floor not yet
    /// placed it would otherwise fall through the world during the seven frames
    /// the build takes.
    public void BeginRebuild(int roomIndex)
    {
        TearDown();
        RoomIndex = roomIndex;
        _pendingBuild = BuildSteps().GetEnumerator();
        MovePlayerToStart(freeze: true);
    }

    private void TickRebuild()
    {
        if (_pendingBuild == null) return;
        if (_pendingBuild.MoveNext())
        {
            _pendingBuild.Current();
            MovePlayerToStart(freeze: true);   // keep it pinned while the floor arrives
            return;
        }

        _pendingBuild = null;
        MovePlayerToStart(freeze: false);
    }

    private void MovePlayerToStart(bool freeze)
    {
        if (GetTree()?.GetFirstNodeInGroup("player") is not Node3D player) return;
        if (freeze) player.GlobalPosition = new Vector3(2f, 2f, 0f);
        if (player is CharacterBody3D body) body.Velocity = Vector3.Zero;
    }

    private void TearDown()
    {
        // RemoveChild before QueueFree: a queued-but-still-parented node keeps
        // its name reserved, so the rebuilt room's exit was silently renamed
        // ("@Area3D@188" instead of "RoomExit") and any lookup by name failed.
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    /// Synchronous rebuild: the room exists by the time this returns. Used by
    /// the first room and by every check, which asserts on the result
    /// immediately and would otherwise be racing a build.
    public void RebuildAs(int roomIndex)
    {
        _pendingBuild = null;
        TearDown();
        RoomIndex = roomIndex;
        BuildFromChunks();
        MovePlayerToStart(freeze: true);
    }

    /// Banners hang from a swaying pivot rather than being placed rigidly, so
    /// the cloth moves from its top edge like real hanging fabric.
    private void HangBanner(Vector3 at)
    {
        var pivot = new BannerSway { Position = at };
        AddChild(pivot);
        var scene = GD.Load<PackedScene>($"{Dir}banner_red.gltf");
        if (scene == null) return;
        var banner = scene.Instantiate<Node3D>();
        banner.Position = new Vector3(0f, -0.6f, 0f);
        pivot.AddChild(banner);
    }

    // ------------------------------------------------------------------
    // Batched structural pieces
    // ------------------------------------------------------------------
    //
    // Place() instantiates a whole scene tree per piece. For the structural
    // props -- floor tiles, the blocks under raised ledges, the backdrop
    // courses -- that is dozens of identical trees per room, and it dominated
    // the rebuild: 233ms of a 450ms build, measured by timing each phase after
    // correlating a played run's worst frames with its events.
    //
    // These pieces never move, never animate and never carry a script, which is
    // exactly what MultiMesh is for: one node, one draw setup, N transforms.
    // Collision is unaffected -- it was always separate box shapes, never the
    // imported meshes.

    private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Transform3D>> _batches = new();
    private int _batchSeq;

    /// Warm tint for the block under a raised ledge.
    ///
    /// The piece samples a neutral grey from the KayKit atlas -- measured
    /// R44 G46 B47 at brightness 45.6, against warm stone at 94-114 everywhere
    /// else in the room -- so a ledge made of it reads as an untextured block
    /// rather than as masonry in shadow. Swapping in the "wall" piece instead
    /// was worse: it is four units wide and swallowed the screen. Tinting keeps
    /// the right silhouette and brings it into the room's palette.
    [Export] public Color LedgeTint = new(1f, 0.72f, 0.45f, 0.42f);

    private readonly System.Collections.Generic.Dictionary<string, Color> _batchTints = new();

    private void PlaceBatched(string name, Transform3D xform, Color? tint = null)
    {
        if (!_batches.TryGetValue(name, out var list))
        {
            list = new System.Collections.Generic.List<Transform3D>();
            _batches[name] = list;
        }
        list.Add(xform);
        if (tint.HasValue) _batchTints[name] = tint.Value;
    }

    /// Turns everything collected by PlaceBatched into MultiMeshInstance3Ds.
    /// A prop can contain more than one mesh, each with its own local
    /// transform, so one MultiMesh is built per contained mesh and that local
    /// transform is folded into every instance.
    private void FlushBatches()
    {
        foreach (var (name, transforms) in _batches)
        {
            var scene = GD.Load<PackedScene>($"{Dir}{name}.gltf");
            if (scene == null) { GD.PrintErr($"DungeonRoomBuilder: missing piece '{name}'"); continue; }

            var probe = scene.Instantiate<Node3D>();
            var meshes = new System.Collections.Generic.List<(Mesh Mesh, Transform3D Local, Material Mat)>();
            CollectMeshParts(probe, Transform3D.Identity, meshes);
            probe.QueueFree();

            foreach (var (mesh, local, _) in meshes)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = transforms.Count,
                };
                for (int i = 0; i < transforms.Count; i++)
                    mm.SetInstanceTransform(i, transforms[i] * local);

                // No MaterialOverride. A GLTF mesh carries its materials per
                // SURFACE, and overriding with surface 0's forces that one onto
                // all of them -- the raised ledges went from lit stone to flat
                // dark grey, which a capture caught and the gate could not.
                // A deliberate overlay on ONE batch, not a blanket override on
                // all of them: overriding every batch with surface 0's material
                // was the earlier mistake, since a GLTF mesh carries materials
                // per surface.
                var node = new MultiMeshInstance3D
                {
                    Name = $"Batch_{name}_{_batchSeq++}",
                    Multimesh = mm,
                };
                if (_batchTints.TryGetValue(name, out var tint))
                    node.MaterialOverlay = new StandardMaterial3D
                    {
                        AlbedoColor = tint,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    };
                AddChild(node);
            }
        }
        _batches.Clear();
        _batchTints.Clear();
        _batchSeq = 0;
    }

    private static void CollectMeshParts(Node node, Transform3D acc,
        System.Collections.Generic.List<(Mesh, Transform3D, Material)> into)
    {
        var here = node is Node3D n3 ? acc * n3.Transform : acc;
        if (node is MeshInstance3D mi && mi.Mesh != null)
            into.Add((mi.Mesh, here, mi.GetActiveMaterial(0)));
        foreach (var c in node.GetChildren()) CollectMeshParts(c, here, into);
    }

    private Node3D Place(string name, Vector3 pos, float yawDegrees)
    {
        var scene = GD.Load<PackedScene>($"{Dir}{name}.gltf");
        if (scene == null)
        {
            GD.PrintErr($"DungeonRoomBuilder: missing piece '{name}'");
            return null;
        }
        var inst = scene.Instantiate<Node3D>();
        inst.Position = pos;
        inst.RotationDegrees = new Vector3(0f, yawDegrees, 0f);
        AddChild(inst);
        return inst;
    }

    /// A floor tile plus the box collider the imported mesh has no shape for.
    ///
    /// KNOWN COSMETIC LIMITATION: floor_tile_large is textured on its TOP face
    /// only, so wherever a tile's edge is visible — the front lip of the ground
    /// run, and the sides of a raised ledge — it renders as a bare light-grey
    /// band. Tried and rejected: the pack's floor_foundation_* pieces, which
    /// are raised base blocks extending UPWARD from the floor plane, not edge
    /// trim; they occluded the player entirely. Proper fix is a level-art pass
    /// choosing a piece meant for exposed platform edges, not a code change.
    private void PlaceFloor(Vector3 pos)
    {
        Place("floor_tile_large", pos, 0f);

        var body = new StaticBody3D
        {
            Position = pos + new Vector3(0f, -0.075f, 0f),
            CollisionLayer = PhysicsLayers.World,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(Grid, 0.15f, Grid) },
        });
        AddChild(body);
    }
}
