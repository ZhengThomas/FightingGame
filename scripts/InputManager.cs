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
    public bool Grab;
}

// Identifies a single button, used by query helpers (WasJustPressed, MarkConsumed, WasHeld) so
// they can address both the input side (which field on InputFrame to read) and the mark side
// (which slot on ConsumedMarks to check/write) with one value.
public enum Button
{
    Left, Right, Up, Down, Jump,
    LightAttack, MediumAttack, HeavyAttack, Dash, Grab,
}

public static class InputHelpers
{
    // Number of Button enum values. Doubles as the bit width used by InputCodec.
    public const int ButtonCount = 10;

    public static bool IsPressed(InputFrame f, Button b) => b switch
    {
        Button.Left         => f.Left,
        Button.Right        => f.Right,
        Button.Up           => f.Up,
        Button.Down         => f.Down,
        Button.Jump         => f.Jump,
        Button.LightAttack  => f.LightAttack,
        Button.MediumAttack => f.MediumAttack,
        Button.HeavyAttack  => f.HeavyAttack,
        Button.Dash         => f.Dash,
        Button.Grab         => f.Grab,
        _ => false,
    };

    public static void Set(ref InputFrame f, Button b, bool pressed)
    {
        switch (b)
        {
            case Button.Left:         f.Left = pressed; break;
            case Button.Right:        f.Right = pressed; break;
            case Button.Up:           f.Up = pressed; break;
            case Button.Down:         f.Down = pressed; break;
            case Button.Jump:         f.Jump = pressed; break;
            case Button.LightAttack:  f.LightAttack = pressed; break;
            case Button.MediumAttack: f.MediumAttack = pressed; break;
            case Button.HeavyAttack:  f.HeavyAttack = pressed; break;
            case Button.Dash:         f.Dash = pressed; break;
            case Button.Grab:         f.Grab = pressed; break;
        }
    }
}

// Turns an input into a number, so we can send it over the internet. One bit in
// the number represents the button press of one button, in order of the struct
public static class InputCodec
{
    public static ushort Pack(InputFrame f)
    {
        ushort bits = 0;
        for (int i = 0; i < InputHelpers.ButtonCount; i++)
            if (InputHelpers.IsPressed(f, (Button)i))
                bits |= (ushort)(1 << i);
        return bits;
    }

    public static InputFrame Unpack(ushort bits)
    {
        InputFrame f = default;
        for (int i = 0; i < InputHelpers.ButtonCount; i++)
            InputHelpers.Set(ref f, (Button)i, (bits & (1 << i)) != 0);
        return f;
    }
}

// Per-button table that tells you when a input was last consumed
public struct ConsumedMarks
{
    // One int per Button. Kept as a small fixed array so a struct copy is a snapshot copy —
    // no separate "copy consumed" step to remember. Size = number of Button enum values.
    public const int SlotCount = 10;

    public int Left, Right, Up, Down, Jump;
    public int LightAttack, MediumAttack, HeavyAttack, Dash, Grab;

    // Factory for a fresh set of marks: -1 everywhere (no consumption yet).
    public static ConsumedMarks Empty => new ConsumedMarks
    {
        Left = -1, Right = -1, Up = -1, Down = -1, Jump = -1,
        LightAttack = -1, MediumAttack = -1, HeavyAttack = -1, Dash = -1, Grab = -1,
    };

    public readonly int Get(Button b) => b switch
    {
        Button.Left         => Left,
        Button.Right        => Right,
        Button.Up           => Up,
        Button.Down         => Down,
        Button.Jump         => Jump,
        Button.LightAttack  => LightAttack,
        Button.MediumAttack => MediumAttack,
        Button.HeavyAttack  => HeavyAttack,
        Button.Dash         => Dash,
        Button.Grab         => Grab,
        _ => -1,
    };

    public void Set(Button b, int frame)
    {
        switch (b)
        {
            case Button.Left:         Left = frame; break;
            case Button.Right:        Right = frame; break;
            case Button.Up:           Up = frame; break;
            case Button.Down:         Down = frame; break;
            case Button.Jump:         Jump = frame; break;
            case Button.LightAttack:  LightAttack = frame; break;
            case Button.MediumAttack: MediumAttack = frame; break;
            case Button.HeavyAttack:  HeavyAttack = frame; break;
            case Button.Dash:         Dash = frame; break;
            case Button.Grab:         Grab = frame; break;
        }
    }
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

// Static query helpers — operate on any InputView, not tied to a specific player.
// This is rollback-friendly: the same logic runs whether the underlying inputs came from
// hardware, a network packet, or a restored snapshot
public static class InputQuery
{
    // Returns the matched InputFrame if `button` transitioned from unpressed to pressed within
    // the last bufferWindow non-hitpause frames, or null if no match. Callers can read the
    // returned frame's other fields to inspect input state at the moment of the press.
    // A press whose frame is <= the button's mark (see ConsumedMarks) is treated as already used.
    public static InputFrame? WasJustPressed(
        this InputView inputs, int bufferWindow, Button button,
        Button[] heldButtons = null,
        Button[] notPressedButtons = null)
    {
        int i = 0;
        int nonHitpauseFramesSeen = 0;
        int mark = inputs.MarkFor(button);

        while (nonHitpauseFramesSeen < bufferWindow && i < inputs.BufferSize)
        {
            InputFrame frame = inputs[i];
            int frameNumber = inputs.CurrentFrame - i;
            bool allPressedThisFrame = true;

            if (!InputHelpers.IsPressed(frame, button))
                allPressedThisFrame = false;

            if (allPressedThisFrame && heldButtons != null)
                foreach (var b in heldButtons)
                    if (!InputHelpers.IsPressed(frame, b)) { allPressedThisFrame = false; break; }

            if (allPressedThisFrame && notPressedButtons != null)
                foreach (var b in notPressedButtons)
                    if (InputHelpers.IsPressed(frame, b)) { allPressedThisFrame = false; break; }

            // Already-consumed check: any prior fire for this button at frame >= frameNumber
            // suppresses this and all earlier presses.
            if (allPressedThisFrame && mark >= frameNumber)
                allPressedThisFrame = false;

            if (allPressedThisFrame && (i + 1 >= inputs.BufferSize || !InputHelpers.IsPressed(inputs[i + 1], button)))
                return frame;

            // Ticks the sim spent frozen don't burn buffer window.
            if (!inputs.WasHitPause(i))
                nonHitpauseFramesSeen++;

            i++;
        }
        return null;
    }

    // Set the "last consumed frame" for `button` to the absolute frame of the most recent
    // unconsumed rising edge inside the window. Everything at or before that frame becomes
    // implicitly consumed for the same button.
    public static void MarkConsumed(this InputView inputs, int bufferWindow, Button button)
    {
        int existingMark = inputs.MarkFor(button);
        for (int i = 0; i < bufferWindow && i < inputs.BufferSize; i++)
        {
            InputFrame frame = inputs[i];
            if (!InputHelpers.IsPressed(frame, button)) continue;
            int frameNumber = inputs.CurrentFrame - i;
            if (existingMark >= frameNumber) continue;
            if (i + 1 < inputs.BufferSize && InputHelpers.IsPressed(inputs[i + 1], button)) continue;

            inputs.SetMarkFor(button, frameNumber);
            return;
        }
    }

    // True if all buttons were held simultaneously at any point in the last bufferWindow frames.
    public static bool WasHeld(this InputView inputs, int bufferWindow, params Button[] buttons)
    {
        for (int i = 0; i < bufferWindow; i++)
        {
            InputFrame frame = inputs[i];
            bool allHeld = true;
            foreach (var b in buttons)
                if (!InputHelpers.IsPressed(frame, b)) { allHeld = false; break; }
            if (allHeld) return true;
        }
        return false;
    }

    // True if the directional sequence described by motion was performed recently.
    public static bool WasMotion(this InputView inputs, bool facingRight, MotionInput motion, int startFrom = 0)
    {
        var seq = motion.Sequence;
        int lastFoundFrame = -1;
        int cheatableAmount = motion.CheatableAmount;

        // First we find the very last input of the motion input
        // Hitpause doesnt count down the buffer window for the last input of the motion input, for game feel
        int i = startFrom;
        int maxSearch = PlayerConstants.BufferWindow;
        int totalWindow = motion.TotalWindow;
        while (i < inputs.BufferSize && i < maxSearch)
        {
            InputFrame frame = inputs[i];
            if (ClassifyDirection(frame, facingRight) == seq[seq.Length - 1].Dir)
            {
                lastFoundFrame = i;
                break;
            }
            if (inputs.WasHitPause(i))
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
            for (int f = searchFrom; f <= searchTo && f < inputs.BufferSize; f++)
            {
                if (ClassifyDirection(inputs[f], facingRight) == step.Dir)
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
    public static bool WasCharge(this InputView inputs, bool facingRight, ChargeInput charge)
    {
        // Step 1: find the most recent frame with any release direction within ReleaseWindow.
        // Hitpause doesnt count down the buffer window for the last input of the motion input, for game feel
        int release = -1;
        int maxSearch = ChargeInput.ReleaseWindow;
        for (int f = 0; f < maxSearch && f < inputs.BufferSize; f++)
        {
            InputFrame frame = inputs[f];
            if (charge.ReleaseDirs.Any(d => d == ClassifyDirection(frame, facingRight)))
            {
                release = f;
            }
            if (inputs.WasHitPause(f))
            {
                maxSearch++;
            }
        }
        if (release < 0) return false;

        // Step 2: within MaxReleaseGap frames after the release phase ended, find ChargeDir.
        // This gap covers neutral frames between letting go of charge and pressing release.
        int chargeStart = -1;
        for (int f = release + 1; f <= release + 1 + ChargeInput.MaxReleaseGap && f < inputs.BufferSize; f++)
        {
            if (charge.ChargeDirs.Any(d => d == ClassifyDirection(inputs[f], facingRight)))
            {
                chargeStart = f;
                break;
            }
        }
        if (chargeStart < 0) return false;

        // Step 4: count consecutive ChargeDir frames going further back in time.
        int count = 0;
        for (int f = chargeStart; f < inputs.BufferSize; f++)
        {
            if (charge.ChargeDirs.Any(d => d == ClassifyDirection(inputs[f], facingRight)))
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

    MatchManager match;

    public override void _Ready()
    {
        match = GetNode<MatchManager>("/root/MatchManager");
    }

    // This machine reads inputs for what players? Online its online one of the players,
    // Locally its both of the players. 
    public bool OwnsPlayer1 { get; set; } = true;
    public bool OwnsPlayer2 { get; set; } = true;

    // Record the locally-owned players' inputs under `frame`. MatchDriver calls this immediately
    // before the tick that consumes them.
    public void PollLocalInputs(int frame)
    {
        if (OwnsPlayer1) ReadPlayerInput(match.Player1, P1Bindings, frame);
        if (OwnsPlayer2) ReadPlayerInput(match.Player2, P2Bindings, frame);
    }

    // Read a player's keys without recording them. This is used with simulated netplay
    public InputFrame ReadInputFor(int playerNumber)
        => ReadBindings(playerNumber == 1 ? P1Bindings : P2Bindings);

    // Read live keyboard for this local player and record it into the durable per-tick log. The
    // sim reads through InputView, which addresses the log by absolute frame — any re-simulation
    // of a past tick reads the same input back from the same slot.
    //
    // A remote player's inputs come from a NetworkManager calling SetConfirmed / SetPredicted on
    // their log instead of this method.
    private void ReadPlayerInput(Player player, InputBindings bindings, int frame)
    {
        if (player == null) return;
        // Locally read, so it's real by definition — never a prediction.
        player.InputLog.SetConfirmed(frame, ReadBindings(bindings));
    }

    // The only place `Input.IsKeyPressed` is called.
    private static InputFrame ReadBindings(InputBindings bindings)
    {
        bool left  = Input.IsKeyPressed(bindings.Left);
        bool right = Input.IsKeyPressed(bindings.Right);
        bool up    = Input.IsKeyPressed(bindings.Up);
        bool down  = Input.IsKeyPressed(bindings.Down);

        // SOCD resolution — opposing directions cancel to neutral
        if (left && right) { left = false; right = false; }
        if (up && down)    { up   = false; down  = false; }

        return new InputFrame
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
        };
    }
}
