using System;
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
    // The local player writes straight to their log; the opponent's keys go through FakeNetwork
    // and only reach the log once "delivered".
    public bool SimulateNetplay = true;

    // Which character this instance controls. Fixed at 1 while both players share a keyboard, but
    // once two copies are talking to each other one of them is player 2, and nothing below may
    // assume otherwise.
    public int LocalPlayerNumber = 1;

    int RemotePlayerNumber => LocalPlayerNumber == 1 ? 2 : 1;
    Player LocalPlayer => match.PlayerFromNumber(LocalPlayerNumber);
    Player RemotePlayer => match.PlayerFromNumber(RemotePlayerNumber);

    // Inputdelay Needs to be >= FakeLatencyTicks + FakeJitterTicks or the game will
    // constantly have to stall Set it lower on purpose to watch that happen; rollback is what
    // eventually lets it come down.
    public int InputDelay = 4;
    public int rollbackFrames = 4;
    public int FakeLatencyTicks = 6;   
    public int FakeJitterTicks = 2;
    public int FakeDropPercent = 5;

    // Frame that was mispredicted and we need to commit a rollback to.
    // -1 means nothing is wrong
    int rollbackTo = -1;

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

            // Guess the next few inputs of the opponent based on waht theyre doing
            RefreshPredictions();

            // Theres a difference between our prediction and the actual, so we resim
            if(rollbackTo != -1)
            {
                int target = match.FrameCount;
                match.RestoreSnapshot(match.GetGameState(target - rollbackTo));
                // Restoring puts FrameCount back to rollbackTo; tick until we've caught up again.
                while (match.FrameCount < target)
                    match.Tick();

                rollbackTo = -1;
            }

            // if an input for this frame hasn't arrived, don't advance. Both sides do the same,
            // so they stall together and stay in step.
            if (!SimulateNetplay || HaveConfirmedInputs(match.FrameCount - rollbackFrames))
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

        // Only our own character is read straight from the keyboard; the opponent's arrives.
        inputManager.OwnsPlayer1 = LocalPlayerNumber == 1;
        inputManager.OwnsPlayer2 = LocalPlayerNumber == 2;
        inputManager.PollLocalInputs(inputFrame);

        Player remote = RemotePlayer;
        if (remote != null)
        {
            // The pretend opponent is on this keyboard too, so their keys are read here and pushed
            // through the connection rather than arriving from anywhere. Against a real peer this
            // send carries OUR input instead, and theirs turns up in Receive.
            fakeNet.Send(inputFrame, inputManager.ReadInputFor(RemotePlayerNumber), netTick);

            foreach ((int frame, InputFrame input) in fakeNet.Receive(netTick)){
                // If the input we received is not the same as what weve predicted
                var predicted = remote.InputLog.InputAt(frame);
                if(frame < match.FrameCount && !remote.InputLog.IsConfirmed(frame) && InputCodec.Pack(input) != InputCodec.Pack(predicted))
                {
                    // Corrections don't arrive in order, so keep the earliest — that's how far
                    // back the replay has to start.
                    rollbackTo = rollbackTo == -1 ? frame : Math.Min(rollbackTo, frame);
                }

                remote.InputLog.SetConfirmed(frame, input);
            }
            // Tell the sender how far we've got, so it stops resending what landed and keeps
            // resending what didn't. Without this a long enough burst of drops would strand a
            // frame forever and the game would wait on it for good.
            fakeNet.Acknowledge(remote.InputLog.LastConfirmedFrame);
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

    // Fill in guesses for every opponent frame that hasn't arrived, up to the one we're about to
    // tick. The guess is "they're still doing whatever they last actually did" — right more often
    // than it sounds, since people hold directions and buttons for several frames at a time.
    //
    // Rerun every frame rather than only for the newest one: once a correction lands,
    // LastConfirmedFrame moves and every guess past it needs re-basing on the newer input.
    void RefreshPredictions()
    {
        Player remote = RemotePlayer;
        if (!SimulateNetplay || remote == null) return;

        InputLog log = remote.InputLog;
        if (log.LastConfirmedFrame < 0) return;

        InputFrame guess = log.InputAt(log.LastConfirmedFrame);
        for (int f = log.LastConfirmedFrame + 1; f <= match.FrameCount; f++)
            log.SetPredicted(f, guess);
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
