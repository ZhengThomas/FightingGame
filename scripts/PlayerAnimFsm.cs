using System;

// The animation state machine, kept separate from PlayerState because the two don't line up:
// RunStop is still playing while the player is already Idle, and the sim has no opinion about that.
//
// Each state maps to exactly one clip, which is what makes "seek to Frame" always land in the right
// place. Where the clip differs (walk forward vs back, rise vs fall) the states are separate too,
// so entering one resets Frame and the clip starts from 0.
//
// Lives in PlayerSnapshot and steps once per sim tick, so a rollback restores it like anything else
// and the animator node holds no timeline of its own.
public enum AnimState
{
    // Idle is deliberately first: a zeroed AnimatorState reads as "standing still".
    Idle,
    WalkForward, WalkBack,
    RunStart, Run, RunStop,
    CrouchDown, Crouch, StandUp,
    Backdash,
    JumpSquat, JumpRise, Fall, Landing,
    AirdashForward, AirdashBack,
    Attack,
    Hurt, AirHurtRise, AirHurtFall,
    SoftKnockdown, HardKnockdown, Wakeup,
    Block, CrouchBlock, AirBlock,
}

public struct AnimatorState
{
    public AnimState Current;
    public int Frame;      // ticks since entering Current; the clip plays from here
    public int ClipToken;  // when this changes the clip restarts even if Current didn't change
}

public static class PlayerAnimFsm
{
    // Frame counts for the four one-shot clips. These have to match the actual animation lengths —
    // the FSM can't read them off the AnimationPlayer, since it runs sim-side.
    public const int RunStartFrames = 6;
    public const int RunStopFrames = 22;
    public const int CrouchDownFrames = 15;
    public const int StandUpFrames = 15;

    // Advance one tick. Pure function of the previous animator state and the player's current sim
    // state, so re-running a tick after a rollback produces the same result.
    public static AnimatorState Step(AnimatorState s, Player p)
    {
        AnimState desired = DesiredState(p);

        // A one-shot that hasn't run out keeps playing unless something cuts it short.
        if (IsOneShot(s.Current) && s.Frame + 1 < LengthOf(s.Current) && !Interrupts(s.Current, desired))
            return new AnimatorState { Current = s.Current, Frame = s.Frame + 1, ClipToken = s.ClipToken };

        AnimState next = WithTransition(s.Current, desired);
        int token = IsOneShot(next) ? 0 : TokenFor(p);

        if (next != s.Current || token != s.ClipToken)
            return new AnimatorState { Current = next, Frame = 0, ClipToken = token };

        return new AnimatorState { Current = s.Current, Frame = s.Frame + 1, ClipToken = s.ClipToken };
    }

    // The looping state the player's sim state calls for, ignoring any transition clip.
    static AnimState DesiredState(Player p)
    {
        // Attacks name their own clip, so one state covers all of them.
        if (p.IsAttacking() && p.CurrentMove.HasMove && !string.IsNullOrEmpty(p.CurrentMove.Data?.AnimationName))
            return AnimState.Attack;

        return p.CurrentState switch
        {
            PlayerState.Walking          => MovingForward(p) ? AnimState.WalkForward : AnimState.WalkBack,
            PlayerState.GroundDashing    => AnimState.Run,
            PlayerState.Backdashing      => AnimState.Backdash,
            PlayerState.Crouching        => AnimState.Crouch,
            PlayerState.CrouchAttacking  => AnimState.Crouch,   // only reached if the move has no clip
            PlayerState.JumpSquat        => AnimState.JumpSquat,
            PlayerState.Jumping or PlayerState.AirAttacking
                                         => p.Physics.VelocityY > 0 ? AnimState.JumpRise : AnimState.Fall,
            PlayerState.Airdashing       => AirdashIsForward(p) ? AnimState.AirdashForward : AnimState.AirdashBack,
            PlayerState.Landing          => AnimState.Landing,
            PlayerState.Hitstun          => AnimState.Hurt,
            PlayerState.AirHitstun       => p.Physics.VelocityY > 0 ? AnimState.AirHurtRise : AnimState.AirHurtFall,
            PlayerState.SoftKnockdown    => AnimState.SoftKnockdown,
            PlayerState.Knockdown        => AnimState.HardKnockdown,
            PlayerState.Wakeup           => AnimState.Wakeup,
            PlayerState.Blockstun        => AnimState.Block,
            PlayerState.CrouchBlockstun  => AnimState.CrouchBlock,
            PlayerState.AirBlockstun     => AnimState.AirBlock,
            _ => AnimState.Idle, // stand attacks without a clip, and the grab states
        };
    }

    // Restarts the clip when it changes: a new attack in a chain, or a fresh hit while already hurt.
    static int TokenFor(Player p)
    {
        if (p.IsAttacking() && p.CurrentMove.HasMove) return p.CurrentMove.InstanceId;
        if (IsReaction(p.CurrentState)) return p.ReactionFlashId;
        return 0;
    }

    // Slots a one-shot in front of the destination state where one exists.
    //
    // The current side is read through EffectiveLoop, so a one-shot counts as the loop it stands
    // in for: crouching again during StandUp has to play CrouchDown, not snap straight to Crouch.
    static AnimState WithTransition(AnimState current, AnimState desired)
    {
        AnimState from = EffectiveLoop(current);

        if (desired == AnimState.Run && from != AnimState.Run)
            return AnimState.RunStart;
        // RunStart excepted: letting a dash go early plays the start clip out and lands on idle,
        // rather than running a stop clip for a dash that never got going.
        if (from == AnimState.Run && IsStanding(desired) && current != AnimState.RunStart)
            return AnimState.RunStop;
        if (desired == AnimState.Crouch && IsStanding(from))
            return AnimState.CrouchDown;
        if (from == AnimState.Crouch && IsStanding(desired))
            return AnimState.StandUp;
        return desired;
    }

    // The loop a one-shot is standing in for. RunStop is visually still stopping, but as far as
    // picking the next transition goes the player is already standing.
    static AnimState EffectiveLoop(AnimState s) => s switch
    {
        AnimState.RunStart   => AnimState.Run,
        AnimState.RunStop    => AnimState.Idle,
        AnimState.CrouchDown => AnimState.Crouch,
        AnimState.StandUp    => AnimState.Idle,
        _ => s,
    };

    // Whether `desired` is allowed to cut a running one-shot short.
    static bool Interrupts(AnimState oneShot, AnimState desired) => oneShot switch
    {
        // Snappy — standing back up mid-crouch cuts these instantly, so teabagging stays responsive.
        // Anything other than the state they're heading for cuts them, walking included.
        AnimState.CrouchDown => desired != AnimState.Crouch,
        AnimState.StandUp    => desired != AnimState.Idle,
        // Committed — idle/walk flicker can't kill the dash start/stop, but a real action can.
        // Run interrupts RunStop (re-dashing restarts from RunStart) but not RunStart, which is
        // already on its way there.
        AnimState.RunStart   => !IsStanding(desired) && desired != AnimState.Run,
        AnimState.RunStop    => !IsStanding(desired),
        _ => true,
    };

    public static bool IsOneShot(AnimState s)
        => s == AnimState.RunStart || s == AnimState.RunStop
        || s == AnimState.CrouchDown || s == AnimState.StandUp;

    public static int LengthOf(AnimState s) => s switch
    {
        AnimState.RunStart   => RunStartFrames,
        AnimState.RunStop    => RunStopFrames,
        AnimState.CrouchDown => CrouchDownFrames,
        AnimState.StandUp    => StandUpFrames,
        _ => 0,
    };

    static bool IsStanding(AnimState s)
        => s == AnimState.Idle || s == AnimState.WalkForward || s == AnimState.WalkBack;

    static bool IsReaction(PlayerState s)
        => s == PlayerState.Hitstun || s == PlayerState.AirHitstun
        || s == PlayerState.Blockstun || s == PlayerState.CrouchBlockstun
        || s == PlayerState.AirBlockstun;

    // Standing still counts as forward, matching the old WalkAnimForDirection.
    static bool MovingForward(Player p)
    {
        int vx = p.Physics.VelocityX;
        if (Math.Abs(vx) < 1) return true;
        return p.GetFacing() == FacingDirection.Right ? vx > 0 : vx < 0;
    }

    static bool AirdashIsForward(Player p)
        => p.GetFacing() == FacingDirection.Right ? p.AirDashDirection > 0 : p.AirDashDirection < 0;
}
