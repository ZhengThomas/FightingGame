using System;
using System.Collections.Generic;

// Stands in for a real connection so rollback can be built and tested in one process.
//
// There's no peer here, so it fakes the inbound half only: every time the driver sends, this asks
// PeerInputSource what the opponent "pressed" on that frame (the second set of keys on this
// keyboard) and delivers it back some ticks later. Outbound input has nowhere to go and is dropped.
//
// Nothing here is sim state — it sits outside the rollback loop, so its randomness can't affect the
// simulation. The seed is fixed so a given run repeats exactly, which is the main reason to test
// against this rather than a real network.
public class FakeNetwork : IInputTransport
{
    // Upper bound on how many frames one packet carries. Sends normally cover everything the peer
    // hasn't acknowledged yet, which is only a few frames on a healthy connection — this cap just
    // stops a packet growing without limit when acks stop coming.
    public const int MaxFramesPerPacket = 32;

    public int DelayTicks = 6;    // one-way latency
    public int JitterTicks = 2;   // random extra delay on top, 0..JitterTicks
    public int DropPercent = 0;

    // Where the pretend opponent's inputs come from, given a frame number.
    public Func<int, InputFrame> PeerInputSource;

    public bool Ready => true;              // nothing to connect to
    public int LocalPlayerNumber { get; set; } = 1;

    struct Packet
    {
        public int ArriveTick;
        public int NewestFrame;
        public ushort[] Inputs; // Inputs[i] belongs to frame NewestFrame - i
    }

    readonly List<Packet> inFlight = new List<Packet>();
    readonly FrameRing<ushort> recentlySent = new FrameRing<ushort>(64);
    int lastAcked = -1;
    int tick;
    uint rng;

    public FakeNetwork(int seed) { rng = seed == 0 ? 1u : (uint)seed; }

    // localInput is discarded — there's no peer to receive it. What gets queued is the opponent's
    // input for the same frame, which is the direction this class actually simulates.
    public void Send(int frame, InputFrame localInput, int ackFrame, int checksumFrame, uint checksum)
    {
        Acknowledge(ackFrame);
        QueuePeerInput(frame, PeerInputSource != null ? PeerInputSource(frame) : default);
        tick++;
    }

    public List<(int Frame, InputFrame Input)> Poll()
    {
        var arrived = new List<(int, InputFrame)>();
        for (int p = inFlight.Count - 1; p >= 0; p--)
        {
            if (inFlight[p].ArriveTick > tick) continue;
            Packet packet = inFlight[p];
            inFlight.RemoveAt(p);
            for (int i = packet.Inputs.Length - 1; i >= 0; i--)
                arrived.Add((packet.NewestFrame - i, InputCodec.Unpack(packet.Inputs[i])));
        }
        return arrived;
    }

    // No peer, so no claims to compare against.
    public bool TryTakePeerChecksum(out int frame, out uint checksum)
    {
        frame = -1;
        checksum = 0;
        return false;
    }

    public void Shutdown() => Clear();

    public void Clear()
    {
        inFlight.Clear();
        lastAcked = -1;
    }

    // Newest frame the receiver has everything up to. Anything past this keeps being resent, so no
    // input can be lost permanently — a fixed-size resend window would deadlock the moment a burst
    // of drops outlasted it.
    void Acknowledge(int frame)
    {
        if (frame > lastAcked) lastAcked = frame;
    }

    void QueuePeerInput(int frame, InputFrame input)
    {
        recentlySent.Set(frame, InputCodec.Pack(input));

        if (DropPercent > 0 && Next(100) < DropPercent) return;

        // Everything not yet acknowledged, newest first.
        int oldest = Math.Max(0, lastAcked + 1);
        int count = Math.Clamp(frame - oldest + 1, 1, MaxFramesPerPacket);
        ushort[] window = new ushort[count];
        for (int i = 0; i < count; i++)
            window[i] = recentlySent[frame - i];

        inFlight.Add(new Packet
        {
            ArriveTick = tick + DelayTicks + (JitterTicks > 0 ? (int)Next((uint)JitterTicks + 1) : 0),
            NewestFrame = frame,
            Inputs = window,
        });
    }

    // xorshift32 — small, and seeded so runs repeat. Gets stuck forever at zero, hence the guard
    // in the constructor.
    uint Next(uint bound)
    {
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        return rng % bound;
    }
}
