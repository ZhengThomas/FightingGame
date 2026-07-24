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
    public float VelocityX;
    public float VelocityY;
    public bool IsOnFloor;
}

public enum JumpType
{
    Normal,
    Dash,
    Combo,
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
    public const float FloorY = -0.8f;
    public const int BufferWindow = 6;
    public const int CancelBufferWindow = 8;          // slightly more lenient during cancels
    public const int DashInputLeniency = 10;          // time between two button presses for a dash
    public const float MaxPushboxCorrectionPerFrame = 0.2f;
    public const float MaxPlayerSeparation = 6f;      // max distance between the two players
    public const float MaxDistanceFromCenter = 7f;    // max distance either player can be from the midpoint
    public const int CrossupProtectionWindow = 3;     // frames either direction counts as back after a crossup
    public const int AirBlockstunLandingPenalty = 15; // extra landing frames when touching down during air blockstun
    public const float FixedDelta = 0.01666f;         // Using a fixed number since the engine delta is not consistent
}

public class ActiveMove
{
    public readonly MoveData Data;
    public readonly int Id;
    public int Frame;
    public bool HitLanded;
    public bool Blocked;

    private List<Box> lastValidHitboxes = null;
    private List<Box> lastValidHurtboxes = null;
    static readonly List<Box> emptyList = new List<Box>();

    public ActiveMove(MoveData data, int id)
    {
        Data = data;
        Id = id;
        // Start at 1 so the press tick counts as startup frame 1 (Frame is not incremented
        // that same tick — ProcessCurrentState already ran before StartMove).
        Frame = 1;
    }

    public List<Box> GetCurrentHitboxes()
    {
        int startupEnd = Data.Startup;
        int activeEnd  = startupEnd + Data.Active;

        if (Frame > startupEnd && Frame <= activeEnd)
        {
            int activeFrame = Frame - startupEnd - 1;
            var perFrame = Data.HitboxesPerFrame;
            List<Box> boxes = null;
            if (perFrame != null && activeFrame < perFrame.Length)
                boxes = perFrame[activeFrame];
            if (boxes != null)
                lastValidHitboxes = boxes;
            return lastValidHitboxes ?? emptyList;
        }

        return emptyList;
    }

    // Returns the per-frame hurtbox override for the current frame, or null if none.
    // Player.GetCurrentHurtboxes() falls back to the default pose hurtbox when this is null.
    // Frame is 1-based (press tick = 1); the array is 0-based over the move's lifetime.
    public List<Box> GetHurtboxOverride()
    {
        var perFrame = Data.HurtboxesPerFrame;
        if (perFrame == null) return null;
        List<Box> boxes = null;
        int index = Frame - 1;
        if (index >= 0 && index < perFrame.Length)
            boxes = perFrame[index];
        if (boxes != null)
            lastValidHurtboxes = boxes;
        return lastValidHurtboxes;
    }
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
    Knockdown,      // lying on the ground after a hard knockdown or landing from AirHitstun
    Wakeup,         // rising animation before returning to Idle
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
    public float GravityMultiplier;
    public float hitstunMultiplier;
    public float damageMultiplier;
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

// Meant to be a hitbox or hurtbox or whatever
public struct Box
{
    public float X;      // offset from player center
    public float Y;
    public float Width;
    public float Height;
}

public class Move
{
    public string Name;
    public System.Func<InputFrame[], bool> Condition;
    public System.Action Execute;
}

public partial class Player : Node3D
{
    // Emitted at the end of every simulation tick. Used as the animation clock.
    [Signal] public delegate void TickedEventHandler();

    [Export] public bool ShowDebug = true;
    [Export] public int PlayerNumber = 1;
    InputFrame[] inputBuffer;
    public PhysicsState physics;
    PlayerState currentState = PlayerState.Idle;
    JumpState jump = new JumpState { JumpSquatFrame = -1, AirdashFrame = -1, LandingFrame = -1 };
    ActiveMove currentMove = null;

    public PlayerState CurrentState => currentState;
    public ActiveMove CurrentMove => currentMove;
    public PhysicsState Physics => physics;
    public int AirDashDirection => jump.AirDashDirection; // +1/-1, the locked-in airdash direction
    BackdashState backdashInfo = new BackdashState{ BackDashFrame = -1 };
    int dashCooldown = 0; // counts down each tick; while > 0, forward dash and backdash are blocked
    int groundDashFrame = -1; // ticks spent in the current ground dash (-1 = not dashing); gates the walk/idle exit
    HitReactionState hitReaction;
    BlockReactionState blockReaction;
    GrabSequenceState grabSequence;
    Player grabPartner = null; // the other player during GrabHit / HitByGrab
    MatchManager Match;
    public bool SuppressGravity = false;

    List<Move> moveList;
    public Box Pushbox = new Box { X = 0, Y = 0.7f, Width = 0.6f, Height = 1.8f };
    static readonly List<Box> StandingHurtbox = new List<Box>{ new Box { X = 0, Y = 1.0f, Width = 1f, Height = 2.0f } };
    static readonly List<Box> CrouchingHurtbox = new List<Box>{new Box { X = 0, Y = 0.5f, Width = 1f, Height = 1.5f }};
    static readonly List<Box> AirborneHurtbox = new List<Box>{new Box { X = 0, Y = 1.0f, Width = 1f, Height = 2.0f }};

    // stores 10 most recently received hit ids
    int[] hitHistory = new int[10];
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
        Match = GetNode<MatchManager>("/root/MatchManager");
        Match.RegisterPlayer(this, PlayerNumber);

        lastFacing = PlayerNumber == 1 ? FacingDirection.Right : FacingDirection.Left;
        InitializeMoveList();
    }

    protected virtual void InitializeMoveList()
    {
        moveList = new List<Move>
        {
            new Move
            {
                Name = "CrouchingLight",
                Condition = (input) => physics.IsOnFloor && input.WasJustPressed(currentBufferWindow, f => f.LightAttack, [f => f.Down]) != null && CanCancelInto(MoveType.CrouchLight),
                Execute = () => StartMove(Moveset.CrouchingLight, PlayerState.CrouchAttacking),
            },
            new Move
            {
                Name = "AirLight",
                Condition = (input) => IsAirborneState(currentState) && input.WasJustPressed(currentBufferWindow, f => f.LightAttack) != null && CanCancelInto(MoveType.Light),
                Execute = () => StartMove(Moveset.AirLight, PlayerState.AirAttacking),
            },
            new Move
            {
                Name = "StandingLight",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, f => f.LightAttack, null, [f => f.Down]) != null && CanCancelInto(MoveType.Light),
                Execute = () => StartMove(Moveset.StandingLight, PlayerState.StandAttacking),
            },
            new Move
            {
                Name = "CrouchingMedium",
                Condition = (input) => physics.IsOnFloor && input.WasJustPressed(currentBufferWindow, f => f.MediumAttack, [f => f.Down]) != null && CanCancelInto(MoveType.CrouchMedium),
                Execute = () => StartMove(Moveset.CrouchingMedium, PlayerState.CrouchAttacking),
            },
            new Move
            {
                Name = "AirMedium",
                Condition = (input) => IsAirborneState(currentState) && input.WasJustPressed(currentBufferWindow, f => f.MediumAttack) != null && CanCancelInto(MoveType.Medium),
                Execute = () => StartMove(Moveset.AirMedium, PlayerState.AirAttacking),
            },
            new Move
            {
                Name = "StandingMedium",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, f => f.MediumAttack, null, [f => f.Down]) != null && CanCancelInto(MoveType.Medium),
                Execute = () => StartMove(Moveset.StandingMedium, PlayerState.StandAttacking),
            },
            new Move
            {
                Name = "CrouchingHeavy",
                Condition = (input) => physics.IsOnFloor && input.WasJustPressed(currentBufferWindow, f => f.HeavyAttack, [f => f.Down]) != null && CanCancelInto(MoveType.CrouchHeavy),
                Execute = () => StartMove(Moveset.CrouchingHeavy, PlayerState.CrouchAttacking),
            },
            new Move
            {
                Name = "AirHeavy",
                Condition = (input) => IsAirborneState(currentState) && input.WasJustPressed(currentBufferWindow, f => f.HeavyAttack) != null && CanCancelInto(MoveType.Heavy),
                Execute = () => StartMove(Moveset.AirHeavy, PlayerState.AirAttacking),
            },
            new Move
            {
                Name = "StandingHeavy",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, f => f.HeavyAttack, null, [f => f.Down]) != null && CanCancelInto(MoveType.Heavy),
                Execute = () => StartMove(Moveset.StandingHeavy, PlayerState.StandAttacking),
            },
            new Move
            {
                Name = "BackThrow",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, f => f.Grab, [f => {return GetFacing() == FacingDirection.Right ? f.Left : f.Right;}]) != null && CanCancelInto(MoveType.Grab) && CanUseGrab(),
                Execute = () => StartMove(Moveset.BackGrab, PlayerState.GrabOccurring),
            },
            new Move
            {
                Name = "FowardThrow",
                Condition = (input) => input.WasJustPressed(currentBufferWindow, f => f.Grab) != null && CanCancelInto(MoveType.Grab) && CanUseGrab(),
                Execute = () => StartMove(Moveset.FowardGrab, PlayerState.GrabOccurring),
            },
        };
    }

    public void RegisterHit(int attackId, bool blocked = false)
    {
        if (currentMove != null && currentMove.Id == attackId)
        {
            currentMove.HitLanded = true;
            currentMove.Blocked = blocked;
        }
    }

    public bool WasAlreadyHitBy(int attackId)
    {
        foreach (var record in hitHistory)
            if (record == attackId)
                return true;
        return false;
    }

    public virtual bool TakeHit(AttackData data, int attackId, int direction, Player attacker)
    {
        hitHistory[hitHistoryIndex] = attackId;
        hitHistoryIndex = (hitHistoryIndex + 1) % hitHistory.Length;

        // Before we actually get hurt, instead first check if we block the hit.
        if (CanBlock(currentState) && IsHoldingBackForBlock(attacker) && IsHoldingHighLowForBlock(data))
        {
            BlockAttack(data, direction);
            return true;
        }

        // If its the first time we are getting hit, we reset the hitstun state
        if (!IsHurt())
        {
            hitReaction.GravityMultiplier = 1;
            hitReaction.hitstunMultiplier = 1;
            hitReaction.damageMultiplier = 1;
        }

        hitReaction.Timer.Duration = data.HitStun > 0 ? data.HitStun : 15;

        // We apply the velocity of the attack to the player
        physics.VelocityX = data.Pushback * direction;

        // TODO - If were in the corner, apply the velcoity of the attack to the attacker in the opposite direction

        if (data.LaunchesOpponent || !physics.IsOnFloor)
        {
            TransitionTo(PlayerState.AirHitstun);
            // If we are gonna be airbourne cuz of the attack, we also apply the y velocity of the attack
            physics.VelocityY = data.LaunchForce;
        }
        else
        {
            TransitionTo(PlayerState.Hitstun);
        }

        return false;
    }

    public virtual void BlockAttack(AttackData data, int direction)
    {
        blockReaction.Timer.Duration = data.BlockStun > 0 ? data.BlockStun : 15;
        physics.VelocityX = data.PushbackOnBlock == 0 ? data.Pushback : data.PushbackOnBlock;
        physics.VelocityX *= direction;

        if(!physics.IsOnFloor)
        {
            physics.VelocityY = data.LaunchForceOnBlock == 0 ? data.LaunchForce : data.LaunchForceOnBlock;
            TransitionTo(PlayerState.AirBlockstun);
            return;
        }
        else if (inputBuffer[0].Down)
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
        TransitionTo(attackingState);
        // The attacker (and its slash VFX) draws over the opponent for the duration of the move,
        // like GGST — not just from the moment a hit connects.
        Match.BringToFront(this);
        // ID is derived from frame count and player number, like hashing but lazy
        // could overflow if 2 billion frames pass, which is unlikely and i will ignore such a possibility
        int id = PlayerNumber * 1000000 + Match.FrameCount;
        currentMove = new ActiveMove(moveData, id);
    }

    public IEnumerable<ActiveMove> GetActiveMoves()
    {
        if (currentMove != null)
            yield return currentMove;
        // future: yield return each spawned projectile/attack here
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
            || currentState == PlayerState.Knockdown
            || currentState == PlayerState.Wakeup;
    }

    public bool IsBlockingState()
    {
        return currentState == PlayerState.Blockstun
            || currentState == PlayerState.CrouchBlockstun
            || currentState == PlayerState.AirBlockstun;
    }

    public bool IsGrabbing()
    {
        return currentState == PlayerState.GrabOccurring;
    }

    public bool CanUseGrab()
    {
        return currentState == PlayerState.Idle
            || currentState == PlayerState.Walking
            || currentState == PlayerState.Crouching
            || currentState == PlayerState.GroundDashing;
    }

    public bool IsInGrabSequence()
    {
        return currentState == PlayerState.GrabHit || currentState == PlayerState.HitByGrab;
    }

    // Movement API used by the attacker to drive the defender during a grab sequence.
    public void SetVelocity(float x, float y)
    {
        physics.VelocityX = x;
        physics.VelocityY = y;
    }

    public void AddVelocity(float x, float y)
    {
        physics.VelocityX += x;
        physics.VelocityY += y;
    }

    public void SnapToPosition(Vector3 position)
    {
        Position = position;
        physics.VelocityX = 0;
        physics.VelocityY = 0;
    }

    public void FlipFacing()
    {
        lastFacing = lastFacing == FacingDirection.Right ? FacingDirection.Left : FacingDirection.Right;
    }

    // Called by MatchManager when a grab hitbox connects.
    // Sets this player up as the attacker for the grab cinematic.
    public void RegisterGrab(Player defender, GrabData grabData)
    {
        grabPartner = defender;
        grabSequence.Timer.Duration = grabData.GrabSequenceDuration;

        if (grabData.IsBackThrow)
            FlipFacing();

        // Snap the defender to the inital grab position.
        int facingMult = GetFacing() == FacingDirection.Right ? 1 : -1;
        defender.SnapToPosition(new Vector3(
            Position.X + grabData.DefenderSnapOffsetX * facingMult,
            Position.Y + grabData.DefenderSnapOffsetY,
            Position.Z
        ));
        TransitionTo(PlayerState.GrabHit);
    }

    // Called by MatchManager when this player is caught by a grab.
    // Snaps the defender into position and hands control to the attacker.
    public void GetGrabbed(GrabData grabData, int attackId, Player attacker)
    {
        hitHistory[hitHistoryIndex] = attackId;
        hitHistoryIndex = (hitHistoryIndex + 1) % hitHistory.Length;

        grabPartner = attacker;
        grabSequence.Timer.Duration = grabData.GrabSequenceDuration;

        TransitionTo(PlayerState.HitByGrab);
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
        bool attackerIsToRight = attacker.Position.X > Position.X;

        // If were currently correctly holding away from the attacker, we are holding back
        bool holdingBackNow = attackerIsToRight ? inputBuffer[0].Left : inputBuffer[0].Right;
        if (holdingBackNow) return true;

        // If in the last bit of time we crossed up, we are always holding back if were not holding neutral
        bool holdingNeutral = !inputBuffer[0].Left && !inputBuffer[0].Right;
        if (holdingNeutral) return false;
        for (int i = 0; i <= PlayerConstants.CrossupProtectionWindow; i++)
        {
            Vector3 historicalAttackerPos = Match.GetHistoricalPosition(attacker, i);
            Vector3 historicalDefenderPos = Match.GetHistoricalPosition(this, i);
            bool attackerWasToRight = historicalAttackerPos.X > historicalDefenderPos.X;

            // Since were not holding neutral and there was a crossup, we are blocking regardless of 
            // What direction we are currently holding
            if (attackerWasToRight != attackerIsToRight) return true;
        }

        // We are not holding back after all the checks
        return false;
    }

    // Returns true if the player's current blocking stance covers the attack's type
    protected virtual bool IsHoldingHighLowForBlock(AttackData data)
    {
        if (IsAirborneState(currentState)) return true; // airblocks always work

        if (data.IsLow)
            return inputBuffer[0].Down;

        if (data.IsOverhead)
            return !inputBuffer[0].Down;  

        return true;              // mid are always blocked
    }

    // cached empty list so we're not allocating a new one every frame
    static readonly List<Box> emptyBoxList = new List<Box>();
    public List<Box> GetCurrentHitboxes()
    {
        return currentMove?.GetCurrentHitboxes() ?? emptyBoxList;
    }
    public virtual List<Box> GetCurrentHurtboxes()
    {
        // Were immortal if were in the middle of waking up
        if (currentState == PlayerState.Wakeup)
        {
            return emptyBoxList;
        }
        // Both players are invulnerable for the duration of the grab cinematic
        if (currentState == PlayerState.GrabHit || currentState == PlayerState.HitByGrab)
        {
            return emptyBoxList;
        }
        // and were "crouching" if were lying on the ground
        if (currentState == PlayerState.Knockdown)
        {
            return CrouchingHurtbox;
        }
        // If were attacking, its based on our attack
        if (IsAttacking() && currentMove != null)
            return currentMove.GetHurtboxOverride() ?? GetDefaultHurtbox();
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
            currentState == PlayerState.GrabHit ||
            currentState == PlayerState.HitByGrab ||
            IsHurt()) && !forceOverride)
        {
            return;
        }

        Player opponent = Match.Player1 == this ? Match.Player2 : Match.Player1;
        if (opponent == null) return;

        lastFacing = opponent.Position.X > Position.X ? FacingDirection.Right : FacingDirection.Left;
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
            bool curFrame  = inputIsRight ? inputBuffer[i].Right     : inputBuffer[i].Left;
            bool prevFrame = inputIsRight ? inputBuffer[i + 1].Right : inputBuffer[i + 1].Left;

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
            bool curFrame  = inputIsRight ? inputBuffer[i].Right     : inputBuffer[i].Left;
            bool prevFrame = inputIsRight ? inputBuffer[i + 1].Right : inputBuffer[i + 1].Left;

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
            ? inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Dash, [f => f.Right]) != null
            : inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Dash, [f => f.Left]) != null;
        bool neutralDashPressed = inputBuffer[0].Dash && !inputBuffer[0].Left && !inputBuffer[0].Right;
        return dashMacro || IsDoubleTap(forwardIsRight) || neutralDashPressed;
    }

    bool BackDashInputted()
    {
        bool backIsRight = GetFacing() == FacingDirection.Left;
        bool dashMacro = backIsRight
            ? inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Dash, [f => f.Right]) != null
            : inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Dash, [f => f.Left]) != null;

        return dashMacro || IsDoubleTap(backIsRight);
    }

    public void Tick(InputFrame[] buffer)
    {
        inputBuffer = buffer;
        if (dashCooldown > 0) dashCooldown--;
        updateFacing();
        ApplyGravity();
        ProcessCurrentState();
        ApplyVelocity();
        ResolveFloorCollision();

        if (inputBuffer.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.QCF))
        {
            GD.Print("QCF");
        }
        if (inputBuffer.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.QCB))
        {
            GD.Print("QCB");
        }
        if (inputBuffer.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.HCF))
        {
            GD.Print("HCF");
        }
        if (inputBuffer.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.HCB))
        {
            GD.Print("HCB");
        }
        if (inputBuffer.WasMotion(GetFacing() == FacingDirection.Right, MotionInputs.DP))
        {
            GD.Print("DP");
        }
        if (inputBuffer.WasCharge(GetFacing() == FacingDirection.Right, ChargeInputs.BackForward))
        {
            GD.Print("BackForward");
        }
        if (inputBuffer.WasCharge(GetFacing() == FacingDirection.Right, ChargeInputs.DownUp))
        {
            GD.Print("DownUp");
        }

        // Fired once per simulation tick. The animator listens to this to step animations
        // in lockstep with the game (so they freeze when the game is paused / in hit pause).
        EmitSignal(SignalName.Ticked);
    }

    protected virtual void ApplyGravity()
    {
        if (physics.IsOnFloor) return;
        if (currentState == PlayerState.Landing) return;
        if (currentState == PlayerState.Airdashing) return;
        if (SuppressGravity) return;

        bool hurt = IsHurt();
        float floating = hurt ? Stats.HitstunFloatingGravity : Stats.FloatingGravity;
        float rising   = hurt ? Stats.HitstunRisingGravity   : Stats.RisingGravity;
        float falling  = hurt ? Stats.HitstunFallingGravity  : Stats.FallingGravity;

        // Jumps are floatier if you are doing an air combo
        if (!hurt && jump.CurrentJumpType == JumpType.Combo)
            floating = Stats.ComboFloatingGravity;

        if (Math.Abs(physics.VelocityY) <= Stats.FloatingGravityThreshold)
            physics.VelocityY -= floating;
        else if (physics.VelocityY >= 0)
            physics.VelocityY -= rising;
        else
            physics.VelocityY -= falling;

        if (physics.VelocityY < Stats.MaxFallSpeed)
            physics.VelocityY = Stats.MaxFallSpeed;
    }

    protected virtual void ProcessCurrentState()
    {
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
        currentMove.Frame++;

        int startupEnd  = currentMove.Data.Startup;
        int activeEnd   = startupEnd + currentMove.Data.Active;
        int recoveryEnd = activeEnd  + currentMove.Data.Recovery;

        if (currentMove.Frame <= startupEnd)
        {
            // startup — not yet active
        }
        else if (currentMove.Frame <= activeEnd)
        {
            // active — grab hitbox is out
        }
        else if (currentMove.Frame <= recoveryEnd)
        {
            // recovery — grab whiffed
        }
        else
        {
            TransitionTo(PlayerState.Idle);
        }
    }

    protected virtual void HandleGrabHitState()
    {
        grabSequence.Timer.Frame++;
        Decelerate();
        if (grabSequence.Timer.Frame < 20)
        {
            grabPartner.Decelerate();
        }

        // Drive the grab here using grabPartner.SetVelocity / AddVelocity / SnapToPosition.
        if (grabSequence.Timer.Frame == 20)
        {
            grabPartner.SetVelocity(3f * (int)GetFacing(), 9f);
            grabPartner.hitReaction.Timer.Duration = 100; // They get "hit" by the grab and are sent flying ish
            // this will be custom for each character, but rn im lazy and this is simple and sounds right
            grabPartner.TransitionTo(PlayerState.AirHitstun);
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
        if (currentMove == null || !currentMove.HitLanded) return false;
        if (currentMove.Data is not AttackData attackData) return false;
        if (attackData.CancellableInto == null) return false;

        foreach (var type in attackData.CancellableInto)
            if (type == move) return true;

        return false;
    }

    protected virtual void HandleStandAttackingState()
    {
        Decelerate();
        currentMove.Frame++;

        int startupEnd  = currentMove.Data.Startup;
        int activeEnd   = startupEnd + currentMove.Data.Active;
        int recoveryEnd = activeEnd  + currentMove.Data.Recovery;

        if (currentMove.HitLanded)
        {
            Move cancel = CheckMoveList();
            if (cancel != null) { cancel.Execute(); return; }

            // We also wanna check if we can cancel into some other stuff, like jumping
            else if(CanCancelInto(MoveType.Jump) && inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Jump) != null)
            {
                JumpType jumpType = currentMove.Blocked ? JumpType.Normal : JumpType.Combo;
                Jump(Stats.JumpForce, jumpType);
                TransitionTo(PlayerState.Jumping);
                return;
            }
        }
        if (currentMove.Frame <= startupEnd)
        {
            // startup — not yet active, apply startup velocity
        }
        else if (currentMove.Frame <= activeEnd)
        {
            // active — hitbox is out
        }
        else if (currentMove.Frame <= recoveryEnd)
        {
            // recovery — attack is done, waiting to act again
        }
        else
        {
            TransitionTo(physics.IsOnFloor ? PlayerState.Idle : PlayerState.Jumping);
        }
    }
    protected virtual void HandleCrouchAttackingState()
    {
        Decelerate();
        currentMove.Frame++;

        int startupEnd  = currentMove.Data.Startup;
        int activeEnd   = startupEnd + currentMove.Data.Active;
        int recoveryEnd = activeEnd  + currentMove.Data.Recovery;

        if (currentMove.HitLanded)
        {
            Move cancel = CheckMoveList();
            if (cancel != null) { cancel.Execute(); return; }
            
            // We also wanna check if we can cancel into some other stuff, like jumping
            else if(CanCancelInto(MoveType.Jump) && inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Jump) != null)
            {
                // Combo jumps are done by just jumping, no jumpsquat
                JumpType jumpType = currentMove.Blocked ? JumpType.Normal : JumpType.Combo;
                Jump(Stats.JumpForce, jumpType);
                TransitionTo(PlayerState.Jumping);
                return;
            }
        }
        if (currentMove.Frame <= startupEnd)
        {
            // startup — not yet active, apply startup velocity
        }
        else if (currentMove.Frame <= activeEnd)
        {
            // active — hitbox is out
        }
        else if (currentMove.Frame <= recoveryEnd)
        {
            // recovery — attack is done, waiting to act again
        }
        else
        {
            TransitionTo(PlayerState.Crouching);
        }
    }
    protected virtual void HandleAirAttackingState()
    {
        currentMove.Frame++;

        int startupEnd  = currentMove.Data.Startup;
        int activeEnd   = startupEnd + currentMove.Data.Active;
        int recoveryEnd = activeEnd  + currentMove.Data.Recovery;

        if (currentMove.HitLanded)
        {
            Move cancel = CheckMoveList();
            if (cancel != null) { cancel.Execute(); return; }

            // We also wanna check if we can cancel into some other stuff, like jumping
            else if (CanCancelInto(MoveType.Jump) && inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Jump) != null && jump.HasDoubleJump)
            {
                JumpType jumpType = currentMove.Blocked ? JumpType.Normal : JumpType.Combo;
                Jump(Stats.DoubleJumpForce, jumpType);
                jump.HasDoubleJump = false;
                updateFacing(true); 
                TransitionTo(PlayerState.Jumping);
                return;
            }
            else if (CanCancelInto(MoveType.Dash) && (ForwardDashInputted() || BackDashInputted()) && jump.HasAirdash)
            {
                jump.AirDashDirection = ForwardDashInputted()
                    ? (GetFacing() == FacingDirection.Right ? 1 : -1)
                    : (GetFacing() == FacingDirection.Right ? -1 : 1);
                jump.HasAirdash = false;
                jump.AirdashFrame = 0;
                jump.CurrentJumpType = currentMove.Blocked ? JumpType.Normal : JumpType.Combo;
                updateFacing(true); 
                TransitionTo(PlayerState.Airdashing);
                return;
            }
        }
        if (currentMove.Frame <= startupEnd)
        {
            // startup — not yet active, apply startup velocity
        }
        else if (currentMove.Frame <= activeEnd)
        {
            // active — hitbox is out
        }
        else if (currentMove.Frame <= recoveryEnd)
        {
            // recovery — attack is done, waiting to act again
        }
        else
        {
            TransitionTo(physics.IsOnFloor ? PlayerState.Idle : PlayerState.Jumping);
        }
    }
    protected virtual void HandleHitstunState()
    {
        Decelerate(Stats.GroundedHitDecelerationSpeed);
        if (hitReaction.Timer.Advance())
            TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleAirHitstunState()
    {
        // Gravity is applied by ApplyGravity; landing is caught by ResolveFloorCollision -> Knockdown
        AirHitDecelerate();
        if (hitReaction.Timer.Advance())
            TransitionTo(PlayerState.Jumping);
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

        if (!inputBuffer[0].Down)
            TransitionTo(PlayerState.Idle);
    }

    protected virtual void HandleAirdashState()
    {
        jump.AirdashFrame++;
        HandleAirdashMovement();

        int totalAirdashDuration = Stats.AirDashDuration + Stats.AirDashStartup;
        if(jump.AirdashFrame > totalAirdashDuration)
        {
            jump.AirdashFrame = -1;
            TransitionTo(PlayerState.Jumping);
        }
    }

    // Looks through our movelist, and if theres a match on a move, we return some info on that move
    private Move CheckMoveList()
    {
        foreach (var move in moveList)
        {
            if (move.Condition(inputBuffer))
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
            ? inputBuffer.WasHeld(PlayerConstants.BufferWindow, f => f.Jump)
            : inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Jump) != null;
        if (!jumpInput) return false;
        JumpSquat();
        TransitionTo(PlayerState.JumpSquat);
        return true;
    }

    protected bool TryStartCrouch()
    {
        if (!inputBuffer[0].Down) return false;
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

        if (inputBuffer[0].Right || inputBuffer[0].Left)
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

        if (!(inputBuffer[0].Right || inputBuffer[0].Left))
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

        bool backHeld = GetFacing() == FacingDirection.Right ? inputBuffer[0].Left : inputBuffer[0].Right;
        if (!(inputBuffer[0].Right || inputBuffer[0].Left) && !inputBuffer[0].Dash)
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
        else if (inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Jump) is InputFrame jumpPress && jump.HasDoubleJump && jump.FramesSinceLastJump >= Stats.FramesUntilActionableAfterJump)
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
            updateFacing(true); 
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
        if (IsAttacking()){
            currentBufferWindow = PlayerConstants.BufferWindow;
            currentMove = null;
        }
        if (currentState == PlayerState.GrabOccurring)
        {
            currentBufferWindow = PlayerConstants.BufferWindow;
            currentMove = null;
        }
        if (currentState == PlayerState.GrabHit || currentState == PlayerState.HitByGrab)
            grabPartner = null;

        // reset state-specific that we are moving into
        if (newState == PlayerState.JumpSquat)
        {
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
            jump.AirdashFrame = 0;
        if (newState == PlayerState.AirAttacking || newState == PlayerState.StandAttacking || newState == PlayerState.CrouchAttacking){
            currentBufferWindow = PlayerConstants.CancelBufferWindow;
        }
        if (newState == PlayerState.GrabOccurring)
            currentBufferWindow = PlayerConstants.CancelBufferWindow;
        if (newState == PlayerState.GrabHit || newState == PlayerState.HitByGrab)
            grabSequence.Timer.Frame = 0;
        if (newState == PlayerState.Hitstun || newState == PlayerState.AirHitstun)
            hitReaction.Timer.Frame = 0;
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
        if (inputBuffer[0].Right)
            physics.VelocityX = Stats.Speed;
        else if (inputBuffer[0].Left) 
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
        if(jump.AirdashFrame <= Stats.AirDashStartup)
        {
            physics.VelocityX = 0;
        }
        else
        {
            physics.VelocityX = Stats.AirDashSpeed * jump.AirDashDirection;
        }
        physics.VelocityY = 0;
    }

    protected virtual void HandleAerialMovement()
    {
        // Does nothing 
    }

    protected virtual void Decelerate(float? speed = null)
    {
        float deceleration = speed ?? (Math.Abs(physics.VelocityX) > Stats.StrongDecelerationThreshold
            ? Stats.StrongDecelerationSpeed
            : Stats.DecelerationSpeed);

        if (physics.VelocityX > 0)
            physics.VelocityX = Math.Max(0, physics.VelocityX - deceleration);
        else
            physics.VelocityX = Math.Min(0, physics.VelocityX + deceleration);
    }

    protected virtual void AirHitDecelerate()
    {
        // Air knockback is a little weird
        // At the top of the arc, we decelerate
        // We cannot decelerate more than a certain amount
        // Once we leave the top of the arc, we always go to some minimum speed
        if (Math.Abs(physics.VelocityY) <= Stats.FloatingGravityThreshold)
        {
            float deceleration = Stats.AirHitDecelerationSpeed;
            if (physics.VelocityX > 0)
                physics.VelocityX = Math.Max(Stats.AirHitMinimumSpeed, physics.VelocityX - deceleration);
            else
                physics.VelocityX = Math.Min(-Stats.AirHitMinimumSpeed, physics.VelocityX + deceleration);
        }
        else if (physics.VelocityY < -Stats.FloatingGravityThreshold)
        {
            // Left the top of the arc, go to the minimum speed
            physics.VelocityX = Math.Sign(physics.VelocityX) * Stats.AirHitMinimumSpeed;
        }
    }

    protected virtual void SetupJumpDirection()
    {
        // Prefer the direction held at the jump press edge (respects buffered inputs). When jump is
        // held through a landing there is no fresh press edge, so fall back to the currently held
        // direction — this lets hold-jump bunny-hopping keep its forward/back momentum.
        InputFrame source = inputBuffer.WasJustPressed(PlayerConstants.BufferWindow, f => f.Jump) ?? inputBuffer[0];
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

    protected virtual void Jump(float jumpForce, JumpType jumpType)
    {
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
        Position = new Vector3(
            Position.X + physics.VelocityX * PlayerConstants.FixedDelta,
            Position.Y + physics.VelocityY * PlayerConstants.FixedDelta,
            Position.Z
        );
    }

    protected virtual void ResolveFloorCollision()
    {
        if (Position.Y <= PlayerConstants.FloorY)
        {
            Position = new Vector3(Position.X, PlayerConstants.FloorY, Position.Z);
            physics.VelocityY = 0;
            if (!physics.IsOnFloor)
            {
                // just landed this frame
                jump.HasDoubleJump = true;
                jump.HasAirdash = true;
                jump.FramesSinceLastJump = 0;
                if (currentState == PlayerState.AirHitstun)
                    TransitionTo(PlayerState.Knockdown);
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
        }
        else
        {
            physics.IsOnFloor = false;
        }
    }
}