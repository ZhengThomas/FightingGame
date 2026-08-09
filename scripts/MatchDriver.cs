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

    // Simulated netplay dev knobs.
    // P1 counts as ours and writes straight to its log. P2 stands in for the opponent: their keys
    // go through FakeNetwork and only reach the log once "delivered".
    public bool SimulateNetplay = true;

    // Input delta Needs to be >= FakeLatencyTicks + FakeJitterTicks or the game will
    // constantly have to stall Set it lower on purpose to watch that happen; rollback is what
    // eventually lets it come down.
    public int InputDelay = 8;
    public int FakeLatencyTicks = 6;   // ~100ms at 60fps
    public int FakeJitterTicks = 2;
    public int FakeDropPercent = 30;

    FakeNetwork fakeNet;
    // Advances every real frame, including stalled ones — delivery happens in wall time, not sim
    // time, so a stall must not also stop packets arriving.
    int netTick;
    bool primed;
    int stalledFrames;

    public override void _Ready()
    {
        debug = GetNode<DebugManager>("/root/DebugManager");
        inputManager = GetNode<InputManager>("/root/InputManager");
        match = GetNode<MatchManager>("/root/MatchManager");
        fakeNet = new FakeNetwork(seed: 12345);
    }

    // How many ticks the self-test rewinds and replays. Small enough to be cheap, long enough to
    // cross a hit or a state change.
    const int RollbackTestDepth = 15;

    public override void _PhysicsProcess(double delta)
    {
        if (debug.ConsumeShouldTick())
        {
            if (SimulateNetplay)
            {
                StepSimulatedNetplay();
            }
            else
            {
                // Inputs are recorded under the frame the tick is about to consume.
                inputManager.PollLocalInputs(match.FrameCount);
            }

            // if an input for this frame hasn't arrived, don't advance. Both sides do the same,
            // so they stall together and stay in step.
            if (!SimulateNetplay || HaveConfirmedInputs(match.FrameCount))
            {
                match.Tick();

                if (debug.RollbackTestEnabled)
                    RunRollbackSelfTest();
            }
            else
            {
                // We haven't received the input yet so were gonna stall
                stalledFrames++;
            }
        }

        // Both run whether or not the sim advanced, so the view stays correct while frozen.
        match.SyncPresentation();
        match.DrawDebugBoxes();
    }

    // One real frame of the pretend connection
    // p1 input goes straight into the log and immediately consumed,
    // opponent's takes a detour through FakeNetwork.
    void StepSimulatedNetplay()
    {
        EnsurePrimed();

        int inputFrame = match.FrameCount + InputDelay;

        fakeNet.DelayTicks = FakeLatencyTicks;
        fakeNet.JitterTicks = FakeJitterTicks;
        fakeNet.DropPercent = FakeDropPercent;

        inputManager.OwnsPlayer1 = true;
        inputManager.OwnsPlayer2 = false;
        inputManager.PollLocalInputs(inputFrame);

        if (match.Player2 != null)
        {
            fakeNet.Send(inputFrame, inputManager.ReadInputFor(2), netTick);
            foreach ((int frame, InputFrame input) in fakeNet.Receive(netTick))
                match.Player2.InputLog.SetConfirmed(frame, input);
            // Tell the sender how far we've got, so it stops resending what landed and keeps
            // resending what didn't. Without this a long enough burst of drops would strand a
            // frame forever and the game would wait on it for good.
            fakeNet.Acknowledge(match.Player2.InputLog.LastConfirmedFrame);
        }

        netTick++;
    }

    // The first InputDelay frames have no input written to them — the delay means writing starts
    // at frame InputDelay. Without filling them the game would wait forever for frame 0.
    void EnsurePrimed()
    {
        if (primed || match.Player1 == null || match.Player2 == null) return;
        for (int f = 0; f < InputDelay; f++)
        {
            match.Player1.InputLog.SetConfirmed(f, default);
            match.Player2.InputLog.SetConfirmed(f, default);
        }
        primed = true;
    }

    // True once both players' inputs for `frame` have actually arrived.
    bool HaveConfirmedInputs(int frame)
    {
        if (match.Player1 != null && match.Player1.InputLog.LastConfirmedFrame < frame) return false;
        if (match.Player2 != null && match.Player2.InputLog.LastConfirmedFrame < frame) return false;
        return true;
    }

    // Frames skipped waiting on input since startup — how choppy the pretend connection is.
    public int StalledFrames => stalledFrames;

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
