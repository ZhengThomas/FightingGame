using System.Runtime.CompilerServices;
using System.Collections.Generic;


// A bunch of containers that make the current state easy to snapshot
// Every field must itself be a value type (structs, ints, enums, bools).

// The mutable per-tick state of the one currently-playing move, as a value type. MoveData is
// immutable and referenced by numeric Id, so this struct carries no references and copies
// cleanly into snapshots.
public struct ActiveMoveState
{
    public int MoveDataId;   // 0 = none; otherwise a MoveData.LookupById id
    public int InstanceId;   // per-attack unique id (PlayerNumber * 1M + FrameCount at press)
    public int Frame;
    public bool HitLanded;
    public bool Blocked;

    public readonly bool HasMove => MoveDataId > 0;
    public readonly MoveData Data => MoveData.LookupById(MoveDataId);

    // Ids start at 1, so an all-zero struct already reads as "no move".
    public static ActiveMoveState None => default;
}

// One live hitbox spawned by a move. 
public struct HitboxInstance
{
    public int HitboxDataId;   // 0 = inactive slot
    public int InstanceId;     // unique per spawn; defender's hitHistory keys off this
    public int Frame;          // 0-based ticks since spawn; expires when Frame >= Data.ActiveDuration
    public bool HitLanded;     // set true when a hit lands (drives combo scaling / cancel eligibility)
    public bool Blocked;       // set true when the landed hit was blocked
    public int FacingAtSpawn;  // +1 (right) / -1 (left) — locked at spawn so the hitbox stays on the same side even if the owner turns around

    public readonly bool IsActive => HitboxDataId > 0;
    public readonly HitboxData Data => HitboxData.LookupById(HitboxDataId);

    // Ids start at 1, so an all-zero struct (and a fresh HitboxSlots) already reads as inactive.
    public static HitboxInstance None => default;
}

// Fixed 8-slot pool of live hitboxes per player, stored inline so the PlayerSnapshot struct
// copies every live hitbox by value.
[InlineArray(Size)]
public struct HitboxSlots
{
    public const int Size = 8;
    private HitboxInstance _element0;
}

// Match-global sim state — the state that lives on MatchManager.
public struct MatchSnapshot
{
    public int FrameCount;
    public int SimFrame;
    public int HitPauseFramesRemaining;
    public int P1xLastFrame;
    public int P2xLastFrame;
    public int FrontPlayerNumber;

    // Round bookkeeping. In here because the sim's next tick depends on all of it 
    public RoundPhase Phase;
    public int PhaseFrame;
    public int P1Wins;
    public int P2Wins;
}

// Per-player sim state — every field on Player that affects the simulation's forward evolution
// and therefore has to be restored to re-run past ticks deterministically. Cached / derived-
// only state (cachedStats, moveList) is deliberately absent — it's rebuildable or immutable.
public struct PlayerSnapshot
{
    public int SimX;
    public int SimY;
    public PhysicsState Physics;
    public PlayerState CurrentState;
    public JumpState Jump;
    public ActiveMoveState CurrentMove;
    public BackdashState BackdashInfo;
    public int DashCooldown;
    public int GroundDashFrame;
    public HitReactionState HitReaction;
    public BlockReactionState BlockReaction;
    public GrabSequenceState GrabSequence;
    public int GrabPartnerNumber;
    public bool SuppressGravity;
    public int ReactionFlashId;
    public HitHistoryBuffer HitHistory;
    public int HitHistoryIndex;
    public FacingDirection LastFacing;
    public int CurrentBufferWindow;
    public ConsumedMarks Marks;
    public HitboxSlots Hitboxes;
    public int Health;
    // Visual only — carried so a rollback restores the animation alongside the sim, but left out
    // of SnapshotHash on purpose (see SnapshotHash).
    public AnimatorState Anim;
}

// Whole-match snapshot for one tick — the unit that lives in MatchManager's rolling history
// buffer. Rollback restore = one assignment per Player and MatchManager (via Restore methods).
public struct Snapshot
{
    public MatchSnapshot Match;
    public PlayerSnapshot P1;
    public PlayerSnapshot P2;
}

// Folds a whole Snapshot down to one number, so two machines running the same inputs can compare
// results per frame and catch a divergence on the frame it happens.
//
// FNV-1a, 32-bit. Seed and Prime are the standard constants; the values themselves aren't special,
// but both peers have to use the same ones. Fields are hashed individually rather than by copying
// the struct's raw bytes, because the padding C# inserts between fields isn't guaranteed to hold
// anything consistent — identical states could hash differently.
//
// EVERY field that steers the simulation has to be mixed in, in the same order on both sides. A
// field left out here doesn't fail loudly; it just stops being checked, and a desync in it slips
// through. Add to PlayerSnapshot above, add here.
//
// One deliberate exception: PlayerSnapshot.Anim. It's visual state, it never feeds back into the
// sim, and machines don't need matching animations — including it would report a cosmetic animator
// bug as a netcode desync.
public static class SnapshotHash
{
    const uint Seed = 2166136261;
    const uint Prime = 16777619;

    static uint Mix(uint h, int value) => (h ^ (uint)value) * Prime;
    static uint Mix(uint h, bool value) => Mix(h, value ? 1 : 0);

    public static uint Of(Snapshot s) => MixPlayer(MixPlayer(MixMatch(Seed, s.Match), s.P1), s.P2);

    static uint MixMatch(uint h, MatchSnapshot m)
    {
        h = Mix(h, m.FrameCount);
        h = Mix(h, m.SimFrame);
        h = Mix(h, m.HitPauseFramesRemaining);
        h = Mix(h, m.P1xLastFrame);
        h = Mix(h, m.P2xLastFrame);
        h = Mix(h, m.FrontPlayerNumber);
        h = Mix(h, (int)m.Phase);
        h = Mix(h, m.PhaseFrame);
        h = Mix(h, m.P1Wins);
        h = Mix(h, m.P2Wins);
        return h;
    }

    static uint MixPlayer(uint h, PlayerSnapshot p)
    {
        h = Mix(h, p.SimX);
        h = Mix(h, p.SimY);

        h = Mix(h, p.Physics.VelocityX);
        h = Mix(h, p.Physics.VelocityY);
        h = Mix(h, p.Physics.PushbackVelocityX);
        h = Mix(h, p.Physics.IsOnFloor);

        h = Mix(h, (int)p.CurrentState);

        h = Mix(h, p.Jump.JumpSquatFrame);
        h = Mix(h, p.Jump.LandingFrame);
        h = Mix(h, p.Jump.LandingDuration);
        h = Mix(h, p.Jump.HasDoubleJump);
        h = Mix(h, p.Jump.HasAirdash);
        h = Mix(h, p.Jump.jumpDirection);
        h = Mix(h, (int)p.Jump.CurrentJumpType);
        h = Mix(h, p.Jump.AirdashFrame);
        h = Mix(h, p.Jump.AirDashDirection);
        h = Mix(h, p.Jump.FramesSinceLastJump);

        h = Mix(h, p.CurrentMove.MoveDataId);
        h = Mix(h, p.CurrentMove.InstanceId);
        h = Mix(h, p.CurrentMove.Frame);
        h = Mix(h, p.CurrentMove.HitLanded);
        h = Mix(h, p.CurrentMove.Blocked);

        h = Mix(h, p.BackdashInfo.BackDashFrame);
        h = Mix(h, p.DashCooldown);
        h = Mix(h, p.GroundDashFrame);

        h = Mix(h, p.HitReaction.Timer.Frame);
        h = Mix(h, p.HitReaction.Timer.Duration);
        h = Mix(h, p.HitReaction.DamageScaleIndex);
        h = Mix(h, p.HitReaction.HitstunScaleIndex);
        h = Mix(h, p.HitReaction.GravityScale);
        h = Mix(h, p.HitReaction.HardKnockdown);

        h = Mix(h, p.BlockReaction.Timer.Frame);
        h = Mix(h, p.BlockReaction.Timer.Duration);
        h = Mix(h, p.GrabSequence.Timer.Frame);
        h = Mix(h, p.GrabSequence.Timer.Duration);
        h = Mix(h, p.GrabPartnerNumber);

        h = Mix(h, p.SuppressGravity);
        h = Mix(h, p.ReactionFlashId);

        for (int i = 0; i < HitHistoryBuffer.Size; i++)
            h = Mix(h, p.HitHistory[i]);
        h = Mix(h, p.HitHistoryIndex);

        h = Mix(h, (int)p.LastFacing);
        h = Mix(h, p.CurrentBufferWindow);

        h = Mix(h, p.Marks.Left);
        h = Mix(h, p.Marks.Right);
        h = Mix(h, p.Marks.Up);
        h = Mix(h, p.Marks.Down);
        h = Mix(h, p.Marks.Jump);
        h = Mix(h, p.Marks.LightAttack);
        h = Mix(h, p.Marks.MediumAttack);
        h = Mix(h, p.Marks.HeavyAttack);
        h = Mix(h, p.Marks.Dash);
        h = Mix(h, p.Marks.Grab);

        for (int i = 0; i < HitboxSlots.Size; i++)
        {
            HitboxInstance hb = p.Hitboxes[i];
            h = Mix(h, hb.HitboxDataId);
            h = Mix(h, hb.InstanceId);
            h = Mix(h, hb.Frame);
            h = Mix(h, hb.HitLanded);
            h = Mix(h, hb.Blocked);
            h = Mix(h, hb.FacingAtSpawn);
        }

        h = Mix(h, p.Health);
        return h;
    }
}

// Fixed 10-slot ring buffer of hit IDs, stored inline in a struct (C# 12 InlineArray) so a
// struct copy snapshots its contents. Replaces the previous `int[]` field on Player, which
// would have shared its backing array across snapshot copies.
[InlineArray(Size)]
public struct HitHistoryBuffer
{
    public const int Size = 10;
    private int _element0; // sole field; InlineArray expands this into Size slots at the layout level
}
