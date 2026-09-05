using Godot;

namespace LostCrownlike.World;

/// <summary>
/// The Spatial Metric Contract: every level dimension is derived from the
/// player's own movement tunables, never hand-guessed.
///
/// Values mirror PlayerController's [Export] defaults. They are duplicated
/// here rather than read off a live node so a room can be generated before any
/// player exists (editor tooling, tests, async preload) — VerifyAgainst()
/// exists to catch the two drifting apart.
/// </summary>
public sealed class PlayerMetrics
{
    public float JumpHeight = 2.5f;
    public float JumpApexTime = 0.35f;
    public float FallGravityMultiplier = 1.6f;
    public float MoveSpeed = 8f;
    public float AirControlMultiplier = 0.65f;
    public float DashDistance = 4.5f;

    /// Fraction of a theoretical maximum a chunk is allowed to use. Jumps sized
    /// at exactly the limit are frame-perfect and read as unfair; AAA
    /// platformers leave headroom.
    public float SafetyMargin = 0.75f;

    /// Rise time + fall time. Fall is faster because gravity is multiplied
    /// after the apex, so the airtime is NOT simply 2x the apex time.
    public float AirTime => JumpApexTime + JumpApexTime / Mathf.Sqrt(FallGravityMultiplier);

    /// Horizontal reach of a full running jump, landing at takeoff height.
    public float MaxJumpRun => MoveSpeed * AirControlMultiplier * AirTime;

    /// Highest ledge reachable from the ground with one jump.
    public float MaxJumpUp => JumpHeight;

    /// Same, with double jump: the second jump fires at the first apex.
    public float MaxDoubleJumpUp => JumpHeight * 2f;

    /// Widest gap crossable by jumping then dashing across.
    public float MaxJumpDashRun => MaxJumpRun + DashDistance;

    // -- Safe, shippable dimensions -------------------------------------

    public float SafeGap => MaxJumpRun * SafetyMargin;
    public float SafeDashGap => MaxJumpDashRun * SafetyMargin;
    public float SafeStepUp => MaxJumpUp * SafetyMargin;
    /// The player's own height, and the reason a climb step has a FLOOR as well
    /// as a ceiling. The capsule in Main.tscn is 1.6 tall.
    public float PlayerHeight = 1.6f;

    /// Vertical clearance a platform needs above it before a player can stand
    /// on it at all.
    public float MinHeadroom => PlayerHeight + 0.5f;

    /// Climb step, ramped with difficulty as before. Forcing it up to
    /// MinHeadroom looked like the fix for "platforms too close together" and
    /// was not: it flattened the difficulty ramp, making room 0's chimney 2.1
    /// units per step instead of 0.83, and the traversal bot went from crossing
    /// all three layouts to crossing none. The stacking is fixed where it
    /// actually comes from -- the horizontal stagger, see ChimneyStagger.
    public float SafeChimneyStep => MaxJumpUp * SafetyMargin * 0.8f;

    /// How far a chimney ledge steps sideways from the one below.
    ///
    /// It used to be SafeGap * 0.35 = 0.85 units against ledges 3 wide, so
    /// consecutive ledges overlapped by 2.15 and the climb was a stack of
    /// shelves with no standing room between them -- reported from play as
    /// "three platforms one above the other". A full SafeGap clears the ledge
    /// below entirely, so the climb zig-zags instead of stacking, and the jump
    /// it asks for is the one the contract already guarantees.
    /// A full SafeGap was too much: clearing the ledge below is only half the
    /// problem, since the player has to gain height in the same jump. Measured
    /// at SafeGap (2.44) the bot climbed 3.0 of the 4.3 it needed and stalled.
    /// This is the smallest stagger that still leaves no ledge above another.
    public float ChimneyStagger => ChimneyLedgeWidth + 0.2f;

    /// Ledge width, kept just under the stagger so no ledge sits above another.
    /// Narrow enough that a small stagger clears it, wide enough to land on:
    /// the player capsule is 0.8 across.
    public float ChimneyLedgeWidth => 1.8f;

    /// Logs a warning if the live controller has been retuned away from these
    /// numbers, which would silently make generated rooms unbeatable.
    public void VerifyAgainst(Node playerNode)
    {
        if (playerNode == null) return;
        foreach (var (name, mine) in new (string, float)[]
                 {
                     ("JumpHeight", JumpHeight),
                     ("JumpApexTime", JumpApexTime),
                     ("MoveSpeed", MoveSpeed),
                     ("DashDistance", DashDistance),
                     ("AirControlMultiplier", AirControlMultiplier),
                     ("FallGravityMultiplier", FallGravityMultiplier),
                 })
        {
            var live = playerNode.Get(name);
            if (live.VariantType == Variant.Type.Nil) continue;
            float value = live.AsSingle();
            if (!Mathf.IsEqualApprox(value, mine))
                GD.PushWarning($"PlayerMetrics: '{name}' is {value} on the player but {mine} in the level metrics — generated rooms may be unbeatable or trivial.");
        }
    }
}
