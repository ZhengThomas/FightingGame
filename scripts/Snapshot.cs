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
