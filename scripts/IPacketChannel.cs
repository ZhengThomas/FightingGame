// Carries blobs of bytes to the one other machine in this match. Knows nothing about what's in
// them — NetTransport sits on top and owns all of that.
//
// This is the only part that differs between a loopback UDP connection and Steam.
public interface IPacketChannel
{
    // False until the other side has been found. Send still gets called while false; a channel that
    // needs a handshake sends its own traffic instead of what it was handed.
    bool Connected { get; }

    // Which character this instance drives, 1 or 2. Zero means not worked out yet — channels that
    // learn it from the peer won't know until Connected.
    int LocalPlayerNumber { get; }

    void Send(byte[] data);

    // Next blob that has arrived, or null once the queue is empty. Handshake traffic is swallowed
    // here and never surfaces.
    byte[] Receive();

    void Shutdown();
}
