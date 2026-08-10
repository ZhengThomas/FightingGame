using System;
using Godot;

// Runs the connection for one networked match: sends our inputs, takes in the opponent's, guesses
// at whatever hasn't arrived yet, notices when a guess turned out wrong, and compares state
// checksums with the peer.
//
// Knows nothing about frame pacing. It reports what it knows — are we connected, can we advance,
// how far back do we need to replay — and MatchDriver decides what to do about it.
public class NetplaySession
{
    readonly MatchManager match;
    readonly InputManager inputManager;
    readonly IInputTransport transport;

    // Frames between pressing a button and it taking effect — the head start the connection gets
    // to deliver an input before the far side needs it. Applies to both players.
    public int InputDelay { get; }
    // How far ahead of confirmed input we're willing to run on guesses before giving up and
    // waiting. Costs nothing until a guess is wrong, unlike InputDelay which costs every input.
    public int MaxRollbackFrames { get; }

    public bool Ready => transport.Ready;
    public int LocalPlayerNumber => transport.LocalPlayerNumber;
    public int RemotePlayerNumber => LocalPlayerNumber == 1 ? 2 : 1;

    Player LocalPlayer => match.PlayerFromNumber(LocalPlayerNumber);
    Player RemotePlayer => match.PlayerFromNumber(RemotePlayerNumber);

    int rollbackTo = -1;
    int desyncFrame = -1;
    bool primed;
    // Highest frame we've already sampled the keyboard for. Sampling one twice would change an
    // input the peer may already hold, and they'd never correct it — a confirmed frame arriving
    // with a new value doesn't trigger a replay, it's just overwritten.
    int lastPolledFrame = -1;

    public NetplaySession(MatchManager match, InputManager inputManager, IInputTransport transport,
                          int inputDelay, int maxRollbackFrames)
    {
        this.match = match;
        this.inputManager = inputManager;
        this.transport = transport;
        InputDelay = inputDelay;
        MaxRollbackFrames = maxRollbackFrames;
    }

    // One frame's worth of connection work, run before the sim advances.
    public void Step()
    {
        EnsurePrimed();

        int inputFrame = match.FrameCount + InputDelay;

        // Only our own character is read from the keyboard; the opponent's arrives over the wire,
        // and polling it here would overwrite what came in.
        inputManager.OwnsPlayer1 = LocalPlayerNumber == 1;
        inputManager.OwnsPlayer2 = LocalPlayerNumber == 2;

        // Once only. Step() also runs on stalled frames, where FrameCount hasn't moved, so polling
        // unconditionally would resample a frame already sent to the peer.
        if (inputFrame > lastPolledFrame)
        {
            inputManager.PollLocalInputs(inputFrame);
            lastPolledFrame = inputFrame;
        }

        Player local = LocalPlayer;
        Player remote = RemotePlayer;
        if (local == null || remote == null) return;

        SendOurInput(local, remote, inputFrame);
        TakeTheirInput(remote);
        RefreshPredictions(remote);
        CheckPeerChecksum();
    }

    // The earliest frame whose real input contradicted the guess we used, or -1 if nothing needs
    // replaying. Consumed on read.
    public int TakeRollbackFrame()
    {
        int frame = rollbackTo;
        rollbackTo = -1;
        return frame;
    }

    // Whether the sim may advance to `frame`. False until the peer turns up
    public bool CanAdvance(int frame) => Ready && HaveConfirmedInputs(frame - MaxRollbackFrames);

    public void Shutdown() => transport.Shutdown();

    void SendOurInput(Player local, Player remote, int inputFrame)
    {
        // Only vouch for frames where nobody was guessing.
        int csFrame = ConfirmedThrough();
        uint checksum = 0u;
        if (csFrame >= 0)
        {
            int framesAgo = match.FrameCount - 1 - csFrame;
            checksum = match.HasGameState(framesAgo) ? match.StateChecksum(framesAgo) : 0u;
            if (checksum == 0u) csFrame = -1;
        }

        transport.Send(inputFrame, local.InputLog.InputAt(inputFrame),
                       remote.InputLog.LastConfirmedFrame, csFrame, checksum);
    }

    void TakeTheirInput(Player remote)
    {
        foreach ((int frame, InputFrame input) in transport.Poll())
        {
            // A real input landing on a frame we guessed, and disagreeing with the guess, means
            // everything from there on was simulated wrong.
            bool contradictsGuess = frame < match.FrameCount
                && !remote.InputLog.IsConfirmed(frame)
                && InputCodec.Pack(input) != InputCodec.Pack(remote.InputLog.InputAt(frame));

            if (contradictsGuess)
                // Corrections don't arrive in order, so keep the earliest — that's how far back
                // the replay has to start.
                rollbackTo = rollbackTo == -1 ? frame : Math.Min(rollbackTo, frame);

            remote.InputLog.SetConfirmed(frame, input);
        }
    }

    // Guess every opponent frame that hasn't arrived, up to the one about to be ticked. The guess
    // is "they're still doing whatever they last actually did"
    void RefreshPredictions(Player remote)
    {
        InputLog log = remote.InputLog;
        if (log.LastConfirmedFrame < 0) return;

        InputFrame guess = log.InputAt(log.LastConfirmedFrame);
        for (int f = log.LastConfirmedFrame + 1; f <= match.FrameCount; f++)
            log.SetPredicted(f, guess);
    }

    // Compare the peer's claim about a past frame against what we computed for it. A mismatch means
    // the two simulations have diverged — the one failure a single-process test could never catch,
    // because there was only ever one simulation.
    void CheckPeerChecksum()
    {
        if (desyncFrame >= 0) return; // already reported; don't spam
        if (!transport.TryTakePeerChecksum(out int frame, out uint theirs)) return;
        if (frame < 0 || frame > ConfirmedThrough()) return; // we were still guessing there

        int framesAgo = match.FrameCount - 1 - frame;
        if (framesAgo < 0 || !match.HasGameState(framesAgo)) return; // too new, or too old to check

        uint ours = match.StateChecksum(framesAgo);
        if (ours == theirs) return;

        desyncFrame = frame;
        GD.PushError($"DESYNC at frame {frame}: we have {ours:X8}, peer has {theirs:X8}. "
            + "The two simulations have diverged.");
    }

    // The first InputDelay frames never get an input written to them, since writing starts at
    // frame InputDelay. Without filling them the game would wait forever for frame 0.
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

    // Newest frame simulated with no guesswork at all
    int ConfirmedThrough()
    {
        if (match.Player1 == null || match.Player2 == null) return -1;
        int both = Math.Min(match.Player1.InputLog.LastConfirmedFrame,
                            match.Player2.InputLog.LastConfirmedFrame);
        return Math.Min(both, match.FrameCount - 1);
    }

    bool HaveConfirmedInputs(int frame)
    {
        if (match.Player1 != null && match.Player1.InputLog.LastConfirmedFrame < frame) return false;
        if (match.Player2 != null && match.Player2.InputLog.LastConfirmedFrame < frame) return false;
        return true;
    }
}
