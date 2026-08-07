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
// Writer model (scaffolded here for netplay; currently only the local case is used):
//   - LOCAL player: InputManager polls the keyboard once per real physics frame and calls
//     Set(FrameCount, live-keys). This is the sole place `Input.IsKeyPressed` is called.
//   - REMOTE player (future): a NetworkManager will call Set(N, confirmedInput) as inputs
//     arrive from the peer, and Set(N, predictedInput) for frames that haven't arrived yet.
//     A per-slot confirmed/predicted flag can be added when we need to detect mispredictions
//     (that's when to rollback). For now the class doesn't track that — the shape is right
//     to layer it on later.
//
// The sim reads the log through InputView (Player.Inputs), which pairs a log reference with the
// ConsumedMarks (sim state, lives on Player), MatchManager's per-frame hit-pause record, and the
// current tick number, so that queries like WasJustPressed can address inputs, their consumed
// marks, and whether each tick was frozen by "i frames ago" while everything underneath is
// indexed by absolute frame number for rollback safety.
public class InputLog : FrameRing<InputFrame>
{
    public InputLog(int size) : base(size) { }
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

    // Input i frames before CurrentFrame (matches the old Buffer[i] semantics).
    public InputFrame this[int i] => Log[CurrentFrame - i];

    // True if hit pause froze the sim body i frames back. Frozen ticks don't count against
    // buffer windows.
    public readonly bool WasHitPause(int i) => HitPause[CurrentFrame - i];

    // Read/write the "last consumed frame" for a specific button. Writes reach Owner.Marks
    // directly — Marks is a struct FIELD on Player (not a property) so struct-method mutation
    // updates in place.
    public readonly int MarkFor(Button b) => Owner.Marks.Get(b);
    public void SetMarkFor(Button b, int frame) => Owner.Marks.Set(b, frame);
}
