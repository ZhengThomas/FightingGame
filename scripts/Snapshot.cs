using System.Runtime.CompilerServices;
using System.Collections.Generic;


// A bunch of containers that make the current state easy to snapshot
// Every field must itself be a value type (structs, ints, enums, bools).

// The mutable per-tick state of the one currently-playing move, as a value type. Replaces the
// previous ActiveMove class — MoveData now lives elsewhere (immutable, referenced by numeric
// Id) so this struct itself carries no references and copies cleanly into snapshots.
//
// MoveDataId = -1 means "no active move" (previously represented as `currentMove == null`).
public struct ActiveMoveState
{
    public int MoveDataId;   // -1 = none; otherwise index into MoveData.LookupById
    public int InstanceId;   // per-attack unique id (PlayerNumber * 1M + FrameCount at press)
    public int Frame;
    public bool HitLanded;
    public bool Blocked;

    public readonly bool HasMove => MoveDataId >= 0;
    public readonly MoveData Data => MoveData.LookupById(MoveDataId);

    // Sentinel "no move" value. Struct default (all zeros, MoveDataId = 0) would incorrectly
    // point at whichever move is registered as Id 0, so use this explicit factory instead.
    public static ActiveMoveState None => new ActiveMoveState { MoveDataId = -1 };
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
}

// Per-player sim state — every field on Player that affects the simulation's forward evolution
// and therefore has to be restored to re-run past ticks deterministically. Runtime-only caches
// (lastValidHitboxes / lastValidHurtboxes, cachedStats, moveList) are deliberately absent —
// they're derived from other state and rebuild themselves on demand.
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
}

// Whole-match snapshot for one tick — the unit that lives in MatchManager's rolling history
// buffer. Rollback restore = one assignment per Player and MatchManager (via Restore methods).
public struct Snapshot
{
    public MatchSnapshot Match;
    public PlayerSnapshot P1;
    public PlayerSnapshot P2;
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
