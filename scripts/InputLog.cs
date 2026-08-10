// Ring buffer of per-frame values indexed by absolute simulation tick number. The modulo handles
// both the ring wrap (frame > Size) and negative "before tick 0" lookups, which read back as
// default(T) — "no buttons pressed" for inputs, "not hit pause" for the freeze record.
public class FrameRing<T>
{
    private readonly T[] slots;
    public int Size => slots.Length;

    public FrameRing(int size) { slots = new T[size]; }

    public T Get(int frame) => slots[Index(frame)];
    public void Set(int frame, T value) => slots[Index(frame)] = value;
    public T this[int frame] => slots[Index(frame)];

    private int Index(int frame) => ((frame % slots.Length) + slots.Length) % slots.Length;
}

// Per-player ring buffer of per-frame inputs, indexed by absolute simulation tick number.
//
// Writer model:
//   - LOCAL player: InputManager polls the keyboard once per real physics frame and calls
//     SetConfirmed(FrameCount, live-keys). This is the sole place `Input.IsKeyPressed` is called.
//   - REMOTE player (not wired up yet): a NetworkManager calls SetConfirmed(N, input) as inputs
//     arrive from the peer, and SetPredicted(N, guess) for frames that haven't arrived. A
//     confirmed input replacing a prediction that disagreed is what triggers a rollback.
//
// The sim reads the log through InputView (Player.Inputs), which pairs a log reference with the
// ConsumedMarks (sim state, lives on Player), MatchManager's per-frame hit-pause record, and the
// current tick number, so that queries like WasJustPressed can address inputs, their consumed
// marks, and whether each tick was frozen by "i frames ago" while everything underneath is
// indexed by absolute frame number for rollback safety.
// One recorded input, plus whether it actually happened or was guessed. Online the opponent's
// inputs arrive a few frames late, so the gap gets filled with a prediction (normally "still
// holding what they last held") and corrected once the real one shows up.
public struct LoggedInput
{
    public InputFrame Input;
    public bool Confirmed;
    // Which frame this slot actually holds. The ring reuses slots every Size frames, so without
    // this a read for a frame that was never written returns whatever lived there Size frames ago
    // — silently, and as if it were real.
    public int Frame;
}

public class InputLog : FrameRing<LoggedInput>
{
    public InputLog(int size) : base(size) { }

    // Highest frame written at all, confirmed or guessed.
    public int NewestFrame { get; private set; } = -1;

    // Highest frame where everything up to and including it is confirmed. -1 = nothing received yet.
    public int LastConfirmedFrame { get; private set; } = -1;

    // A real input — the local keyboard, or one that arrived from the peer.
    public void SetConfirmed(int frame, InputFrame input)
    {
        Set(frame, new LoggedInput { Input = input, Confirmed = true, Frame = frame });
        if (frame > NewestFrame) NewestFrame = frame;
        // Bounded by NewestFrame so this can't walk into stale slots the ring hasn't overwritten.
        while (LastConfirmedFrame < NewestFrame && IsConfirmed(LastConfirmedFrame + 1))
            LastConfirmedFrame++;
    }

    // A guess, standing in until the real input arrives.
    public void SetPredicted(int frame, InputFrame input)
    {
        Set(frame, new LoggedInput { Input = input, Confirmed = false, Frame = frame });
        if (frame > NewestFrame) NewestFrame = frame;
    }

    public InputFrame InputAt(int frame) => SlotOf(frame).Input;
    public bool IsConfirmed(int frame) => SlotOf(frame).Confirmed;

    // The slot's contents if it really holds `frame`, otherwise an empty unconfirmed input — which
    // is the honest answer for a frame nothing was ever recorded for.
    LoggedInput SlotOf(int frame)
    {
        LoggedInput slot = this[frame];
        return slot.Frame == frame ? slot : default;
    }
}

// Sim-facing view over a Player's InputLog and ConsumedMarks, anchored at a specific tick.
// Created cheaply on demand (Player.Inputs). Callers use `inputs.WasJustPressed(...)` etc.
// (extension methods on InputView in InputQuery) — the view exposes both raw inputs by "i
// frames ago" and per-button consumed marks by Button, hiding the absolute-frame indexing
// underneath.
public struct InputView
{
    public InputLog Log;
    public FrameRing<bool> HitPause;  // MatchManager's per-frame "the sim body was frozen" record
    public Player Owner;      // holds the ConsumedMarks that MarkConsumed writes through
    public int CurrentFrame;

    public int BufferSize => Log.Size;

    // Input i frames before CurrentFrame (matches the old Buffer[i] semantics). The sim reads
    // inputs the same way whether they were confirmed or predicted — sorting that out is the
    // network layer's job, not the simulation's.
    public InputFrame this[int i] => Log.InputAt(CurrentFrame - i);

    // True if hit pause froze the sim body i frames back. Frozen ticks don't count against
    // buffer windows.
    public readonly bool WasHitPause(int i) => HitPause[CurrentFrame - i];

    // Read/write the "last consumed frame" for a specific button. Writes reach Owner.Marks
    // directly — Marks is a struct FIELD on Player (not a property) so struct-method mutation
    // updates in place.
    public readonly int MarkFor(Button b) => Owner.Marks.Get(b);
    public void SetMarkFor(Button b, int frame) => Owner.Marks.Set(b, frame);
}
