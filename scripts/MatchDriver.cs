using Godot;

// Owns the order of operations for one real frame: collect this tick's inputs, advance the sim,
// then draw. Nothing else runs _PhysicsProcess, so the sequence is defined here rather than
// emerging from autoload registration order.
//
// Rollback slots in between the input poll and the tick — restore a past snapshot, then call
// Tick() repeatedly to catch back up to the present. That needs a single caller that treats
// "how many ticks is this frame worth" as a decision, which is what this class is for.
public partial class MatchDriver : Node
{
    DebugManager debug;
    InputManager inputManager;
    MatchManager match;

    public override void _Ready()
    {
        debug = GetNode<DebugManager>("/root/DebugManager");
        inputManager = GetNode<InputManager>("/root/InputManager");
        match = GetNode<MatchManager>("/root/MatchManager");
    }

    // How many ticks the self-test rewinds and replays. Small enough to be cheap, long enough to
    // cross a hit or a state change.
    const int RollbackTestDepth = 15;

    public override void _PhysicsProcess(double delta)
    {
        if (debug.ConsumeShouldTick())
        {
            // Inputs are recorded under the frame the tick is about to consume.
            inputManager.PollLocalInputs(match.FrameCount);
            match.Tick();

            if (debug.RollbackTestEnabled)
                RunRollbackSelfTest();
        }

        // Both run whether or not the sim advanced, so the view stays correct while frozen.
        match.SyncPresentation();
        match.DrawDebugBoxes();
    }

    // Rewinds a few ticks and replays them with the inputs already in the log, then checks the
    // result is identical.
    //
    // Passing is invisible: the replay lands on the state the game was already in.
    void RunRollbackSelfTest()
    {
        if (!match.HasGameState(RollbackTestDepth)) return; // not enough history yet

        int frame = match.FrameCount;
        uint expected = match.StateChecksum();
        Snapshot before = match.CaptureSnapshot();

        match.RestoreSnapshot(match.GetGameState(RollbackTestDepth));
        for (int i = 0; i < RollbackTestDepth; i++)
            match.Tick();

        uint actual = match.StateChecksum();
        if (actual == expected) return;

        GD.PushError(
            $"Rollback self-test failed: replaying frames {frame - RollbackTestDepth}-{frame - 1} "
            + $"gave {actual:X8}, expected {expected:X8}. Some sim state isn't in the snapshot. "
            + "Test disabled — press the toggle again to re-arm.");

        // Put the game back where it was so a failure doesn't derail the session.
        match.RestoreSnapshot(before);
        debug.DisableRollbackTest();
    }
}
