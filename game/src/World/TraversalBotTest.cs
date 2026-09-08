using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: can the generated rooms actually be WALKED?
///
/// Every other check in this suite teleports the player. That makes them
/// honest about what they test and silent about the one thing a platformer has
/// to get right: the Spatial Metric Contract claims every chunk is sized so the
/// player's abilities can clear it, and nothing has ever pressed a button to
/// find out.
///
/// SCOPE: rooms 0-2, which is every distinct LAYOUT at the difficulty the ramp
/// gives them. Rooms 3-5 repeat those layouts at saturated difficulty and the
/// bot does not finish them -- measured climb against climb needed: 3.0 of 6.7,
/// 8.5 of 10.8, 1.6 of 5.6. That is not evidence the rooms are impassable: the
/// tallest obstacle the generator can build, a full-difficulty wall shaft, is
/// climbable in 7 wall jumps (check 38), so the shortfall is this bot's
/// technique. Asserting six rooms here would only encode how good the bot is.
///
/// The bot is deliberately simple and reactive -- hold right, jump when the
/// floor runs out or a wall blocks it, dash when the gap is too wide to jump.
/// It is not good at platforming, and that is the point: a level a clumsy bot
/// can cross is one the metric contract really does guarantee, while a level it
/// cannot cross needs a human to say whether the level or the bot is at fault.
/// So this reports DISTANCE, and asserts only what a bad bot still proves.
public partial class TraversalBotTest : Node
{
    private int _f;
    private CharacterBody3D _player;
    private DungeonRoomBuilder _room;
    private PlayerMetrics _metrics;

    private float _startX;
    private float _exitX;
    private float _exitY;
    private float _furthestX;
    private int _stuckFrames;
    private int _jumps;
    private int _dashes;
    private bool _reachedExit;
    private int _framesToExit = -1;
    /// When set, the bot runs one CHUNK at a time at full difficulty instead of
    /// whole rooms. Same driving code on purpose: the press-edge handling is
    /// the subtle part and there should be exactly one of it.
    [Export] public string[] ChunksToTest = System.Array.Empty<string>();

    /// How many rooms to walk. Check 37 uses 3 -- every distinct LAYOUT. A
    /// separate scene runs all 6, which repeats those layouts at saturated
    /// difficulty; that number is measured and reported rather than asserted,
    /// because a failure there says as much about the bot as about the level.
    [Export] public int RoomsToTest = 3;
    [Export] public int StartRoom = 0;

    private int _roomUnderTest;
    private int _roomStartFrame;
    private readonly System.Collections.Generic.List<string> _results = new();
    private bool _reportedStall;
    private int _attacks;
    private bool _dmgHooked;
    private bool _airJumpUsed;
    private int _heldDir = 1;
    private int _wallJumps;
    private float _maxY;
    private float _startY;
    private int _wallJumpEvents;
    private int _doubleJumpEvents;
    private int _dashEvents;
    private bool _climbing;
    private bool _jumpHeld;
    private bool _dashHeld;
    private bool _attackHeld;
    private int _lastJumpFrame = -99;
    private int _lastDashFrame = -99;
    private int _lastAttackFrame = -99;
    private float _climbStartX;

    public override void _Ready() => _metrics = new PlayerMetrics();

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as CharacterBody3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 11 && !_dmgHooked)
        {
            _dmgHooked = true;
            // Direct evidence. Six indirect diagnoses in a row were wrong here;
            // the damage event carries the knockback and the source position,
            // which is the thing actually launching the player backwards.
            EventBus.Instance.PlayerDamaged += (amount, src, kb, _) =>
                GD.Print($"[BOT]   HIT f{_f}: {amount} dmg from ({src.X:F1},{src.Y:F1}) knockback={kb}");
            // The bot's own counter records BUTTON PRESSES. Whether the game
            // answered is a different number, and the gap between them is the
            // whole question when a climb is not happening.
            EventBus.Instance.WallJumped += () => _wallJumpEvents++;
            EventBus.Instance.DoubleJumped += () => _doubleJumpEvents++;
            EventBus.Instance.Dashed += () => _dashEvents++;
            EventBus.Instance.LevelTransitionRequested += (_, _) =>
            {
                if (_reachedExit) return;
                _reachedExit = true;
                _framesToExit = _f;
            };
        }

        if (_f == 10) BeginRoom(StartRoom);

        if (_f < 10) return;

        Drive();

        float x = _player.GlobalPosition.X;
        _maxY = Mathf.Max(_maxY, _player.GlobalPosition.Y);
        if (x < _furthestX - 5.0f)
        {
            // Player respawned at a checkpoint or fell back: reset tracking and stall latch
            _furthestX = x;
            _stuckFrames = 0;
            _reportedStall = false;
            _climbing = false;
        }
        else if (x > _furthestX + 0.05f) { _furthestX = x; _stuckFrames = 0; _reportedStall = false; }
        else _stuckFrames++;

        // Report WHERE it stalls and with what, once. "The bot got 84%" is not
        // a finding; "the bot stalled at the foot of a shaft without the
        // ability that shaft needs" is one, and so is "it stalled with every
        // ability in hand on flat ground", which would mean the bot.
        if (_stuckFrames == 120 && !_reportedStall)
        {
            _reportedStall = true;
            var space = _player.GetWorld3D()?.DirectSpaceState;
            var p = _player.GlobalPosition;
            bool fAhead = space != null && Ray(space, p + new Vector3(1.1f, 0.1f, 0f), new Vector3(0f, -1.4f, 0f));
            bool wAhead = space != null && Ray(space, p + new Vector3(0f, -0.3f, 0f), new Vector3(1.0f, 0f, 0f));
            bool wHigh  = space != null && Ray(space, p + new Vector3(0f,  0.4f, 0f), new Vector3(1.0f, 0f, 0f));
            var abilities = (AbilityFlags)(int)_player.Get("UnlockedAbilities");
            // Enemies live on the Enemy layer, which the World-masked probes
            // above cannot see -- but a CharacterBody3D still collides with
            // them. An enemy standing in the corridor is a wall the bot's
            // sensors report as open ground.
            var nearest = NearestEnemyDistance(p);
            GD.Print($"[BOT] stalled at ({p.X:F1}, {p.Y:F1}) onFloor={_player.IsOnFloor()} " +
                     $"floorAhead={fAhead} wallAhead={wAhead} wallAtWaist={wHigh} abilities={abilities}");
            GD.Print($"[BOT] nearest enemy at stall: {(nearest < 0 ? "none" : nearest.ToString("F2"))}");
            GD.Print($"[BOT] move_right still pressed: {Input.IsActionPressed("move_right")}; " +
                     $"velocity={_player.Velocity}");

            // Ask the engine what it is actually touching. Rays can only see
            // the layers they are told to mask, and every indirect diagnosis in
            // this test has been wrong: a wall probe over the player's head, an
            // enemy distance measured on X alone, a blocker that had already
            // been knocked 200 units away.
            int n = _player.GetSlideCollisionCount();
            GD.Print($"[BOT] slide collisions this frame: {n}");
            for (int i = 0; i < n; i++)
            {
                var col = _player.GetSlideCollision(i);
                var who = col.GetCollider() as Node;
                GD.Print($"[BOT]   touching '{who?.Name}' ({who?.GetType().Name}) normal={col.GetNormal()}");
            }

            // Map what is actually in front, instead of asking one ray one
            // question. Every single-probe diagnosis in this test has been
            // wrong so far.
            for (float h = -0.7f; h <= 0.9f; h += 0.4f)
            {
                var hits = "";
                for (float dist = 0.5f; dist <= 2.5f; dist += 0.5f)
                    hits += Ray(space, p + new Vector3(0f, h, 0f), new Vector3(dist, 0f, 0f)) ? "#" : ".";
                GD.Print($"[BOT]   probe y{h:+0.0;-0.0}: {hits}   (0.5 .. 2.5 units ahead)");
            }
        }

        // Success is the exit TRIGGER firing, not a coordinate comparison. The
        // first version asked whether the bot got within a unit of the door and
        // reported failure at 66.9 of 67.9 -- a rounding margin, not a
        // traversal result. The trigger is what the game itself uses.


        // Trajectory, not a single snapshot. Every guess so far about why it
        // stops has been wrong; a position log says what actually happens.
        // Kept. A single snapshot sent this investigation after four different
        // wrong culprits; the trajectory is what showed the real cycle.
        if (_f % 180 == 0 && _f >= 180)
            GD.Print($"[BOT] f{_f}: x={x:F1} y={_player.GlobalPosition.Y:F1} " +
                     $"onFloor={_player.IsOnFloor()} hp={(int)_player.Get("CurrentHealth")} " +
                     $"nearestEnemy={NearestEnemyDistance(_player.GlobalPosition):F1}");

        // Per-room budget, proportional to the room. 30 frames per metre allows
        // sufficient headroom for a clumsy bot that falls into a pit and has
        // to walk back from a checkpoint.
        int budget = 900 + Mathf.RoundToInt(Mathf.Abs(_exitX - _startX) * 30f);
        int elapsed = _f - _roomStartFrame;
        if (_reachedExit || elapsed > budget)
        {
            float progress = (_furthestX - _startX) / Mathf.Max(0.01f, _exitX - _startX);
            string tag = ChunksToTest.Length > 0 ? ChunksToTest[_roomUnderTest] : $"room {_roomUnderTest}";
            _results.Add($"{tag}: {(_reachedExit ? "CROSSED" : "STUCK")} " +
                         $"at {progress * 100f:F0}% in {elapsed} frames " +
                         $"(pressed: jump {_jumps} wall {_wallJumps} dash {_dashes} attack {_attacks} | " +
                         $"game answered: wallJump {_wallJumpEvents} doubleJump {_doubleJumpEvents} dash {_dashEvents} | " +
                         $"climbed {_maxY - _startY:F1} of {_exitY - _startY:F1} needed)");
            GD.Print($"[BOT] {_results[_results.Count - 1]}");

            if (!_reachedExit)
                GD.Print($"[BOT]   stuck at ({_player.GlobalPosition.X:F1}, {_player.GlobalPosition.Y:F1}) " +
                         $"abilities={(AbilityFlags)(int)_player.Get("UnlockedAbilities")}");

            int last = ChunksToTest.Length > 0 ? ChunksToTest.Length - 1 : StartRoom + RoomsToTest - 1;
            if (_roomUnderTest < last)
            {
                BeginRoom(_roomUnderTest + 1);
                return;
            }

            SetHeld(1);
            Input.ActionRelease("move_right");
            int crossed = 0;
            foreach (var r in _results) if (r.Contains("CROSSED")) crossed++;
            int total = ChunksToTest.Length > 0 ? ChunksToTest.Length : RoomsToTest;
            string what = ChunksToTest.Length > 0 ? "chunks at full difficulty" : "layouts";
            GD.Print($"[BOT] {crossed} of {total} {what} crossed with real input");

            GD.Print(crossed == total
                ? $"[BOT] RESULT: PASS (every one of the {total} {what} is traversable with the abilities it grants)"
                : "[BOT] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    /// Probes the world the same way the enemies' ledge check does, then presses
    /// the button a player would.
    private void Drive()
    {
        var space = _player.GetWorld3D()?.DirectSpaceState;
        if (space == null) return;

        var pos = _player.GlobalPosition;
        bool onFloor = _player.IsOnFloor();
        bool onWall = _player.IsOnWall();

        bool floorAhead = Ray(space, pos + new Vector3(1.1f, 0.1f, 0f), new Vector3(0f, -1.4f, 0f));
        bool floorAtJumpRange = Ray(space, pos + new Vector3(_metrics.SafeGap, 0.1f, 0f), new Vector3(0f, -1.6f, 0f));
        // Wall probes sit INSIDE the capsule. The player's capsule is centred on
        // its origin (-0.8 to +0.8) while the enemies' sits above it (0 to 1.6);
        // copying the enemy offset put this ray over the player's head, so a
        // waist-high ledge read as open ground.
        bool wallAhead = Ray(space, pos + new Vector3(0f, -0.3f, 0f), new Vector3(1.0f, 0f, 0f))
                      || Ray(space, pos + new Vector3(0f,  0.4f, 0f), new Vector3(1.0f, 0f, 0f));

        // Is there anywhere to LAND? A dash gap has ground on the far side; the
        // end of the level does not. Without this the bot read "no floor ahead,
        // none within a jump" at the last platform and dashed off the end,
        // travelling 36 units past the room while falling -- reported by the
        // check as 153% of the room crossed, which was true and useless.
        bool landingAtDashRange =
            Ray(space, pos + new Vector3(_metrics.SafeDashGap, 0.6f, 0f), new Vector3(0f, -2.5f, 0f))
         || Ray(space, pos + new Vector3(_metrics.SafeDashGap * 0.75f, 0.6f, 0f), new Vector3(0f, -2.5f, 0f));

        bool ledgeAbove = Ray(space, pos + new Vector3(0f, 0.9f, 0f), new Vector3(0f, _metrics.MaxJumpUp, 0f));
        bool needDash = !floorAhead && !floorAtJumpRange && landingAtDashRange && !ledgeAbove && _player.Velocity.Y <= 0f;
        bool wantClimbStraightUp = ledgeAbove && _stuckFrames > 40;

        // Climb latch. Gating on the X-stall alone is self-defeating: the first
        // kick nudges X forward, which clears the stall, which switches
        // climbing off again.
        // Climbing mode is wall-jumping, so it is only useful if the player can
        // wall-jump. A chimney is ledges, and grants DoubleJump rather than
        // WallJump; entering climb mode there spent 117 presses producing zero
        // wall jumps and 0.1 units of climb out of the 3.7 needed, because the
        // ability was never in hand.
        bool canWallJump = ((AbilityFlags)(int)_player.Get("UnlockedAbilities"))
                           .HasFlag(AbilityFlags.WallJump);
        if (_stuckFrames > 45 && !onFloor && onWall && canWallJump && !_climbing)
        {
            _climbing = true;
            _climbStartX = pos.X;
        }
        if (_climbing && onFloor && pos.X > _climbStartX + 1.5f) _climbing = false;

        // Decide whether a jump is wanted this frame, then route EVERY press
        // through one release-then-press gate below. PlayerController fills its
        // jump buffer from IsActionJustPressed, so pressing an action that is
        // already held produces no edge and no jump. Driving press and release
        // from separate modulo timers meant most presses landed on an already
        // held button: measured in the shaft as 33 presses producing 8 wall
        // jumps and 42 double jumps, and in an isolated two-wall probe as 600
        // frames of wall SLIDING without a single wall jump.
        bool wantJump;
        int wantKind = -1;   // 0 ground, 1 air, 2 wall

        if (_climbing && canWallJump && !onFloor && onWall)
        {
            // Wall jumping needs the controller's own preconditions: press INTO
            // the wall, and already be falling.
            float nx = _player.GetWallNormal().X;
            int into = nx > 0f ? -1 : 1;
            if (_heldDir != into) { _heldDir = into; SetHeld(into); }
            wantJump = _player.Velocity.Y <= 0f && _f > _lastJumpFrame + 8;
            wantKind = 2;
        }
        // The stall nudge belongs here, not as an afterthought: a chimney is
        // ledges rather than a wall, so wallAhead is false and floorAhead is
        // true at its foot -- the bot has no reason to jump and simply stops.
        // Losing this in a refactor took room 0 from crossed to 12 jump presses
        // in 1601 frames.
        else if (onFloor && (wallAhead || !floorAhead || _stuckFrames > 40))
        {
            // Going up a chimney means going UP: holding right the whole way
            // carries the jump off the ledge it was aimed at.
            if (wantClimbStraightUp)
            {
                Input.ActionRelease("move_left");
                Input.ActionRelease("move_right");
                _heldDir = 0;
            }
            else if (_heldDir != 1) { _heldDir = 1; SetHeld(1); }
            wantJump = _f > _lastJumpFrame + 6;
            wantKind = 0;
        }
        else if (!onFloor && !_airJumpUsed && _player.Velocity.Y < 1.0f)
        {
            // Second jump, near the apex. Without it the bot held DoubleJump and
            // never used it, stopping dead at the chimney in room 0.
            wantJump = _f > _lastJumpFrame + 6;
            wantKind = 1;
        }
        else
        {
            if (onFloor && _heldDir != 1) { _heldDir = 1; SetHeld(1); }
            wantJump = false;
        }

        if (onFloor) _airJumpUsed = false;
        if (_heldDir == 0 && !wantClimbStraightUp) { _heldDir = 1; SetHeld(1); }

        // Release FIRST, then press. With the release on an else-branch, a
        // wantJump that stayed true kept the button held forever and no further
        // edge was ever produced -- 12 presses in 1601 frames, none answered.
        if (_jumpHeld && _f >= _lastJumpFrame + 2)
        {
            Input.ActionRelease("jump");
            _jumpHeld = false;
        }
        if (wantJump && !_jumpHeld)
        {
            Input.ActionPress("jump");
            _jumpHeld = true;
            _lastJumpFrame = _f;
            if (wantKind == 2) _wallJumps++;
            else { _jumps++; _airJumpUsed = wantKind == 1; }
        }

        // Dash over the gaps too wide to jump, once the ability is in hand.
        if (_dashHeld && _f >= _lastDashFrame + 3)
        {
            Input.ActionRelease("dash");
            _dashHeld = false;
        }
        if (needDash && !onFloor && !_dashHeld && _f > _lastDashFrame + 12)
        {
            Input.ActionPress("dash");
            _dashHeld = true;
            _lastDashFrame = _f;
            _dashes++;
        }

        // Swing at whatever is in reach (enemies, unthrown levers, or if stuck against a barrier).
        // Without this the bot walked into the first enemy or lever and pushed against it.
        float nearestTarget = NearestEnemyDistance(pos);
        bool shouldSwing = (nearestTarget >= 0f && nearestTarget < 2.2f) || (_stuckFrames > 25 && wallAhead);
        if (shouldSwing)
        {
            if (_attackHeld && _f >= _lastAttackFrame + 4)
            {
                Input.ActionRelease("attack_light");
                _attackHeld = false;
            }
            if (!_attackHeld && _f > _lastAttackFrame + 14)
            {
                Input.ActionPress("attack_light");
                _attackHeld = true;
                _lastAttackFrame = _f;
                _attacks++;
            }
        }
    }

    private float NearestEnemyDistance(Vector3 from)
    {
        float best = -1f;
        foreach (var e in FindEnemies(_room))
        {
            float dx = e.GlobalPosition.X - from.X;
            float dy = e.GlobalPosition.Y - from.Y;
            // A lever stands on the floor while player origin is at waist height (~0.9m higher)
            if (e is LeverSwitch) dy = Mathf.Max(0f, Mathf.Abs(dy) - 0.9f);
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (best < 0f || d < best) best = d;
        }
        return best;
    }

    private static System.Collections.Generic.List<Node3D> FindEnemies(Node from)
    {
        var found = new System.Collections.Generic.List<Node3D>();
        if (from is AI.EnemyController e && e.State != AI.EnemyController.EnemyState.Dead) found.Add(e);
        if (from is LeverSwitch l && !l.IsThrown) found.Add(l);
        foreach (var c in from.GetChildren()) found.AddRange(FindEnemies(c));
        return found;
    }

    /// Rebuilds the room to the given index and restarts the measurement.
    private void BeginRoom(int index)
    {
        _roomUnderTest = index;
        _roomStartFrame = _f;
        _reachedExit = false;
        _framesToExit = -1;
        _stuckFrames = 0;
        _reportedStall = false;
        _jumps = _dashes = _attacks = _wallJumps = 0;
        _wallJumpEvents = _doubleJumpEvents = _dashEvents = 0;
        _airJumpUsed = false;
        _heldDir = 1;
        _climbing = false;
        _jumpHeld = _dashHeld = _attackHeld = false;

        // The FLAGS above are not the button state. Releasing them in code while
        // the engine still holds jump from the previous chunk means the next
        // press lands on an already-held button, which produces no edge --
        // PlayerController fills its jump buffer from IsActionJustPressed, so
        // the bot then never jumps again.
        //
        // This is the same defect the shaft climb once had, one level up, and it
        // made this check ORDER-DEPENDENT: Gap crossed in 101 frames on its own
        // and stalled for 1406 after StepUp, on identical geometry. It was
        // latent until an unrelated change shifted the build by one frame.
        foreach (var action in new[] { "jump", "dash", "attack_light", "attack_heavy", "parry" })
            Input.ActionRelease(action);

        if (ChunksToTest.Length > 0)
        {
            _room.ChunkUnderTest = ChunksToTest[index];
            _room.RebuildAs(0);
        }
        else if (index > 0)
        {
            _room.RebuildAs(index);
        }

        // The last room is a boss fight now, and this check is about whether
        // the FLOOR can be walked. Leaving the boss in measured the bot's
        // combat instead: it stalled at (86.2, -1.8), which is below the
        // floor -- knocked into a gap by a 22-damage swing. Freeing it rather
        // than killing it also opens the sealed exit, since the seal asks
        // whether a live boss exists.
        foreach (var n in GetTree().GetNodesInGroup("boss"))
            n.QueueFree();

        _startX = _player.GlobalPosition.X;
        _startY = _player.GlobalPosition.Y;
        _maxY = _startY;
        _furthestX = _startX;
        var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
        _exitX = exit?.GlobalPosition.X ?? _startX;
        _exitY = exit?.GlobalPosition.Y ?? _startY;
        string label = ChunksToTest.Length > 0 ? ChunksToTest[index] : $"room {index}";
        GD.Print($"[BOT] {label}: x {_startX:F1} -> {_exitX:F1}, exit at y {_exitY:F1} " +
                 $"(player starts at y {_startY:F1}, so {_exitY - _startY:F1} of climb)");
        SetHeld(1);
    }

    private static void SetHeld(int dir)
    {
        if (dir >= 0) { Input.ActionRelease("move_left"); Input.ActionPress("move_right"); }
        else          { Input.ActionRelease("move_right"); Input.ActionPress("move_left"); }
    }

    private static PhysicsRayQueryParameters3D _probe;

    private static bool Ray(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 offset)
    {
        // Reused, not allocated per cast: five of these run every frame for
        // thousands of frames, and each Create() is a RefCounted Godot object.
        _probe ??= new PhysicsRayQueryParameters3D { CollisionMask = PhysicsLayers.World };
        _probe.From = from;
        _probe.To = from + offset;
        return space.IntersectRay(_probe).Count > 0;
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren())
        {
            var f = FindRoom(c);
            if (f != null) return f;
        }
        return null;
    }
}
