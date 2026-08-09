using System;
using System.Collections.Generic;

// Stands in for a real connection so rollback can be built and tested in one process. Inputs
// handed to Send come back out of Receive some ticks later, optionally jittered or dropped.
//
// Nothing here is sim state — it sits outside the rollback loop, so its randomness can't affect
// the simulation. The seed is fixed so a given run repeats exactly, which is the main reason to
// test against this rather than a real network.
public class FakeNetwork
{
    // Upper bound on how many frames one packet carries. Sends normally cover everything the peer
    // hasn't acknowledged yet, which is only a few frames on a healthy connection — this cap just
    // stops a packet growing without limit when acks stop coming.
    //
    // Falling this far behind means the connection is effectively dead; real netcode times out and
    // disconnects at that point rather than trying to catch up.
    public const int MaxFramesPerPacket = 32;

    public int DelayTicks = 6;    // one-way latency
    public int JitterTicks = 2;   // random extra delay on top, 0..JitterTicks
    public int DropPercent = 0;

    struct Packet
    {
        public int ArriveTick;
        public int NewestFrame;
        public ushort[] Inputs; // Inputs[i] belongs to frame NewestFrame - i
    }

    readonly List<Packet> inFlight = new List<Packet>();
    readonly FrameRing<ushort> recentlySent = new FrameRing<ushort>(64);
    int lastAcked = -1;
    uint rng;

    public FakeNetwork(int seed) { rng = seed == 0 ? 1u : (uint)seed; }

    // Called by the receiver with the newest frame it has everything up to. Anything past this
    // keeps being resent, so no input can be lost permanently — a fixed-size resend window would
    // deadlock the moment a burst of drops outlasted it.
    public void Acknowledge(int frame)
    {
        if (frame > lastAcked) lastAcked = frame;
    }

    public void Send(int frame, InputFrame input, int nowTick)
    {
        recentlySent.Set(frame, InputCodec.Pack(input));

        if (DropPercent > 0 && Next(100) < DropPercent) return;

        // Everything the peer hasn't confirmed yet, newest first.
        int oldest = Math.Max(0, lastAcked + 1);
        int count = Math.Clamp(frame - oldest + 1, 1, MaxFramesPerPacket);
        ushort[] window = new ushort[count];
        for (int i = 0; i < count; i++)
            window[i] = recentlySent[frame - i];

        inFlight.Add(new Packet
        {
            ArriveTick = nowTick + DelayTicks + (JitterTicks > 0 ? (int)Next((uint)JitterTicks + 1) : 0),
            NewestFrame = frame,
            Inputs = window,
        });
    }

    // Everything that has landed by nowTick. Jitter means packets can arrive out of order, which
    // is realistic — callers must not assume frames come back in sequence.
    public List<(int Frame, InputFrame Input)> Receive(int nowTick)
    {
        var arrived = new List<(int, InputFrame)>();
        for (int p = inFlight.Count - 1; p >= 0; p--)
        {
            if (inFlight[p].ArriveTick > nowTick) continue;
            Packet packet = inFlight[p];
            inFlight.RemoveAt(p);
            for (int i = packet.Inputs.Length - 1; i >= 0; i--)
                arrived.Add((packet.NewestFrame - i, InputCodec.Unpack(packet.Inputs[i])));
        }
        return arrived;
    }

    public void Clear()
    {
        inFlight.Clear();
        lastAcked = -1;
    }

    // xorshift32 — small, and seeded so runs repeat.
    uint Next(uint bound)
    {
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        return rng % bound;
    }
}
