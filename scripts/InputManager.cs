using Godot;
using System;
using System.Linq;

public struct InputFrame
{
    public bool Left;
    public bool Right;
    public bool Up;
    public bool Down;
    public bool Jump;
    public bool LightAttack;
    public bool MediumAttack;
    public bool HeavyAttack;
    public bool Dash;
    // Inputs during hitpause are recorded but don't decrement the input buffer window
    public bool Grab;
    public bool isDuringHitpause;
}

public struct InputBindings
{
    public Key Left, Right, Up, Down;
    public Key LightAttack, MediumAttack, HeavyAttack, Dash;
    public Key Grab;
}

// Facing-relative directional zones. All motion inputs are defined in these terms
// so they automatically work regardless of which way the player is facing.
public enum Direction
{
    Neutral,
    Forward, Back,
    Up, Down,
    UpForward, UpBack,
    DownForward, DownBack,
}

public struct MotionStep
{
    public Direction Dir;
    public bool Optional; // if true, this step is consumed if found but skipped if not
}

// A motion input defined as a sequence of directional zones that must be visited
// in order within a time window.
public struct MotionInput
{
    public MotionStep[] Sequence;  // chronological order, oldest step first
    public int TotalWindow;        // the whole motion must fit within this many frames
    public int MaxStepGap;         // max frames allowed between any two consecutive steps
    public int CheatableAmount;    // How many optional steps can be skipped if they are not found
}

// Charge input, hold dir then other dir
public struct ChargeInput
{
    public Direction[]   ChargeDirs;     // directions to hold (e.g., Back, Down)
    public Direction[] ReleaseDirs;   // any of these count as a valid release
    public static readonly int ChargeFrames = 30;          // minimum consecutive frames ChargeDir must be held
    public static readonly int ReleaseWindow = 8;         // release must have occurred within this many recent frames
    public static readonly int MaxReleaseGap = 6;         // max frames of non-charge allowed between charge and release
}

// Static query helpers — operate on any InputFrame[] buffer, not tied to a specific player.
// This is rollback-friendly: the same logic runs whether the buffer came from hardware,
// a network packet, or a saved snapshot.
public static class InputQuery
{
    // Returns the matched InputFrame if a button transitioned from unpressed to pressed within the last
    // bufferWindow non-hitpause frames, or null if no match. Callers can read Left/Right/etc. from the
    // returned frame to get input state at the exact moment of the press.
    public static InputFrame? WasJustPressed(
        this InputFrame[] buffer, int bufferWindow,
        Func<InputFrame, bool> pressedInput,
        Func<InputFrame, bool>[] heldInputs = null,
        Func<InputFrame, bool>[] notPressedInputs = null)
    {
        int i = 0;
        int nonHitpauseFramesSeen = 0;

        while (nonHitpauseFramesSeen < bufferWindow && i < buffer.Length)
        {
            bool allPressedThisFrame = true;

            if (!pressedInput(buffer[i]))
                allPressedThisFrame = false;

            if (allPressedThisFrame && heldInputs != null)
                foreach (var btn in heldInputs)
                    if (!btn(buffer[i])) { allPressedThisFrame = false; break; }

            if (allPressedThisFrame && notPressedInputs != null)
                foreach (var btn in notPressedInputs)
                    if (btn(buffer[i])) { allPressedThisFrame = false; break; }

            if (allPressedThisFrame && (i + 1 >= buffer.Length || !pressedInput(buffer[i + 1])))
                return buffer[i];

            if (!buffer[i].isDuringHitpause)
                nonHitpauseFramesSeen++;

            i++;
        }
        return null;
    }

    // True if all buttons were held simultaneously at any point in the last bufferWindow frames.
    public static bool WasHeld(this InputFrame[] buffer, int bufferWindow, params Func<InputFrame, bool>[] buttons)
    {
        for (int i = 0; i < bufferWindow; i++)
        {
            bool allHeld = true;
            foreach (var btn in buttons)
                if (!btn(buffer[i])) { allHeld = false; break; }
            if (allHeld) return true;
        }
        return false;
    }

    // True if the directional sequence described by motion was performed recently.
    public static bool WasMotion(this InputFrame[] buffer, bool facingRight, MotionInput motion, int startFrom = 0)
    {
        var seq = motion.Sequence;
        int lastFoundFrame = -1;
        int cheatableAmount = motion.CheatableAmount;

        // First we find the very last input of the motion input 
        // Hitpause doesnt count down the buffer window for the last input of the motion input, for game feel
        int i = startFrom;
        int maxSearch = PlayerConstants.BufferWindow;
        int totalWindow = motion.TotalWindow;
        while (i < buffer.Length && i < maxSearch)
        {
            if (ClassifyDirection(buffer[i], facingRight) == seq[seq.Length - 1].Dir)
            {
                lastFoundFrame = i;
                break;
            }
            if (buffer[i].isDuringHitpause)
            {
                maxSearch++;
                totalWindow++;
            }
            i++;
        }

        // We couldnt find the last input of the motion input, give up
        if (lastFoundFrame < 0) return false;

        // Then we find every other input in the motion input in reverse order
        for (int s = seq.Length - 2; s >= 0; s--) // -2 because we skip the last input
        {
            MotionStep step = seq[s];
            int searchFrom = lastFoundFrame < 0 ? 0 : lastFoundFrame + 1;
            int searchTo = Math.Min(searchFrom + motion.MaxStepGap, totalWindow);

            bool found = false;
            for (int f = searchFrom; f <= searchTo && f < buffer.Length; f++)
            {
                if (ClassifyDirection(buffer[f], facingRight) == step.Dir)
                {
                    lastFoundFrame = f;
                    found = true;
                    break;
                }
            }
            // Cant skip this step, give up
            if (!found && !step.Optional)
            {
                return false;
            }
            // We might be able to cheat out this step
            if (!found && step.Optional)
            {
                if (cheatableAmount == 0)
                {
                    return false;
                }
                cheatableAmount--;
            }
        }
        
        return true;
    }

    // True if a charge input that was specified was performed recently
    public static bool WasCharge(this InputFrame[] buffer, bool facingRight, ChargeInput charge)
    {
        // Step 1: find the most recent frame with any release direction within ReleaseWindow.
        // Hitpause doesnt count down the buffer window for the last input of the motion input, for game feel
        int release = -1;
        int maxSearch = ChargeInput.ReleaseWindow;
        for (int f = 0; f < maxSearch && f < buffer.Length; f++)
        {
            if (charge.ReleaseDirs.Any(d => d == ClassifyDirection(buffer[f], facingRight)))
            {
                release = f;
            }
            if(buffer[f].isDuringHitpause)
            {
                maxSearch++;
            }
        }
        if (release < 0) return false;

        // Step 2: within MaxReleaseGap frames after the release phase ended, find ChargeDir.
        // This gap covers neutral frames between letting go of charge and pressing release.
        int chargeStart = -1;
        for (int f = release + 1; f <= release + 1 + ChargeInput.MaxReleaseGap && f < buffer.Length; f++)
        {
            if (charge.ChargeDirs.Any(d => d == ClassifyDirection(buffer[f], facingRight)))
            {
                chargeStart = f;
                break;
            }
        }
        if (chargeStart < 0) return false;

        // Step 4: count consecutive ChargeDir frames going further back in time.
        int count = 0;
        for (int f = chargeStart; f < buffer.Length; f++)
        {
            if (charge.ChargeDirs.Any(d => d == ClassifyDirection(buffer[f], facingRight)))
                count++;
            else
                break;
        }
        return count >= ChargeInput.ChargeFrames;
    }

    private static Direction ClassifyDirection(InputFrame f, bool facingRight)
    {
        bool fwd  = facingRight ? f.Right : f.Left;
        bool back = facingRight ? f.Left  : f.Right;

        if (fwd  && f.Down) return Direction.DownForward;
        if (back && f.Down) return Direction.DownBack;
        if (fwd  && f.Up)   return Direction.UpForward;
        if (back && f.Up)   return Direction.UpBack;
        if (fwd)            return Direction.Forward;
        if (back)           return Direction.Back;
        if (f.Up)           return Direction.Up;
        if (f.Down)         return Direction.Down;
        return Direction.Neutral;
    }
}

// Predefined motion inputs. All sequences are in chronological order (oldest step first).
// Diagonals between cardinal directions are optional — the cardinals form the required backbone.
public static class MotionInputs
{
    static MotionStep R(Direction z) => new MotionStep { Dir = z, Optional = false };
    static MotionStep O(Direction z) => new MotionStep { Dir = z, Optional = true  };

    public static readonly MotionInput QCF = new MotionInput
    {
        Sequence = [R(Direction.Down), O(Direction.DownForward), R(Direction.Forward)],
        TotalWindow = 20,
        MaxStepGap = 10,
        CheatableAmount = 1,
    };

    public static readonly MotionInput QCB = new MotionInput
    {
        Sequence = [R(Direction.Down), O(Direction.DownBack), R(Direction.Back)],
        TotalWindow = 20,
        MaxStepGap = 10,
        CheatableAmount = 1,
    };

    public static readonly MotionInput HCF = new MotionInput
    {
        Sequence = [R(Direction.Back), O(Direction.DownBack), O(Direction.Down), O(Direction.DownForward), R(Direction.Forward)],
        TotalWindow = 30,
        MaxStepGap = 10,
        CheatableAmount = 2,
    };

    public static readonly MotionInput HCB = new MotionInput
    {
        Sequence = [R(Direction.Forward), O(Direction.DownForward), O(Direction.Down), O(Direction.DownBack), R(Direction.Back)],
        TotalWindow = 30,
        MaxStepGap = 10,
        CheatableAmount = 2,
    };

    // DP (623): forward → down → downforward. The diagonal at the end is required
    // since it's the defining final zone of the motion, not just a pass-through.
    public static readonly MotionInput DP = new MotionInput
    {
        Sequence = [R(Direction.Forward), R(Direction.Down), R(Direction.DownForward)],
        TotalWindow = 20,
        MaxStepGap = 10,
        CheatableAmount = 0,
    };
}

// Predefined charge inputs.
public static class ChargeInputs
{
    // [4] > 6
    public static readonly ChargeInput BackForward = new ChargeInput
    {
        ChargeDirs     = [Direction.Back, Direction.DownBack, Direction.UpBack],
        ReleaseDirs   = [Direction.Forward, Direction.DownForward, Direction.UpForward],
    };

    // [2] > 8
    public static readonly ChargeInput DownUp = new ChargeInput
    {
        ChargeDirs     = [Direction.Down, Direction.DownForward, Direction.DownBack],
        ReleaseDirs   = [Direction.Up, Direction.UpForward, Direction.UpBack],
    };
}

public partial class InputManager : Node
{
    public const int BufferSize = 60;

    // Per-player input buffers: index 0 = Player 1, index 1 = Player 2.
    // For rollback: inject a recorded InputFrame into the appropriate buffer slot
    // before calling Tick to replay that frame.
    public InputFrame[][] InputBuffers = new InputFrame[2][];

    static readonly InputBindings P1Bindings = new InputBindings
    {
        Left = Key.Left,
        Right = Key.Right,
        Up = Key.Up,
        Down = Key.Down,
        LightAttack = Key.Z,
        MediumAttack = Key.X,
        HeavyAttack = Key.C,
        Dash = Key.Shift,
        Grab = Key.V,
    };

    static readonly InputBindings P2Bindings = new InputBindings
    {
        Left = Key.A,
        Right = Key.D,
        Up = Key.W,
        Down = Key.S,
        LightAttack = Key.U,
        MediumAttack = Key.I,
        HeavyAttack = Key.O,
        Dash = Key.P,
        Grab = Key.L,
    };

    DebugManager debug;
    MatchManager match;

    public override void _Ready()
    {
        InputBuffers[0] = new InputFrame[BufferSize];
        InputBuffers[1] = new InputFrame[BufferSize];
        debug = GetNode<DebugManager>("/root/DebugManager");
        match = GetNode<MatchManager>("/root/MatchManager");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!debug.ShouldTick) return;
        ReadPlayerInput(0, P1Bindings);
        ReadPlayerInput(1, P2Bindings);
    }

    private void ReadPlayerInput(int playerIndex, InputBindings bindings)
    {
        var buffer = InputBuffers[playerIndex];

        for (int i = BufferSize - 1; i > 0; i--)
            buffer[i] = buffer[i - 1];

        bool left  = Input.IsKeyPressed(bindings.Left);
        bool right = Input.IsKeyPressed(bindings.Right);
        bool up    = Input.IsKeyPressed(bindings.Up);
        bool down  = Input.IsKeyPressed(bindings.Down);

        // SOCD resolution — opposing directions cancel to neutral
        if (left && right) { left = false; right = false; }
        if (up && down)    { up   = false; down  = false; }

        buffer[0] = new InputFrame
        {
            Left         = left,
            Right        = right,
            Up           = up,
            Down         = down,
            Jump         = up,
            LightAttack  = Input.IsKeyPressed(bindings.LightAttack),
            MediumAttack = Input.IsKeyPressed(bindings.MediumAttack),
            HeavyAttack  = Input.IsKeyPressed(bindings.HeavyAttack),
            Dash         = Input.IsKeyPressed(bindings.Dash),
            Grab         = Input.IsKeyPressed(bindings.Grab),
            isDuringHitpause = match.IsInHitPause,
        };
    }
}
