using Godot;
using System;
using System.Collections.Generic;
using System.IO;

// Turns inputs into packets and back, over whatever channel it's handed. Owns the packet format,
// the resend window, the ack tracking, the checksum exchange, and the artificial network
// conditions — everything that stays the same whether the bytes travel over UDP or Steam.
public class NetTransport : IInputTransport
{
    // Cap on frames per packet — sends normally cover only what the peer hasn't acknowledged.
    public const int MaxFramesPerPacket = 32;

    // Artificial network conditions, applied to packets on arrival. This is to simualte a bad connection
    // when testing on your own local computer
    public int ExtraLatencyTicks = 0;
    public int JitterTicks = 0;
    public int DropPercent = 0;

    readonly IPacketChannel channel;
    readonly FrameRing<ushort> recentlySent = new FrameRing<ushort>(64);
    readonly List<(int ArriveTick, byte[] Data)> held = new List<(int, byte[])>();

    // Newest frame of OURS the peer has told us it received. Anything past this keeps going out
    // again. Not to be confused with our ack of them, which we only pass along in packets.
    int peerAcked = -1;
    int tick;
    uint rng;

    bool haveChecksum;
    int checksumFrame;
    uint checksumValue;

    public bool Ready => channel.Connected;
    public int LocalPlayerNumber => channel.LocalPlayerNumber;

    public NetTransport(IPacketChannel channel, int seed = 987654321)
    {
        this.channel = channel;
        rng = seed == 0 ? 1u : (uint)seed;
    }

    public void Send(int frame, InputFrame localInput, int ackFrame, int csFrame, uint checksum)
    {
        tick++;
        recentlySent.Set(frame, InputCodec.Pack(localInput));

        // Everything the peer hasn't confirmed receiving, newest first.
        int oldest = Math.Max(0, peerAcked + 1);
        int count = Math.Clamp(frame - oldest + 1, 1, MaxFramesPerPacket);

        var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write(frame);
        w.Write((byte)count);
        for (int i = 0; i < count; i++)
            w.Write(recentlySent[frame - i]);
        w.Write(ackFrame);
        w.Write(csFrame);
        w.Write(checksum);

        // Handed over even before the channel is connected — it substitutes its own handshake.
        channel.Send(stream.ToArray());
    }

    public List<(int Frame, InputFrame Input)> Poll()
    {
        var arrived = new List<(int, InputFrame)>();

        // Drain the channel into the delay queue, dropping some if asked to.
        byte[] data;
        while ((data = channel.Receive()) != null)
        {
            if (DropPercent > 0 && Next(100) < DropPercent) continue;

            int delay = ExtraLatencyTicks + (JitterTicks > 0 ? (int)Next((uint)JitterTicks + 1) : 0);
            held.Add((tick + delay, data));
        }

        // Then take whatever is due.
        for (int i = held.Count - 1; i >= 0; i--)
        {
            if (held[i].ArriveTick > tick) continue;
            byte[] due = held[i].Data;
            held.RemoveAt(i);
            ReadPacket(due, arrived);
        }

        return arrived;
    }

    void ReadPacket(byte[] data, List<(int, InputFrame)> into)
    {
        try
        {
            var r = new BinaryReader(new MemoryStream(data));

            int newest = r.ReadInt32();
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
                into.Add((newest - i, InputCodec.Unpack(r.ReadUInt16())));

            // How far they've received from us — what sizes our resend window.
            int theirAck = r.ReadInt32();
            if (theirAck > peerAcked) peerAcked = theirAck;

            int csFrame = r.ReadInt32();
            uint csValue = r.ReadUInt32();
            if (csFrame >= 0)
            {
                haveChecksum = true;
                checksumFrame = csFrame;
                checksumValue = csValue;
            }
        }
        catch (EndOfStreamException)
        {
            GD.PushWarning("NetTransport: malformed packet, ignoring.");
        }
    }

    public bool TryTakePeerChecksum(out int frame, out uint checksum)
    {
        frame = checksumFrame;
        checksum = checksumValue;
        bool had = haveChecksum;
        haveChecksum = false;
        return had;
    }

    public void Shutdown()
    {
        channel.Shutdown();
        held.Clear();
    }

    // xorshift32 — small, and seeded so runs repeat. Gets stuck forever at zero, hence the guard in
    // the constructor.
    uint Next(uint bound)
    {
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        return rng % bound;
    }
}
