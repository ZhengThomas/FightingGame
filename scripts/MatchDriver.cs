using Godot;

// Owns the order of operations for one real frame: collect this tick's inputs, advance the sim,
// then draw. Nothing else runs _PhysicsProcess, so the sequence is defined here rather than
// emerging from autoload registration order.
//
// It decides how many ticks a frame is worth — none while stalled, one normally, several when a
// replay is catching up. The connection itself lives in NetplaySession; this only asks what it
// knows and acts on the answer.
//
// It doesn't decide how the match is connected — whoever loads this scene fills in Setup first, so
// a lobby can hand over a Steam connection the same way the menu hands over a loopback one. Left
// alone it's a local match on one keyboard, which is what running the scene from the editor gets.
public partial class MatchDriver : Node
{
    const string MenuScene = "res://menu.tscn";

    // Written by the previous screen after instantiating the fighting scene but before adding it to
    // the tree, so it's in place by the time _Ready runs.
    public MatchSetup Setup;

    DebugManager debug;
    InputManager inputManager;
    MatchManager match;

    // Null when both players share this keyboard.
    NetplaySession session;
    // Null outside training.
    TrainingMode training;
    int stalledFrames;

    // How many ticks the self-test rewinds and replays. Small enough to be cheap, long enough to
    // cross a hit or a state change.
    const int RollbackTestDepth = 15;

    public override void _Ready()
    {
        debug = GetNode<DebugManager>("/root/DebugManager");
        match = MatchManager.Current;
        inputManager = new InputManager(match);

        if (Setup.Mode == GameMode.Training) training = new TrainingMode(match);

        if (Setup.Transport != null)
            session = new NetplaySession(match, inputManager, Setup.Transport,
                                         Setup.InputDelay, Setup.RollbackFrames);
    }

    // Leaving the scene ends the match, so the connection goes with it.
    public override void _ExitTree() => session?.Shutdown();

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

            // Before the tick, and once per real frame rather than once per tick.
            training?.Step();

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

        // Read after ticking, and only once the sim has held the decision long enough that a
        // rollback can't take it back.
        if (match.Flow.ReadyToLeave) ReturnToMenu();
    }

    // Tears the match down and goes back to the front screen. _ExitTree closes the connection on
    // the way out.
    void ReturnToMenu()
    {
        // The lobby still says a match is running and won't accept anyone, and both sides are
        // leaving it — so drop it rather than hand the menu a lobby that would restart instantly.
        SteamLobby.Current?.Leave();

        Node menu = GD.Load<PackedScene>(MenuScene).Instantiate();
        Node old = GetTree().CurrentScene;
        GetTree().Root.AddChild(menu);
        GetTree().CurrentScene = menu;
        old.QueueFree();
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
