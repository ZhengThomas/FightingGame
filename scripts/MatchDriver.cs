using Godot;

// Owns the order of operations for one real frame: collect this tick's inputs, advance the sim,
// then draw. Nothing else runs _PhysicsProcess, so the sequence is defined here rather than
// emerging from autoload registration order.
//
// It decides how many ticks a frame is worth — none while stalled, one normally, several when a
// replay is catching up. The connection itself lives in NetplaySession; this only asks what it
// knows and acts on the answer.
//
// It doesn't decide how the match is connected — StartMatch is told. That call comes from whatever
// screen ran before the fighting scene, so a lobby can hand over a Steam connection the same way
// the menu hands over a loopback one.
public partial class MatchDriver : Node
{
    DebugManager debug;
    InputManager inputManager;
    MatchManager match;

    // Null when both players share this keyboard.
    NetplaySession session;
    // False until StartMatch. Nothing ticks before then, so the menu can sit there.
    bool matchRunning;
    int stalledFrames;

    // How many ticks the self-test rewinds and replays. Small enough to be cheap, long enough to
    // cross a hit or a state change.
    const int RollbackTestDepth = 15;

    public override void _Ready()
    {
        debug = GetNode<DebugManager>("/root/DebugManager");
        inputManager = GetNode<InputManager>("/root/InputManager");
        match = GetNode<MatchManager>("/root/MatchManager");
    }

    public override void _ExitTree() => session?.Shutdown();

    // Set up the next match. Call before swapping in the fighting scene — players register as that
    // scene loads, and ticking starts as soon as they do.
    public void StartMatch(MatchSetup setup)
    {
        session = setup.Transport == null
            ? null
            : new NetplaySession(match, inputManager, setup.Transport,
                                 setup.InputDelay, setup.RollbackFrames);
        stalledFrames = 0;
        matchRunning = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!matchRunning)
        {
            // Nobody set a match up, so this is a scene run straight from the editor rather than
            // reached through the menu. Give it a local one once its players exist.
            if (match.Player1 == null) return;
            StartMatch(default);
        }

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
                // A brief stall is normal. One that lasts is a bug, so say what it's waiting on.
                if (stalledFrames % 120 == 60)
                    GD.Print($"Stalled {stalledFrames} frames at {match.FrameCount}: "
                        + session.StallReason(match.FrameCount));
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
