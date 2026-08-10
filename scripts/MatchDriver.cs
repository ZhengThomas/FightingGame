using Godot;

// Owns the order of operations for one real frame: collect this tick's inputs, advance the sim,
// then draw. Nothing else runs _PhysicsProcess, so the sequence is defined here rather than
// emerging from autoload registration order.
//
// It decides how many ticks a frame is worth — none while stalled, one normally, several when a
// replay is catching up. The connection itself lives in NetplaySession; this only asks what it
// knows and acts on the answer.
public partial class MatchDriver : Node
{
    DebugManager debug;
    InputManager inputManager;
    MatchManager match;

    // Play against a connection rather than sharing one keyboard.
    public bool SimulateNetplay = true;
    // Talk to another copy of the game for real, instead of the pretend connection. Needs
    // SimulateNetplay as well — this only chooses which kind.
    public bool UseRealNetwork = true;

    // Which character this instance drives. Only used for the pretend connection; a real one works
    // it out from which port it managed to grab.
    public int LocalPlayerNumber = 1;

    // InputDelay needs to be >= FakeLatencyTicks + FakeJitterTicks or the game constantly stalls.
    // Set it lower on purpose to watch that happen. The two aren't symmetrical: InputDelay costs
    // responsiveness on every input, RollbackFrames costs nothing until a guess is wrong — so bias
    // toward a small delay and a wide rollback window.
    public int InputDelay = 4;
    public int RollbackFrames = 4;
    public int FakeLatencyTicks = 6;   // ~100ms at 60fps
    public int FakeJitterTicks = 2;
    public int FakeDropPercent = 5;

    // Null when both players share this keyboard.
    NetplaySession session;
    int stalledFrames;

    // How many ticks the self-test rewinds and replays. Small enough to be cheap, long enough to
    // cross a hit or a state change.
    const int RollbackTestDepth = 15;

    public override void _Ready()
    {
        debug = GetNode<DebugManager>("/root/DebugManager");
        inputManager = GetNode<InputManager>("/root/InputManager");
        match = GetNode<MatchManager>("/root/MatchManager");

        if (SimulateNetplay)
            session = new NetplaySession(match, inputManager, MakeTransport(),
                                         InputDelay, RollbackFrames);
    }

    public override void _ExitTree() => session?.Shutdown();

    IInputTransport MakeTransport()
    {
        if (UseRealNetwork)
        {
            return new UdpTransport
            {
                ExtraLatencyTicks = FakeLatencyTicks,
                JitterTicks = FakeJitterTicks,
                DropPercent = FakeDropPercent,
            };
        }

        var fake = new FakeNetwork(seed: 12345)
        {
            LocalPlayerNumber = LocalPlayerNumber,
            DelayTicks = FakeLatencyTicks,
            JitterTicks = FakeJitterTicks,
            DropPercent = FakeDropPercent,
        };
        // No peer exists, so the pretend opponent is the second set of keys on this keyboard.
        fake.PeerInputSource = _ => inputManager.ReadInputFor(LocalPlayerNumber == 1 ? 2 : 1);
        return fake;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (debug.ConsumeShouldTick())
        {
            if (session != null)
                session.Step();
            else
                // Inputs are recorded under the frame the tick is about to consume.
                inputManager.PollLocalInputs(match.FrameCount);

            // A guess turned out wrong, so everything since then ran on bad information.
            int replayFrom = session?.TakeRollbackFrame() ?? -1;
            if (replayFrom >= 0)
                ReplayFrom(replayFrom);

            if (session == null || session.CanAdvance(match.FrameCount))
            {
                match.Tick();

                if (debug.RollbackTestEnabled)
                    RunRollbackSelfTest();
            }
            else
            {
                // Either the peer hasn't turned up, or we're further ahead of what's arrived than
                // we could replay. Both mean wait.
                stalledFrames++;
            }
        }

        // Both run whether or not the sim advanced, so the view stays correct while frozen.
        match.SyncPresentation();
        match.DrawDebugBoxes();
    }

    // Rewind to `frame` and re-run every tick since, now that we know what the opponent really did.
    void ReplayFrom(int frame)
    {
        int target = match.FrameCount;
        match.RestoreSnapshot(match.GetGameState(target - frame));
        // Restoring puts FrameCount back to `frame`; tick until we've caught up again.
        while (match.FrameCount < target)
            match.Tick();
    }

    // Frames skipped waiting on input since startup — how choppy the connection is.
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
