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
// The sim reads the log through InputView (Player.Inputs), which pairs a log reference with
// the Consumed ring buffer (sim state, lives on Player) and the current tick number so that
// queries like WasJustPressed can access both inputs and their consumed marks by "i frames ago"
// while everything underneath is indexed by absolute frame number for rollback safety.
public class InputLog
{
    private readonly InputFrame[] slots;
    public int Size => slots.Length;

    public InputLog(int size) { slots = new InputFrame[size]; }

    // Absolute-tick read/write. The modulo handles both the ring wrap (frame > Size) and
    // negative "before tick 0" lookups (default(InputFrame) reads as "no buttons pressed").
    public InputFrame Get(int frame) => slots[Index(frame)];
    public void Set(int frame, InputFrame input) => slots[Index(frame)] = input;
    public InputFrame this[int frame] => slots[Index(frame)];

    private int Index(int frame) => ((frame % slots.Length) + slots.Length) % slots.Length;
}

// Sim-facing view over a Player's InputLog and ConsumedMarks, anchored at a specific tick.
// Created cheaply on demand (Player.Inputs). Callers use `inputs.WasJustPressed(...)` etc.
// (extension methods on InputView in InputQuery) — the view exposes both raw inputs by "i
// frames ago" and per-button consumed marks by Button, hiding the absolute-frame indexing
// underneath.
public struct InputView
{
    public InputLog Log;
    public Player Owner;      // holds the ConsumedMarks that MarkConsumed writes through
    public int CurrentFrame;

    public int BufferSize => Log.Size;

    // Input i frames before CurrentFrame (matches the old Buffer[i] semantics).
    public InputFrame this[int i] => Log[CurrentFrame - i];

    // Read/write the "last consumed frame" for a specific button. Writes reach Owner.Marks
    // directly — Marks is a struct FIELD on Player (not a property) so struct-method mutation
    // updates in place.
    public readonly int MarkFor(Button b) => Owner.Marks.Get(b);
    public void SetMarkFor(Button b, int frame) => Owner.Marks.Set(b, frame);
}
