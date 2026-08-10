using System.Collections.Generic;

// How MatchDriver reaches the opponent, whether that's a real connection or the pretend one.
public interface IInputTransport
{
    // False until both sides have found each other. The driver must not tick before this.
    bool Ready { get; }

    // Which character this instance drives. UDP decides it from which port it managed to grab.
    int LocalPlayerNumber { get; }

    // Hand over this frame's local input, plus how far we've received (so the peer can stop
    // resending) and a checksum for a recent frame (so the peer can spot a desync).
    void Send(int frame, InputFrame localInput, int ackFrame, int checksumFrame, uint checksum);

    // Whatever has arrived since last time. Not sorted by frame — resends and jitter mean frames
    // turn up out of order, which callers have to tolerate.
    List<(int Frame, InputFrame Input)> Poll();

    // The most recent "at frame N my state was X" claim from the peer, if one arrived. Consumed on
    // read, so each claim is only compared once.
    bool TryTakePeerChecksum(out int frame, out uint checksum);

    void Shutdown();
}
