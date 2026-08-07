using System;
using System.Collections.Generic;
using Godot;

public enum FacingDirection
{
    Right = 1,
    Left = -1
}
public struct PhysicsState
{
    public int VelocityX;
    public int VelocityY;
    // Separate from movement/knockback. Set by hit/block pushback; transfers to the
    // attacker in the corner; cleared on jump/airdash cancels.
    public int PushbackVelocityX;
    public bool IsOnFloor;
}

public enum JumpType
{
    Normal,
    Dash,
    Combo,
    Airdash,
}
public struct JumpState 
{
    public int JumpSquatFrame;    // -1 = not in jumpsquat
    public int LandingFrame;      // -1 = not landing
    public int LandingDuration;
    public bool HasDoubleJump;
    public bool HasAirdash;
    public int jumpDirection;     // -1 for left, 1 for right, 0 for up.
    public JumpType CurrentJumpType;
    public int AirdashFrame;      // -1 = not airdashing
    public int AirDashDirection;
    public int FramesSinceLastJump; 
}
// Global, match-wide constants. Per-character tunables (speeds, gravity, jump forces, dash
// timings, knockdown/wakeup, etc.) live in CharacterStats and are read via Player.Stats.
public static class PlayerConstants
{
    // Fixed-point scale: sim units = world floats * PhysicsScale. Divide only when
    // writing Godot Node3D.Position for rendering — never in gameplay math.
    public const int PhysicsScale = 10000;
    public const int FloorY = -8000;
    public const int BufferWindow = 6;
    public const int CancelBufferWindow = 8;          // slightly more lenient during cancels
    public const int DashInputLeniency = 10;          // time between two button presses for a dash
    public const int MaxPushboxCorrectionPerFrame = 2000;
    public const int MaxPlayerSeparation = 60000;      // max distance between the two players
    public const int MaxDistanceFromCenter = 70000;    // max distance either player can be from the midpoint
    public const int CrossupProtectionWindow = 3;     // frames either direction counts as back after a crossup
    public const int AirBlockstunLandingPenalty = 15; // extra landing frames when touching down during air blockstun
    public const float FixedDelta = 0.01666f; // seconds-per-tick for animation only (not used by physics)
    public const int WallCornerInset = 2000; // keep the non-cornered player slightly off the wall

    public static int ToSim(float world) => (int)Math.Round(world * PhysicsScale);
    public static float ToWorld(int sim) => sim / (float)PhysicsScale;
}

public enum PlayerState
{
    Idle,
    Walking,
    JumpSquat,
    Jumping,
    Landing,
    Airdashing,
    GroundDashing,
    GroundDashEnd,
    Backdashing,
    Crouching,
    StandAttacking,
    AirAttacking,
    CrouchAttacking,
    Hitstun,        // grounded hit reaction
    AirHitstun,     // airborne hit reaction / juggle
    SoftKnockdown,  // short grounded recover after landing from AirHitstun (default)
    Knockdown,      // hard knockdown lie-down (throws / flagged moves)
    Wakeup,         // rising animation after hard Knockdown before returning to Idle
    Blockstun,      // in blockstun after blocking an attack
    CrouchBlockstun,
    AirBlockstun,
    GrabOccurring,  // attacker tryna grab
    GrabHit,        // attacker did hit grab, now playing out the animation
    HitByGrab,      // defender got hit by some grab
}
public struct BackdashState
{
    public int BackDashFrame; // -1 means not backdashing
}

// Simple count-up timer. Start(duration) when entering a timed state, then Advance() once per tick;
// Advance returns true on the tick the timer finishes. It's pure data (two ints), so it snapshots
// and restores cleanly for rollback, and there are no captured callbacks to worry about.
public struct FrameTimer
{
    public int Frame;     // counts up each tick
    public int Duration;  // total frames for the current phase

    public void Start(int duration) { Frame = 0; Duration = duration; }
    public bool Advance() { Frame++; return Frame >= Duration; }
    public readonly bool Done => Frame >= Duration;
}

public struct HitReactionState
{
    public FrameTimer Timer;
    // Indices into ComboScaling tables for the next hit (0 = first hit of the combo).
    public int DamageScaleIndex;
    public int HitstunScaleIndex;
    // Gravity scale in thousandths from the last connecting hit (1000 = normal).
    public int GravityScale;
    public bool HardKnockdown;
}

public struct BlockReactionState
{
    public FrameTimer Timer;
}

public struct GrabSequenceState
{
    public FrameTimer Timer;
}

// Meant to be a hitbox or hurtbox or whatever. All fields are in sim units
// (world * PlayerConstants.PhysicsScale). Width and Height are HALF-extents:
// the box spans [X - Width, X + Width] horizontally and [Y - Height, Y + Height] vertically.
public struct Box
{
    public int X;       // offset from player center, sim units
    public int Y;
    public int Width;   // half-width, sim units
    public int Height;  // half-height, sim units
}

public class Move
{
    public string Name;
    public System.Func<InputView, bool> Condition;
    public System.Action Execute;
}

public partial class Player : Node3D
{
    [Export] public bool ShowDebug = true;
    [Export] public int PlayerNumber = 1;
    public const int MaxHealth = 1000;
    public int Health;
    // Durable per-tick record of this player's raw inputs, indexed by absolute FrameCount.
    // Written once per real physics frame by whoever considers this player local (currently
    // always InputManager). Any re-simulation reads back from here — never from live keys.
    public InputLog InputLog { get; private set; }
    // Per-button "last consumed frame" table. SIM STATE, not input — set by MarkConsumed when
    // a move fires so the same press can't re-trigger, and any older press of the same button
    // becomes implicitly consumed too. Struct field (not property) so InputView.SetMarkFor can
    // mutate in place. Snapshots by struct copy along with the rest of Player state.
    public ConsumedMarks Marks;
    // Sim-facing view constructed on demand — bundles the log, this Player (to reach Marks), the
    // match's per-frame hit-pause record, and the current tick so extension methods can address
    // inputs by "i frames ago" while everything underneath is absolute-frame-indexed.
    public InputView Inputs => new InputView { Log = InputLog, HitPause = Match.HitPauseHistory, Owner = this, CurrentFrame = Match.FrameCount };
    public PhysicsState physics;
    // Sim-space position (PhysicsScale units). Source of truth for gameplay.
    public int SimX;
    public int SimY;
    PlayerState currentState = PlayerState.Idle;
    JumpState jump = new JumpState { JumpSquatFrame = -1, AirdashFrame = -1, LandingFrame = -1 };
    ActiveMoveState currentMove = ActiveMoveState.None;
    // Fixed-size pool of live hitboxes this player has spawned. Each slot is either inactive
    // (HitboxDataId = 0) or a live HitboxInstance with its own Frame counter and ActiveDuration.
    HitboxSlots hitboxes;

    public PlayerState CurrentState => currentState;
    public ActiveMoveState CurrentMove => currentMove;
    public PhysicsState Physics => physics;
    public int AirDashDirection => jump.AirDashDirection; // +1/-1, the locked-in airdash direction
    // Bumped every hit/block so the animator can replay reaction clips on re-hit in the same state.
    // Snapshotted for rollback; visuals poll this instead of listening to transition events.
    public int ReactionFlashId => reactionFlashId;
    BackdashState backdashInfo = new BackdashState{ BackDashFrame = -1 };
    int dashCooldown = 0; // counts down each tick; while > 0, forward dash and backdash are blocked
    int groundDashFrame = -1; // ticks spent in the current ground dash (-1 = not dashing); gates the walk/idle exit
    HitReactionState hitReaction;
    BlockReactionState blockReaction;
    GrabSequenceState grabSequence;
    // The other player during GrabHit / HitByGrab. Stored as player number
    int grabPartnerNumber = 0;
    Player GrabPartner => Match?.PlayerFromNumber(grabPartnerNumber);
    MatchManager Match;
    public bool SuppressGravity = false;
    int reactionFlashId;

    List<Move> moveList;
    public Box Pushbox = new Box { X = 0, Y = 7000, Width = 3000, Height = 9000 };
    static readonly List<Box> StandingHurtbox = new List<Box>{ new Box { X = 0, Y = 10000, Width = 5000, Height = 10000 } };
    static readonly List<Box> CrouchingHurtbox = new List<Box>{new Box { X = 0, Y = 5000, Width = 5000, Height = 7500 }};
    static readonly List<Box> AirborneHurtbox = new List<Box>{new Box { X = 0, Y = 10000, Width = 5000, Height = 10000 }};

    // stores 10 most recently received hit ids. Inline value-type ring so a snapshot's struct
    // copy captures every slot (a plain int[] would share the array reference across snapshots).
    HitHistoryBuffer hitHistory;
    int hitHistoryIndex = 0;
    FacingDirection lastFacing;
    int currentBufferWindow = PlayerConstants.BufferWindow; // depends on if were cancelling or not, our current state

    // Per-character tunable stats. Lazily built from CreateStats() so subclasses can supply
    // their own values without depending on _Ready ordering. Override CreateStats() to change them.
    private CharacterStats? cachedStats;
    protected CharacterStats Stats => cachedStats ??= CreateStats();
    protected virtual CharacterStats CreateStats() => CharacterStats.Default;

    public override void _Ready()
    {
        InputLog = new InputLog(InputManager.BufferSize);
        Marks = ConsumedMarks.Empty;

        Match = GetNode<MatchManager>("/root/MatchManager");
        Match.RegisterPlayer(this, PlayerNumber);

        // Scene placement is in world floats; bake into sim ints once.
        SimX = PlayerConstants.ToSim(Position.X);
        SimY = PlayerConstants.ToSim(Position.Y);
        Health = MaxHealth;
        SyncVisualPosition();

        lastFacing = PlayerNumber == 1 ? FacingDirection.Right : FacingDirection.Left;
        InitializeMoveList();
    }

    // Push sim coords to Godot for rendering only.
    public void SyncVisualPosition()
    {
        Position = new Vector3(PlayerConstants.ToWorld(SimX), PlayerConstants.ToWorld(SimY), Position.Z);
    }

    protected virtual void InitializeMoveList()
    {
        moveList = new List<Move>
        {
            new Move
            {
                Name = "CrouchingLight",
                Condition = (input) => physics.IsOnFloor && input.WasJustPressed(currentBufferWindow, Button.LightAttack, [Button.Down]) != null && CanCancelInto(MoveType.CrouchLight),
                Execute = () => StartMove(Moveset.CrouchingLight, PlayerState.CrouchAttacking),
            },
            new Move
            {
                Name = "AirLight",
                Condition = (input) => IsAirborneState(currentState) && input.WasJustPressed(currentBufferWindow, Button.LightAttack) != null && CanCancelInto(MoveType.Light),
                Execute = () => StartMove(Moveset.AirLight, PlayerState.AirAttacking),
            },
            new Move
            {
                Name = "StandingLight",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, Button.LightAttack, null, [Button.Down]) != null && CanCancelInto(MoveType.Light),
                Execute = () => StartMove(Moveset.StandingLight, PlayerState.StandAttacking),
            },
            new Move
            {
                Name = "CrouchingMedium",
                Condition = (input) => physics.IsOnFloor && input.WasJustPressed(currentBufferWindow, Button.MediumAttack, [Button.Down]) != null && CanCancelInto(MoveType.CrouchMedium),
                Execute = () => StartMove(Moveset.CrouchingMedium, PlayerState.CrouchAttacking),
            },
            new Move
            {
                Name = "AirMedium",
                Condition = (input) => IsAirborneState(currentState) && input.WasJustPressed(currentBufferWindow, Button.MediumAttack) != null && CanCancelInto(MoveType.Medium),
                Execute = () => StartMove(Moveset.AirMedium, PlayerState.AirAttacking),
            },
            new Move
            {
                Name = "StandingMedium",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, Button.MediumAttack, null, [Button.Down]) != null && CanCancelInto(MoveType.Medium),
                Execute = () => StartMove(Moveset.StandingMedium, PlayerState.StandAttacking),
            },
            new Move
            {
                Name = "CrouchingHeavy",
                Condition = (input) => physics.IsOnFloor && input.WasJustPressed(currentBufferWindow, Button.HeavyAttack, [Button.Down]) != null && CanCancelInto(MoveType.CrouchHeavy),
                Execute = () => StartMove(Moveset.CrouchingHeavy, PlayerState.CrouchAttacking),
            },
            new Move
            {
                Name = "AirHeavy",
                Condition = (input) => IsAirborneState(currentState) && input.WasJustPressed(currentBufferWindow, Button.HeavyAttack) != null && CanCancelInto(MoveType.Heavy),
                Execute = () => StartMove(Moveset.AirHeavy, PlayerState.AirAttacking),
            },
            new Move
            {
                Name = "StandingHeavy",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, Button.HeavyAttack, null, [Button.Down]) != null && CanCancelInto(MoveType.Heavy),
                Execute = () => StartMove(Moveset.StandingHeavy, PlayerState.StandAttacking),
            },
            new Move
            {
                Name = "BackThrow",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, Button.Grab, [GetFacing() == FacingDirection.Right ? Button.Left : Button.Right]) != null && CanCancelInto(MoveType.Grab) && CanUseGrab(),
                Execute = () => StartMove(Moveset.BackGrab, PlayerState.GrabOccurring),
            },
            new Move
            {
                Name = "FowardThrow",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, Button.Grab) != null && CanCancelInto(MoveType.Grab) && CanUseGrab(),
                Execute = () => StartMove(Moveset.FowardGrab, PlayerState.GrabOccurring),
            },
        };
    }

    // Pack every sim-critical field on this Player into a PlayerSnapshot for rollback storage.
    // Rebuildable / immutable state (moveList, cachedStats) is deliberately absent.
    public PlayerSnapshot CaptureSnapshot() => new PlayerSnapshot
    {
        SimX = SimX,
        SimY = SimY,
        Physics = physics,
        CurrentState = currentState,
        Jump = jump,
        CurrentMove = currentMove,
        BackdashInfo = backdashInfo,
        DashCooldown = dashCooldown,
        GroundDashFrame = groundDashFrame,
        HitReaction = hitReaction,
        BlockReaction = blockReaction,
        GrabSequence = grabSequence,
        GrabPartnerNumber = grabPartnerNumber,
        SuppressGravity = SuppressGravity,
        ReactionFlashId = reactionFlashId,
        HitHistory = hitHistory,
        HitHistoryIndex = hitHistoryIndex,
        LastFacing = lastFacing,
        CurrentBufferWindow = currentBufferWindow,
        Marks = Marks,
        Hitboxes = hitboxes,
        Health = Health,
    };

    // Overwrite every sim-critical field on this Player from a PlayerSnapshot. Also blanks the
    // rebuildable caches so a stale list from before the restore point can't leak into the
    // first GetCurrentHitboxes / GetCurrentHurtboxes call after restore.
    public void RestoreSnapshot(PlayerSnapshot s)
    {
        SimX = s.SimX;
        SimY = s.SimY;
        physics = s.Physics;
        currentState = s.CurrentState;
        jump = s.Jump;
        currentMove = s.CurrentMove;
        backdashInfo = s.BackdashInfo;
        dashCooldown = s.DashCooldown;
        groundDashFrame = s.GroundDashFrame;
        hitReaction = s.HitReaction;
        blockReaction = s.BlockReaction;
        grabSequence = s.GrabSequence;
        grabPartnerNumber = s.GrabPartnerNumber;
        SuppressGravity = s.SuppressGravity;
        reactionFlashId = s.ReactionFlashId;
        hitHistory = s.HitHistory;
        hitHistoryIndex = s.HitHistoryIndex;
        lastFacing = s.LastFacing;
        currentBufferWindow = s.CurrentBufferWindow;
        Marks = s.Marks;
        hitboxes = s.Hitboxes;
        Health = s.Health;
        SyncVisualPosition();
    }

    public bool WasAlreadyHitBy(int attackId)
    {
        for (int i = 0; i < HitHistoryBuffer.Size; i++)
            if (hitHistory[i] == attackId)
                return true;
        return false;
    }

    // Subtract damage from Health, clamped at 0. Grabs call this manually from their sequence
    // handler so the damage tick can be timed to the animation.
    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;
        Health = Math.Max(0, Health - amount);
    }

    public virtual bool TakeHit(HitboxData data, int attackId, int direction, Player attacker)
    {
        hitHistory[hitHistoryIndex] = attackId;
        hitHistoryIndex = (hitHistoryIndex + 1) % HitHistoryBuffer.Size;

        // Before we actually get hurt, instead first check if we block the hit.
        if (CanBlock(currentState) && IsHoldingBackForBlock(attacker) && IsHoldingHighLowForBlock(data))
        {
            BlockAttack(data, direction);
            return true;
        }

        // Starter hit: reset combo scaling. Combo continuations (already IsHurt) preserve it.
        if (!IsHurt())
            ResetComboScaling();

        int hitstunScale = ComboScaling.Sample(ComboScaling.HitstunScale, hitReaction.HitstunScaleIndex);
        hitReaction.GravityScale = ComboScaling.GravityFromHitstun(hitstunScale);

        int baseHitstun = data.HitStun > 0 ? data.HitStun : 15;
        hitReaction.Timer.Duration = ComboScaling.Apply(baseHitstun, hitstunScale);
        int damageScale = ComboScaling.Sample(ComboScaling.DamageScale, hitReaction.DamageScaleIndex);
        TakeDamage(ComboScaling.Apply(data.Damage, damageScale));
        // Last hit wins: only hitboxes tagged CausesHardKnockdown force the long KD on landing.
        hitReaction.HardKnockdown = data.CausesHardKnockdown;
        reactionFlashId++;

        hitReaction.DamageScaleIndex += data.DamageProrationSteps;
        hitReaction.HitstunScaleIndex += data.HitstunProrationSteps;

        int pushX = data.Pushback * direction;
        physics.VelocityX = 0;
        physics.PushbackVelocityX = pushX;

        if (data.LaunchesOpponent || !physics.IsOnFloor)
        {
            TransitionTo(PlayerState.AirHitstun);
            physics.VelocityY = data.LaunchForce;
        }
        else
        {
            TransitionTo(PlayerState.Hitstun);
        }

        return false;
    }

    public virtual void BlockAttack(HitboxData data, int direction)
    {
        blockReaction.Timer.Duration = data.BlockStun > 0 ? data.BlockStun : 15;
        reactionFlashId++;
        int push = data.PushbackOnBlock == 0 ? data.Pushback : data.PushbackOnBlock;
        physics.VelocityX = 0;
        physics.PushbackVelocityX = push * direction;

        if(!physics.IsOnFloor)
        {
            physics.VelocityY = data.LaunchForceOnBlock == 0 ? data.LaunchForce : data.LaunchForceOnBlock;
            TransitionTo(PlayerState.AirBlockstun);
            return;
        }
        else if (Inputs[0].Down)
        {
            TransitionTo(PlayerState.CrouchBlockstun);
            return;
        }
        else
        {
            TransitionTo(PlayerState.Blockstun);
            return;
        }
    }

    protected virtual void StartMove(MoveData moveData, PlayerState attackingState)
    {
        // Mark the press that started this move in the consumed buffer so it can't be reused.
        switch (moveData.Strength)
        {
            case MoveType.Light:
            case MoveType.CrouchLight:
                Inputs.MarkConsumed(currentBufferWindow, Button.LightAttack);
                break;
            case MoveType.Medium:
            case MoveType.CrouchMedium:
                Inputs.MarkConsumed(currentBufferWindow, Button.MediumAttack);
                break;
            case MoveType.Heavy:
            case MoveType.CrouchHeavy:
                Inputs.MarkConsumed(currentBufferWindow, Button.HeavyAttack);
                break;
            case MoveType.Grab:
                Inputs.MarkConsumed(currentBufferWindow, Button.Grab);
                break;
        }

        TransitionTo(attackingState);
        // The attacker (and its slash VFX) draws over the opponent for the duration of the move,
        // like GGST — not just from the moment a hit connects.
        Match.BringToFront(this);
        // ID is derived from frame count and player number, like hashing but lazy
        // could overflow if 2 billion frames pass, which is unlikely and i will ignore such a possibility
        int id = PlayerNumber * 1000000 + Match.FrameCount;
        // Frame starts at 1 so the press tick counts as startup frame 1 (Frame is not incremented
        // that same tick — ProcessCurrentState already ran before StartMove).
        currentMove = new ActiveMoveState { MoveDataId = moveData.Id, InstanceId = id, Frame = 1 };
    }

    public bool IsAttacking()
    {
        return currentState == PlayerState.AirAttacking || currentState == PlayerState.StandAttacking || currentState == PlayerState.CrouchAttacking;
    }

    public static bool IsAirborneState(PlayerState state)
    {
        return state == PlayerState.Jumping || state == PlayerState.Airdashing || state == PlayerState.AirAttacking || state == PlayerState.AirHitstun;
    }

    public bool IsHurt()
    {
        return currentState == PlayerState.Hitstun
            || currentState == PlayerState.AirHitstun
            || currentState == PlayerState.SoftKnockdown
            || currentState == PlayerState.Knockdown
            || currentState == PlayerState.Wakeup;
    }

    public bool IsBlockingState()
    {
        return currentState == PlayerState.Blockstun
            || currentState == PlayerState.CrouchBlockstun
            || currentState == PlayerState.AirBlockstun;
    }

    public int PushbackVelocityX => physics.PushbackVelocityX;

    public void AddPushbackVelocity(int x)
    {
        physics.PushbackVelocityX += x;
    }

    public void ClearPushbackVelocity()
    {
        physics.PushbackVelocityX = 0;
    }

    public bool CanUseGrab()
    {
        return currentState == PlayerState.Idle
            || currentState == PlayerState.Walking
            || currentState == PlayerState.Crouching
            || currentState == PlayerState.GroundDashing;
    }

    // Movement API used by the attacker to drive the defender during a grab sequence.
    public void SetVelocity(int x, int y)
    {
        physics.VelocityX = x;
        physics.VelocityY = y;
    }

    public void AddVelocity(int x, int y)
    {
        physics.VelocityX += x;
        physics.VelocityY += y;
    }

    public void SnapToSim(int x, int y)
    {
        SimX = x;
        SimY = y;
        physics.VelocityX = 0;
        physics.VelocityY = 0;
        physics.PushbackVelocityX = 0;
        SyncVisualPosition();
    }

    public void FlipFacing()
    {
        lastFacing = lastFacing == FacingDirection.Right ? FacingDirection.Left : FacingDirection.Right;
    }

    // Called by MatchManager when a grab hitbox connects.
    // Sets this player up as the attacker for the grab cinematic.
    public void RegisterGrab(Player defender, HitboxData grabHb)
    {
        grabPartnerNumber = defender.PlayerNumber;
        grabSequence.Timer.Duration = grabHb.GrabSequenceDuration;

        if (grabHb.IsBackThrow)
            FlipFacing();

        // Snap the defender to the inital grab position. If the desired snap would put the
        // defender past the wall, clamp them at the wall and shift the attacker by the same amount
        int facingMult = GetFacing() == FacingDirection.Right ? 1 : -1;
        int desiredDefenderX = SimX + grabHb.DefenderSnapOffsetX * facingMult;
        int desiredDefenderY = SimY + grabHb.DefenderSnapOffsetY;

        int wall = PlayerConstants.MaxDistanceFromCenter;
        int clampedDefenderX = Math.Clamp(desiredDefenderX, -wall, wall);
        int attackerShift = clampedDefenderX - desiredDefenderX;
        if (attackerShift != 0)
        {
            SimX += attackerShift;
            SyncVisualPosition();
        }

        defender.SnapToSim(clampedDefenderX, desiredDefenderY);
        TransitionTo(PlayerState.GrabHit);
    }

    // Called by MatchManager when this player is caught by a grab.
    // Snaps the defender into position and hands control to the attacker.
    public void GetGrabbed(HitboxData grabHb, int attackId, Player attacker)
    {
        hitHistory[hitHistoryIndex] = attackId;
        hitHistoryIndex = (hitHistoryIndex + 1) % HitHistoryBuffer.Size;

        grabPartnerNumber = attacker.PlayerNumber;
        grabSequence.Timer.Duration = grabHb.GrabSequenceDuration;

        // Starter grab: reset combo scaling. Combo grabs (grabbed while already hurt) preserve
        // the running scaling from prior hits. This mirrors TakeHit's convention.
        if (!IsHurt())
            ResetComboScaling();
        hitReaction.HardKnockdown = grabHb.CausesHardKnockdown;

        TransitionTo(PlayerState.HitByGrab);
    }

    // Fresh-combo defaults for the running scaling state. Called from every entry point into a
    // hurt/grabbed state that starts a new combo 
    protected void ResetComboScaling()
    {
        hitReaction.DamageScaleIndex = 0;
        hitReaction.HitstunScaleIndex = 0;
        hitReaction.GravityScale = ComboScaling.ScaleUnit;
        hitReaction.HardKnockdown = false;
    }

    // Returns true if the given state is one where the player is allowed to block.
    public static bool CanBlock(PlayerState state)
    {
        return state == PlayerState.Idle
            || state == PlayerState.Walking
            || state == PlayerState.Crouching
            || state == PlayerState.GroundDashing
            || state == PlayerState.Landing
            || state == PlayerState.Jumping          // air block
            || state == PlayerState.Blockstun        // block strings: can re-block during blockstun
            || state == PlayerState.CrouchBlockstun
            || state == PlayerState.AirBlockstun;
    }

    // Returns true if the player is holding back relative to the attacker's current position
    // Also checks for corssup protection window
    public bool IsHoldingBackForBlock(Player attacker)
    {
        bool attackerIsToRight = attacker.SimX > SimX;

        // If were currently correctly holding away from the attacker, we are holding back
        bool holdingBackNow = attackerIsToRight ? Inputs[0].Left : Inputs[0].Right;
        if (holdingBackNow) return true;

        // If in the last bit of time we crossed up, we are always holding back if were not holding neutral
        bool holdingNeutral = !Inputs[0].Left && !Inputs[0].Right;
        if (holdingNeutral) return false;
        for (int i = 0; i <= PlayerConstants.CrossupProtectionWindow; i++)
        {
            // Early in a match the buffer doesn't reach back this far — no history, no crossup.
            if (!Match.HasGameState(i)) break;
            (int ax, int dx) = Match.GetHistoricalSimX(attacker, this, i);
            bool attackerWasToRight = ax > dx;

            // Since were not holding neutral and there was a crossup, we are blocking regardless of 
            // What direction we are currently holding
            if (attackerWasToRight != attackerIsToRight) return true;
        }

        // We are not holding back after all the checks
        return false;
    }

    // Returns true if the player's current blocking stance covers the attack's type
    protected virtual bool IsHoldingHighLowForBlock(HitboxData data)
    {
        if (IsAirborneState(currentState)) return true; // airblocks always work

        if (data.IsLow)
            return Inputs[0].Down;

        if (data.IsOverhead)
            return !Inputs[0].Down;  

        return true;              // mid are always blocked
    }

    // cached empty list so we're not allocating a new one every frame
    static readonly List<Box> emptyBoxList = new List<Box>();

    // Walk backward through a per-frame box-list array starting at `index` (clamped into range)
    // and return the first non-null entry.
    public static List<Box> LookBackForBoxes(List<Box>[] perFrame, int index)
    {
        if (perFrame == null || perFrame.Length == 0) return null;
        int start = System.Math.Min(index, perFrame.Length - 1);
        for (int i = start; i >= 0; i--)
            if (perFrame[i] != null) return perFrame[i];
        return null;
    }

    // Insert a hitbox for `data` into the first inactive slot. Returns the assigned InstanceId,
    // or -1 if all slots are occupied (warning printed so silent drops don't hide themselves).
    public int SpawnHitbox(HitboxData data, int facingMult)
    {
        if (data == null) return -1;
        // InstanceId derives from FrameCount + PlayerNumber + slot so it's stable across
        // rollback resim (same tick → same id) and unique among live hitboxes.
        int frameCount = Match?.FrameCount ?? 0;
        for (int i = 0; i < HitboxSlots.Size; i++)
        {
            if (!hitboxes[i].IsActive)
            {
                int instanceId = PlayerNumber * 10_000_000 + frameCount * 10 + i;
                hitboxes[i] = new HitboxInstance
                {
                    HitboxDataId = data.Id,
                    InstanceId = instanceId,
                    Frame = 0,
                    HitLanded = false,
                    Blocked = false,
                    FacingAtSpawn = facingMult,
                };
                return instanceId;
            }
        }
        GD.PushWarning($"Player {PlayerNumber} could not spawn hitbox (id {data.Id}) — all {HitboxSlots.Size} slots occupied.");
        return -1;
    }

    void SpawnScheduledHitboxes()
    {
        MoveData data = currentMove.Data;
        if (data == null || data.HitboxSpawns == null) return;
        int facingMult = GetFacing() == FacingDirection.Right ? 1 : -1;
        foreach (HitboxSpawn spawn in data.HitboxSpawns)
        {
            if (spawn.Frame == currentMove.Frame && spawn.Hitbox != null)
                SpawnHitbox(spawn.Hitbox, facingMult);
        }
    }

    // Advance every active hitbox's Frame by one, then expire any whose Frame has run past its ActiveDuration.
    void AdvanceHitboxes()
    {
        for (int i = 0; i < HitboxSlots.Size; i++)
        {
            if (!hitboxes[i].IsActive) continue;
            hitboxes[i].Frame++;
            HitboxData data = hitboxes[i].Data;
            if (data == null || hitboxes[i].Frame >= data.ActiveDuration)
                hitboxes[i] = HitboxInstance.None;
        }
    }

    public IEnumerable<HitboxInstance> GetActiveHitboxes()
    {
        for (int i = 0; i < HitboxSlots.Size; i++)
            if (hitboxes[i].IsActive) yield return hitboxes[i];
    }

    // Mark the hitbox with the given InstanceId as having landed. Only touches the hitbox slot.
    public void RegisterHitboxLanded(int instanceId, bool blocked)
    {
        for (int i = 0; i < HitboxSlots.Size; i++)
        {
            if (hitboxes[i].IsActive && hitboxes[i].InstanceId == instanceId)
            {
                hitboxes[i].HitLanded = true;
                hitboxes[i].Blocked = blocked;
                return;
            }
        }
    }

    // Mark the current move as having landed a hit. CanCancelInto / TryHitCancel key off of this.
    public void MarkCurrentMoveLanded(bool blocked)
    {
        if (!currentMove.HasMove) return;
        currentMove.HitLanded = true;
        currentMove.Blocked = blocked;
    }

    // Per-frame hurtbox override for the current move's current frame, or null if none.
    // Frame is 1-based (press tick = 1); the array is 0-based over the move's lifetime.
    List<Box> GetCurrentHurtboxOverride()
    {
        if (!currentMove.HasMove) return null;
        MoveData data = currentMove.Data;
        if (data == null) return null;
        return LookBackForBoxes(data.HurtboxesPerFrame, currentMove.Frame - 1);
    }

    public virtual List<Box> GetCurrentHurtboxes()
    {
        // Invulnerable while knocked down / waking up
        if (currentState == PlayerState.SoftKnockdown
            || currentState == PlayerState.Knockdown
            || currentState == PlayerState.Wakeup)
        {
            return emptyBoxList;
        }
        // Both players are invulnerable for the duration of the grab cinematic
        if (currentState == PlayerState.GrabHit || currentState == PlayerState.HitByGrab)
        {
            return emptyBoxList;
        }
        // If were attacking, its based on our attack
        if (IsAttacking() && currentMove.HasMove)
            return GetCurrentHurtboxOverride() ?? GetDefaultHurtbox();
        // Otherwise, its based on our current state, crouching/idle/jumping/etc.
        return GetDefaultHurtbox();
    }
    protected virtual List<Box> GetDefaultHurtbox()
    {
        if (currentState == PlayerState.Crouching || currentState == PlayerState.CrouchAttacking || currentState == PlayerState.CrouchBlockstun)
            return CrouchingHurtbox;
        if (!physics.IsOnFloor)
            return AirborneHurtbox;
        return StandingHurtbox;
    }

    public virtual void updateFacing(bool forceOverride = false)
    {   
        // Cant turn around in certain states
        if ((currentState == PlayerState.GroundDashing ||
            currentState == PlayerState.Backdashing ||
            currentState == PlayerState.Jumping ||
            currentState == PlayerState.AirAttacking ||
            currentState == PlayerState.Airdashing ||
            currentState == PlayerState.StandAttacking ||
            currentState == PlayerState.CrouchAttacking ||
            currentState == PlayerState.GrabOccurring ||
            currentState == PlayerState.SoftKnockdown ||
            currentState == PlayerState.Knockdown || 
            currentState == PlayerState.Wakeup)
            && !forceOverride)
        {
            return;
        }

        Player opponent = Match.Player1 == this ? Match.Player2 : Match.Player1;
        if (opponent == null) return;

        lastFacing = opponent.SimX > SimX ? FacingDirection.Right : FacingDirection.Left;
    }

    public FacingDirection GetFacing()
    {
        return lastFacing;
    }

    public bool InGrabbableState() {
        return currentState != PlayerState.JumpSquat &&
            !IsAirborneState(currentState) &&
            !IsHurt() &&
            !IsBlockingState();
    }

    // mostly for forward and backdash
    bool IsDoubleTap(bool inputIsRight)
    {
        int mostRecentPress = -1;
        for (int i = 0; i < PlayerConstants.BufferWindow; i++)
        {
            bool curFrame  = inputIsRight ? Inputs[i].Right     : Inputs[i].Left;
            bool prevFrame = inputIsRight ? Inputs[i + 1].Right : Inputs[i + 1].Left;

            if (curFrame && !prevFrame)
            {
                mostRecentPress = i;
                break;
            }
        }

        if (mostRecentPress == -1) return false;
    
        int searchStart = mostRecentPress + 1;
        int searchEnd   = mostRecentPress + PlayerConstants.DashInputLeniency;

        for (int i = searchStart; i < searchEnd; i++)
        {
            bool curFrame  = inputIsRight ? Inputs[i].Right     : Inputs[i].Left;
            bool prevFrame = inputIsRight ? Inputs[i + 1].Right : Inputs[i + 1].Left;

            if (curFrame && !prevFrame)
                return true;
        }

        return false;
    }

    bool ForwardDashInputted()
    {
        if (dashCooldown > 0) return false;
        bool forwardIsRight = GetFacing() == FacingDirection.Right;
        bool dashMacro = forwardIsRight
            ? Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Dash, [Button.Right]) != null
            : Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Dash, [Button.Left]) != null;
        bool neutralDashPressed = Inputs[0].Dash && !Inputs[0].Left && !Inputs[0].Right;
        return dashMacro || IsDoubleTap(forwardIsRight) || neutralDashPressed;
    }

    bool BackDashInputted()
    {
        bool backIsRight = GetFacing() == FacingDirection.Left;
        bool dashMacro = backIsRight
            ? Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Dash, [Button.Right]) != null
            : Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Dash, [Button.Left]) != null;

        return dashMacro || IsDoubleTap(backIsRight);
    }

    public void Tick()
    {
        AdvanceHitboxes();
        if (dashCooldown > 0) dashCooldown--;
        updateFacing();
        ApplyGravity();
        ProcessCurrentState();
        // Air hit/blockstun owns pushback drag via AirHitDecelerate (arc-based).
        if (currentState != PlayerState.AirHitstun && currentState != PlayerState.AirBlockstun)
            DeceleratePushback();
        ApplyVelocity();
        ResolveFloorCollision();

        if (Inputs.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.QCF))
        {
            GD.Print("QCF");
        }
        if (Inputs.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.QCB))
        {
            GD.Print("QCB");
        }
        if (Inputs.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.HCF))
        {
            GD.Print("HCF");
        }
        if (Inputs.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.HCB))
        {
            GD.Print("HCB");
        }
        if (Inputs.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.DP))
        {
            GD.Print("DP");
        }
        if (Inputs.WasCharge(GetFacing() == FacingDirection.Right, ChargeInputs.BackForward))
        {
            GD.Print("BackForward");
        }
        if (Inputs.WasCharge(GetFacing() == FacingDirection.Right, ChargeInputs.DownUp))
        {
            GD.Print("DownUp");
        }
    }

    protected virtual void ApplyGravity()
    {
        if (physics.IsOnFloor) return;
        if (currentState == PlayerState.Landing) return;
        if (currentState == PlayerState.Airdashing) return;
        if (SuppressGravity) return;

        bool hurt = IsHurt();
        int floating = hurt ? Stats.HitstunFloatingGravity : Stats.FloatingGravity;
        int rising   = hurt ? Stats.HitstunRisingGravity   : Stats.RisingGravity;
        int falling  = hurt ? Stats.HitstunFallingGravity  : Stats.FallingGravity;

        // Jumps are floatier if you are doing an air combo
        if (!hurt && jump.CurrentJumpType == JumpType.Combo)
            floating = Stats.ComboFloatingGravity;

        // Post-airdash glide: floatier + slower fall to preserve momentum for a bit.
        if (!hurt && jump.CurrentJumpType == JumpType.Airdash)
        {
            floating = Stats.AirdashFloatingGravity;
        }

        // GravityScale is thousandths (1000 = 1x); multiply then divide keeps the scale integer.
        int grav = hurt ? hitReaction.GravityScale : ComboScaling.ScaleUnit;

        if (Math.Abs(physics.VelocityY) <= Stats.FloatingGravityThreshold)
            physics.VelocityY -= floating * grav / ComboScaling.ScaleUnit;
        else if (physics.VelocityY >= 0)
            physics.VelocityY -= rising * grav / ComboScaling.ScaleUnit;
        else
            physics.VelocityY -= falling * grav / ComboScaling.ScaleUnit;

        if (physics.VelocityY < Stats.MaxFallSpeed)
            physics.VelocityY = Stats.MaxFallSpeed;
    }

    protected virtual void ProcessCurrentState()
    {
        // Advance the active move's frame counter once per tick, before dispatching to the
        // state handler. Doing it here (instead of in each attack handler) means a new attack
        // state can be added without remembering to bump the counter.
        if (currentMove.HasMove)
        {
            currentMove.Frame++;
            // Fire any hitbox spawns scheduled for the frame we just entered. 
            SpawnScheduledHitboxes();
        }

        switch (currentState)
        {
            case PlayerState.Idle:
                HandleIdleState();
                break;
            case PlayerState.Walking:
                HandleWalkingState();
                break;
            case PlayerState.JumpSquat:
                HandleJumpSquatState();
                break;
            case PlayerState.Jumping:
                HandleAirborneState();
                break;
            case PlayerState.Landing:
                HandleLandingState();
                break;
            case PlayerState.GroundDashing:
                HandleGroundDashState();
                break;
            case PlayerState.Backdashing:
                HandleBackDashState();
                break;
            case PlayerState.Airdashing:
                HandleAirdashState();
                break;
            case PlayerState.Crouching:
                HandleCrouchingState();
                break;
            case PlayerState.StandAttacking:
                HandleStandAttackingState();
                break;
            case PlayerState.CrouchAttacking:
                HandleCrouchAttackingState();
                break;
            case PlayerState.AirAttacking:
                HandleAirAttackingState();
                break;
            case PlayerState.Hitstun:
                HandleHitstunState();
                break;
            case PlayerState.AirHitstun:
                HandleAirHitstunState();
                break;
            case PlayerState.SoftKnockdown:
                HandleSoftKnockdownState();
                break;
            case PlayerState.Knockdown:
                HandleKnockdownState();
                break;
            case PlayerState.Wakeup:
                HandleWakeupState();
                break;
            case PlayerState.Blockstun:
                HandleBlockstunState();
                break;
            case PlayerState.CrouchBlockstun:
                HandleCrouchBlockstunState();
                break;
            case PlayerState.AirBlockstun:
                HandleAirBlockstunState();
                break;
            case PlayerState.GrabOccurring:
                HandleGrabOccurringState();
                break;
            case PlayerState.GrabHit:
                HandleGrabHitState();
                break;
            case PlayerState.HitByGrab:
                HandleHitByGrabState();
                break;
        }
    }

    protected virtual void HandleBlockstunState()
    {
        Decelerate(Stats.GroundedHitDecelerationSpeed);
        if (blockReaction.Timer.Advance())
            TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleCrouchBlockstunState()
    {
        Decelerate(Stats.GroundedHitDecelerationSpeed);
        if (blockReaction.Timer.Advance())
            TransitionTo(PlayerState.Crouching);
    }
    protected virtual void HandleAirBlockstunState()
    {
        AirHitDecelerate();
        if (blockReaction.Timer.Advance())
            TransitionTo(PlayerState.Jumping);
    }

    protected virtual void HandleGrabOccurringState()
    {
        Decelerate();
        if (MoveIsOver()) TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleGrabHitState()
    {
        grabSequence.Timer.Frame++;
        Decelerate();
        Player partner = GrabPartner;
        if (grabSequence.Timer.Frame < 20)
        {
            partner?.Decelerate();
        }

        // Drive the grab here using partner.SetVelocity / AddVelocity / SnapToSim.
        if (grabSequence.Timer.Frame == 20 && partner != null)
        {
            partner.SetVelocity(500 * (int)GetFacing(), 1500); // 0.05 / 0.15 world → physics units
            partner.hitReaction.Timer.Duration = 100; // They get "hit" by the grab and are sent flying ish
            // HardKnockdown is already set by GetGrabbed from the grab hitbox.
            partner.TransitionTo(PlayerState.AirHitstun);
        }

        if (grabSequence.Timer.Done) {
            TransitionTo(PlayerState.Idle);
            return;
        }
    }

    protected virtual void HandleHitByGrabState()
    {
        grabSequence.Timer.Frame++;

        // The attacker drives this player's position and velocity via the Player movement API.
        // The attacker is responsible for launching us out of this state at the right time.
        // As well as putting us into the right state afterwards

        // In other words, this handler does nothing, maybe idk about the future but rn its
        // just an empty state that lets us know were not responsible for anything
    }

    public bool CanCancelInto(MoveType move)
    {
        if (!IsAttacking()) return true;
        if (!currentMove.HasMove || !currentMove.HitLanded) return false;
        if (currentMove.Data is not AttackData attackData) return false;
        if (attackData.CancellableInto == null) return false;

        foreach (var type in attackData.CancellableInto)
            if (type == move) return true;

        return false;
    }

    // True if the current move has run past its full Startup+Active+Recovery length. 
    protected bool MoveIsOver()
    {
        if (!currentMove.HasMove) return true;
        MoveData data = currentMove.Data;
        if (data == null) return true;
        return currentMove.Frame > data.Startup + data.Active + data.Recovery;
    }

    // Shared "the hit landed, is anything trying to cancel out of this move?" check for the
    // three attacking handlers. Returns true if a cancel was executed and the caller should stop
    // processing the current tick (the state was transitioned as part of the cancel). Returns
    // false otherwise (no input matched, or the cancel wasn't legal).
    protected bool TryHitCancel()
    {
        if (!currentMove.HitLanded) return false;

        // Move-chain cancel: any move in the movelist whose Condition matches (and CanCancelInto
        // allows) wins first. This is how light → medium → heavy chains happen mid-attack.
        Move chained = CheckMoveList();
        if (chained != null) { chained.Execute(); return true; }

        // Jump cancel: works on ground attacks always, air attacks only if a double jump remains.
        // Air double-jump cancels also consume HasDoubleJump and re-orient facing on the way out.
        bool jumpPressed = Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Jump) != null;
        bool inAir = IsAirborneState(currentState);
        if (CanCancelInto(MoveType.Jump) && jumpPressed && (!inAir || jump.HasDoubleJump))
        {
            JumpType jumpType = currentMove.Blocked ? JumpType.Normal : JumpType.Combo;
            Jump(inAir ? Stats.DoubleJumpForce : Stats.JumpForce, jumpType);
            if (inAir)
            {
                jump.HasDoubleJump = false;
                updateFacing(true);
            }
            TransitionTo(PlayerState.Jumping);
            return true;
        }

        // Air-dash cancel: only air attacks, requires HasAirdash and a dash input this frame.
        if (inAir && CanCancelInto(MoveType.Dash) && jump.HasAirdash
            && (ForwardDashInputted() || BackDashInputted()))
        {
            jump.AirDashDirection = ForwardDashInputted()
                ? (GetFacing() == FacingDirection.Right ? 1 : -1)
                : (GetFacing() == FacingDirection.Right ? -1 : 1);
            jump.HasAirdash = false;
            jump.AirdashFrame = 0;
            jump.CurrentJumpType = currentMove.Blocked ? JumpType.Normal : JumpType.Combo;
            TransitionTo(PlayerState.Airdashing);
            return true;
        }

        return false;
    }

    // The one clean-up hook run when a move ends.
    protected void EndCurrentMove()
    {
        currentMove = ActiveMoveState.None;
    }

    protected virtual void HandleStandAttackingState()
    {
        Decelerate();
        if (TryHitCancel()) return;
        if (MoveIsOver())
            TransitionTo(physics.IsOnFloor ? PlayerState.Idle : PlayerState.Jumping);
    }

    protected virtual void HandleCrouchAttackingState()
    {
        Decelerate();
        if (TryHitCancel()) return;
        if (MoveIsOver())
            TransitionTo(PlayerState.Crouching);
    }

    protected virtual void HandleAirAttackingState()
    {
        if (TryHitCancel()) return;
        if (MoveIsOver())
            TransitionTo(physics.IsOnFloor ? PlayerState.Idle : PlayerState.Jumping);
    }
    protected virtual void HandleHitstunState()
    {
        Decelerate(Stats.GroundedHitDecelerationSpeed);
        if (hitReaction.Timer.Advance())
            TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleAirHitstunState()
    {
        // No air recovery — stay hurt until ResolveFloorCollision lands you into soft/hard KD.
        // Gravity is applied by ApplyGravity.
        AirHitDecelerate();
    }

    protected virtual void HandleSoftKnockdownState()
    {
        // Constant slide away from the opponent (facing was locked toward them on entry).
        physics.VelocityX = GetFacing() == FacingDirection.Right
            ? -Stats.SoftKnockdownSlideSpeed
            : Stats.SoftKnockdownSlideSpeed;
        physics.VelocityY = 0;
        ClearPushbackVelocity();
        if (hitReaction.Timer.Advance())
            TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleKnockdownState()
    {
        physics.VelocityX = 0;
        physics.VelocityY = 0;
        // TODO: check for quickrise input here
        if (hitReaction.Timer.Advance())
            TransitionTo(PlayerState.Wakeup);
    }

    protected virtual void HandleWakeupState()
    {
        // TODO: wakeup is typically invincible for some frames
        if (hitReaction.Timer.Advance())
            TransitionTo(PlayerState.Idle);
    }
    protected virtual void HandleCrouchingState()
    {
        Decelerate();

        if (TryStartAttack()) return;
        if (TryStartJump(onHold: false)) return;

        if (!Inputs[0].Down)
            TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleAirdashState()
    {
        jump.AirdashFrame++;
        HandleAirdashMovement();

        int totalAirdashDuration = Stats.AirDashStartup + Stats.AirDashDuration + Stats.AirDashRecovery;
        if (jump.AirdashFrame > totalAirdashDuration)
        {
            jump.AirdashFrame = -1;
            jump.CurrentJumpType = JumpType.Airdash;
            TransitionTo(PlayerState.Jumping);
        }
    }

    // Looks through our movelist, and if theres a match on a move, we return some info on that move
    private Move CheckMoveList()
    {
        foreach (var move in moveList)
        {
            if (move.Condition(Inputs))
            {
                return move;
            }
        }
        return null;
    }

    // --- Shared transition attempts used by the actionable grounded states (idle / walk / crouch /
    // dash). Each checks one possible action and, if its input is present, performs the transition
    // and returns true. Callers chain them with `if (TryX()) return;` so the first match wins,
    // exactly like the old else-if ladders. This keeps every state's "what can I do from here" list
    // in one place instead of copy-pasted per handler.

    // Attack out of the current state (the matched move's Execute() does the transition).
    protected bool TryStartAttack()
    {
        Move move = CheckMoveList();
        if (move == null) return false;
        move.Execute();
        return true;
    }

    // Jump. Idle/Walking allow buffered *held* jump; crouch/dash require a fresh press (onHold: false).
    protected bool TryStartJump(bool onHold)
    {
        bool jumpInput = onHold
            ? Inputs.WasHeld(PlayerConstants.BufferWindow, Button.Jump)
            : Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Jump) != null;
        if (!jumpInput) return false;
        JumpSquat();
        TransitionTo(PlayerState.JumpSquat);
        return true;
    }

    protected bool TryStartCrouch()
    {
        if (!Inputs[0].Down) return false;
        TransitionTo(PlayerState.Crouching);
        return true;
    }

    protected bool TryForwardDash()
    {
        if (!ForwardDashInputted()) return false;
        TransitionTo(PlayerState.GroundDashing);
        return true;
    }

    protected bool TryBackdash()
    {
        if (!BackDashInputted()) return false;
        TransitionTo(PlayerState.Backdashing);
        return true;
    }

    protected virtual void HandleIdleState()
    {
        HandleGroundedMovement();

        if (TryStartAttack()) return;
        if (TryStartJump(onHold: true)) return;
        if (TryStartCrouch()) return;
        if (TryForwardDash()) return;
        if (TryBackdash()) return;

        if (Inputs[0].Right || Inputs[0].Left)
            TransitionTo(PlayerState.Walking);
    }

    protected virtual void HandleWalkingState()
    {
        HandleGroundedMovement();

        if (TryStartAttack()) return;
        if (TryStartJump(onHold: true)) return;
        if (TryStartCrouch()) return;
        if (TryForwardDash()) return;
        if (TryBackdash()) return;

        if (!(Inputs[0].Right || Inputs[0].Left))
            TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleGroundDashState()
    {
        groundDashFrame++;
        HandleGroundedDashMovement();

        // Attacks, jumps, crouches, and backdashes can cancel the dash at any time.
        if (TryStartAttack()) return;
        if (TryStartJump(onHold: false)) return;
        if (TryStartCrouch()) return;
        if (TryBackdash()) return;

        // Returning to walk/idle is locked out until the dash has run for its minimum duration.
        if (groundDashFrame < Stats.MinDashDuration) return;

        bool backHeld = GetFacing() == FacingDirection.Right ? Inputs[0].Left : Inputs[0].Right;
        if (!(Inputs[0].Right || Inputs[0].Left) && !Inputs[0].Dash)
            // Holding dash keeps dashing forward.
            TransitionTo(PlayerState.Idle);
        else if (backHeld)
            TransitionTo(PlayerState.Walking);
    }

    protected virtual void HandleBackDashState()
    {
        backdashInfo.BackDashFrame++;
        HandleBackDashMovement();

        int totalBackdashDuration = Stats.BackDashDuration + Stats.BackDashRecoverDuration + Stats.BackDashStartDuration;
        if (backdashInfo.BackDashFrame >= totalBackdashDuration)
        {
            
            backdashInfo.BackDashFrame = -1;
            TransitionTo(PlayerState.Idle);
        }
    }

    protected virtual void HandleJumpSquatState()
    {
        jump.JumpSquatFrame++;
        Decelerate();
        if (jump.JumpSquatFrame >= Stats.JumpSquatDuration)
        {
            // Jumptype already set in the JumpSquat function
            Jump(Stats.JumpForce, jump.CurrentJumpType);
            TransitionTo(PlayerState.Jumping);
        }
    }

    protected virtual void HandleAirborneState()
    {
        HandleAerialMovement();
        jump.FramesSinceLastJump++;

        Move usedMove = CheckMoveList();

        if(usedMove != null)
        {
            usedMove.Execute();
        }
        else if (Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Jump) is InputFrame jumpPress && jump.HasDoubleJump && jump.FramesSinceLastJump >= Stats.FramesUntilActionableAfterJump)
        {
            JumpType jumptype = JumpType.Normal;
            updateFacing(true); 
            // If we airdashed, then jumped in the same direction as our airdash, we dashjump. Otherwise, we do a normal jump
            if (!jump.HasAirdash)
            {
                int pressDir = jumpPress.Right ? 1 : (jumpPress.Left ? -1 : 0);
                var isSameDirection = (pressDir > 0 && physics.VelocityX > 0) || (pressDir < 0 && physics.VelocityX < 0);
                if (isSameDirection)
                {
                    jumptype = JumpType.Dash;
                }
            }

            Jump(Stats.DoubleJumpForce, jumptype);
            jump.HasDoubleJump = false;
        }
        else if ((ForwardDashInputted() || BackDashInputted()) && jump.HasAirdash && jump.FramesSinceLastJump >= Stats.FramesUntilActionableAfterJump)
        {
            jump.AirDashDirection = ForwardDashInputted() 
                ? (GetFacing() == FacingDirection.Right ? 1 : -1)
                : (GetFacing() == FacingDirection.Right ? -1 : 1);
            jump.HasAirdash = false;
            jump.AirdashFrame = 0;
            TransitionTo(PlayerState.Airdashing);
        }
        // landing state transition is handled by the ResolveFloorCollision function
    }

    protected virtual void HandleLandingState()
    {   
        jump.LandingFrame++;
        physics.VelocityX = 0;
        physics.VelocityY = 0;
        if (jump.LandingFrame >= jump.LandingDuration)
        {
            jump.LandingFrame = -1;
            TransitionTo(PlayerState.Idle);
        }
    }

    protected virtual void TransitionTo(PlayerState newState)
    {
        // reset state specific stuff that we are moving out of
        if (currentState == PlayerState.JumpSquat)
            jump.JumpSquatFrame = -1;
        if (currentState == PlayerState.Landing)
            jump.LandingFrame = -1;
        if (currentState == PlayerState.Backdashing)
            backdashInfo.BackDashFrame = -1;
        // Leaving a ground dash starts the dash cooldown to block immediate redashing
        if (currentState == PlayerState.GroundDashing)
        {
            dashCooldown = Stats.DashCooldown;
            groundDashFrame = -1;
        }
        if (currentState == PlayerState.Airdashing)
            jump.AirdashFrame = -1;
        if (IsAttacking() || currentState == PlayerState.GrabOccurring)
        {
            currentBufferWindow = PlayerConstants.BufferWindow;
            EndCurrentMove();
        }
        if (currentState == PlayerState.GrabHit || currentState == PlayerState.HitByGrab)
            grabPartnerNumber = 0;

        // reset state-specific that we are moving into
        if (newState == PlayerState.JumpSquat)
        {
            ClearPushbackVelocity();
            jump.FramesSinceLastJump = 0;
            jump.JumpSquatFrame = 0;
        }
        if (newState == PlayerState.GroundDashing)
            groundDashFrame = 0;
        if (newState == PlayerState.Landing)
            jump.LandingFrame = 0;
        if (newState == PlayerState.Backdashing)
            backdashInfo.BackDashFrame = 0;
        if (newState == PlayerState.Airdashing)
        {
            ClearPushbackVelocity();
            jump.AirdashFrame = 0;
        }
        if (newState == PlayerState.AirAttacking || newState == PlayerState.StandAttacking || newState == PlayerState.CrouchAttacking){
            currentBufferWindow = PlayerConstants.CancelBufferWindow;
        }
        if (newState == PlayerState.GrabOccurring)
            currentBufferWindow = PlayerConstants.CancelBufferWindow;
        if (newState == PlayerState.GrabHit || newState == PlayerState.HitByGrab)
            grabSequence.Timer.Frame = 0;
        if (newState == PlayerState.Hitstun || newState == PlayerState.AirHitstun)
            hitReaction.Timer.Frame = 0;
        if (newState == PlayerState.SoftKnockdown)
        {
            hitReaction.Timer.Start(Stats.SoftKnockdownDuration);
            updateFacing(true); // snap toward the opponent, then lock for the slide
            ClearPushbackVelocity();
            physics.VelocityX = GetFacing() == FacingDirection.Right
                ? -Stats.SoftKnockdownSlideSpeed
                : Stats.SoftKnockdownSlideSpeed;
            physics.VelocityY = 0;
        }
        if (newState == PlayerState.Knockdown)
            hitReaction.Timer.Start(Stats.KnockdownDuration);
        if (newState == PlayerState.Wakeup)
            hitReaction.Timer.Start(Stats.WakeupDuration);
        if (newState == PlayerState.Blockstun || newState == PlayerState.CrouchBlockstun || newState == PlayerState.AirBlockstun)
            blockReaction.Timer.Frame = 0;

        currentState = newState;
    }

    protected virtual void HandleGroundedMovement()
    {
        if (Inputs[0].Right)
            physics.VelocityX = Stats.Speed;
        else if (Inputs[0].Left) 
            physics.VelocityX = -Stats.Speed;
        else
            Decelerate();
    }

    protected virtual void HandleGroundedDashMovement()
    {
        physics.VelocityX = GetFacing() == FacingDirection.Right ? Stats.DashSpeed : -Stats.DashSpeed;
    }

    protected virtual void HandleBackDashMovement()
    {   
        
        if(backdashInfo.BackDashFrame <= Stats.BackDashStartDuration)
        {
            // just started to backdash
            Decelerate();
        }
        else if(backdashInfo.BackDashFrame <= Stats.BackDashDuration + Stats.BackDashStartDuration)
        {
            // actually backdashing
            physics.VelocityX = GetFacing() == FacingDirection.Right 
                ? -Stats.BackDashSpeed 
                : Stats.BackDashSpeed;
        }
        else
        {
            // Backdash ending
            Decelerate();
        }
    }

    protected virtual void HandleAirdashMovement()
    {
        ClearPushbackVelocity();
        int activeEnd = Stats.AirDashStartup + Stats.AirDashDuration;
        if (jump.AirdashFrame <= Stats.AirDashStartup)
        {
            physics.VelocityX = 0;
        }
        else if (jump.AirdashFrame <= activeEnd)
        {
            physics.VelocityX = Stats.AirDashSpeed * jump.AirDashDirection;
        }
        else
        {
            // Recovery: still in airdash state, but at the lower finish speed.
            physics.VelocityX = Stats.AirDashFinishSpeed * jump.AirDashDirection;
        }
        physics.VelocityY = 0;
    }

    protected virtual void HandleAerialMovement()
    {
        // Does nothing 
    }

    protected virtual void Decelerate(int? speed = null)
    {
        int deceleration = speed ?? (Math.Abs(physics.VelocityX) > Stats.StrongDecelerationThreshold
            ? Stats.StrongDecelerationSpeed
            : Stats.DecelerationSpeed);

        if (physics.VelocityX > 0)
            physics.VelocityX = Math.Max(0, physics.VelocityX - deceleration);
        else
            physics.VelocityX = Math.Min(0, physics.VelocityX + deceleration);
    }

    protected virtual void DeceleratePushback()
    {
        int deceleration = Stats.GroundedHitDecelerationSpeed;
        if (physics.PushbackVelocityX > 0)
            physics.PushbackVelocityX = Math.Max(0, physics.PushbackVelocityX - deceleration);
        else if (physics.PushbackVelocityX < 0)
            physics.PushbackVelocityX = Math.Min(0, physics.PushbackVelocityX + deceleration);
    }

    protected virtual void AirHitDecelerate()
    {
        // Air hit horizontal lives on PushbackVelocityX. Feel rules:
        // - rising: keep full pushback (no drag)
        // - top of arc: decelerate, but not below AirHitMinimumSpeed
        // - falling: snap to that minimum in the same direction
        int pb = physics.PushbackVelocityX;
        if (pb == 0) return;

        if (Math.Abs(physics.VelocityY) <= Stats.FloatingGravityThreshold)
        {
            int deceleration = Stats.AirHitDecelerationSpeed;
            if (pb > 0)
                physics.PushbackVelocityX = Math.Max(Stats.AirHitMinimumSpeed, pb - deceleration);
            else
                physics.PushbackVelocityX = Math.Min(-Stats.AirHitMinimumSpeed, pb + deceleration);
        }
        else if (physics.VelocityY < -Stats.FloatingGravityThreshold)
        {
            physics.PushbackVelocityX = Math.Sign(pb) * Stats.AirHitMinimumSpeed;
        }
    }

    protected virtual void SetupJumpDirection()
    {
        // Prefer the direction held at the jump press edge (respects buffered inputs). When jump is
        // held through a landing there is no fresh press edge, so fall back to the currently held
        // direction — this lets hold-jump bunny-hopping keep its forward/back momentum.
        InputFrame source = Inputs.WasJustPressed(PlayerConstants.BufferWindow, Button.Jump) ?? Inputs[0];
        if (source.Right) jump.jumpDirection = 1;
        else if (source.Left) jump.jumpDirection = -1;
        else jump.jumpDirection = 0;
    }

    protected virtual void JumpSquat()
    {        
        SetupJumpDirection();
        if(currentState == PlayerState.GroundDashing){
            jump.CurrentJumpType = JumpType.Dash;
        }
        else
        {
            jump.CurrentJumpType = JumpType.Normal;
        }
    }

    protected virtual void Jump(int jumpForce, JumpType jumpType)
    {
        ClearPushbackVelocity();
        SetupJumpDirection();
        jump.CurrentJumpType = jumpType;
        physics.VelocityY = jumpForce;
        switch(jump.CurrentJumpType)
        {
            case JumpType.Dash:
                physics.VelocityX = Stats.DashJumpSpeed;
                break;
            case JumpType.Normal:
                physics.VelocityX = Stats.WalkJumpSpeed;
                break;
            case JumpType.Combo:
                physics.VelocityX = Stats.WalkJumpSpeed;
                break;
        }
        physics.VelocityX *= jump.jumpDirection;

        jump.FramesSinceLastJump = 0;
    }

    protected virtual void ApplyVelocity()
    {
        // Velocities are physics units per frame; only divide when syncing the node for display.
        SimX += physics.VelocityX + physics.PushbackVelocityX;
        SimY += physics.VelocityY;
        SyncVisualPosition();
    }

    protected virtual void ResolveFloorCollision()
    {
        if (SimY <= PlayerConstants.FloorY)
        {
            SimY = PlayerConstants.FloorY;
            physics.VelocityY = 0;
            if (!physics.IsOnFloor)
            {
                // just landed this frame
                jump.HasDoubleJump = true;
                jump.HasAirdash = true;
                jump.FramesSinceLastJump = 0;
                if (currentState == PlayerState.AirHitstun)
                    TransitionTo(hitReaction.HardKnockdown ? PlayerState.Knockdown : PlayerState.SoftKnockdown);
                else if (currentState == PlayerState.AirBlockstun)
                {
                    TransitionTo(PlayerState.Landing);
                    jump.LandingDuration = PlayerConstants.AirBlockstunLandingPenalty + Stats.LandingDuration;
                }
                else
                {
                    TransitionTo(PlayerState.Landing);
                    jump.LandingDuration = Stats.LandingDuration;
                }
            }
            physics.IsOnFloor = true;
            SyncVisualPosition();
        }
        else
        {
            physics.IsOnFloor = false;
        }
    }
}