using Godot;
using System;
using System.Collections.Generic;
using System.IO;

// A real connection between two copies of the game on this machine.
//
// Which side you are is decided by which port you manage to grab: first instance takes PortA and
// becomes player 1, second finds it taken, falls back to PortB and becomes player 2.
public class UdpTransport : IInputTransport
{
    public const int PortA = 7000;
    public const int PortB = 7001;

    // Cap on frames per packet — sends normally cover only what the peer hasn't acknowledged.
    public const int MaxFramesPerPacket = 32;

    // Artificial network conditions, applied on top of real packets. Two windows on one desk talk
    // in well under a millisecond with no loss, so without these everything looks perfect and
    // proves nothing.
    public int ExtraLatencyTicks = 0;
    public int JitterTicks = 0;
    public int DropPercent = 0;

    enum MsgType : byte { Hello = 1, Input = 2 }

    public bool Ready { get; private set; }
    public int LocalPlayerNumber { get; private set; }

    readonly PacketPeerUdp socket = new PacketPeerUdp();
    readonly FrameRing<ushort> recentlySent = new FrameRing<ushort>(64);
    readonly List<(int ArriveTick, byte[] Data)> held = new List<(int, byte[])>();

    int lastAcked = -1;
    int tick;
    uint rng;

    bool haveChecksum;
    int checksumFrame;
    uint checksumValue;

    public bool Failed { get; private set; }

    public UdpTransport(int seed = 987654321)
    {
        rng = seed == 0 ? 1u : (uint)seed;

        int peerPort;
        if (socket.Bind(PortA) == Error.Ok)
        {
            LocalPlayerNumber = 1;
            peerPort = PortB;
        }
        else if (socket.Bind(PortB) == Error.Ok)
        {
            LocalPlayerNumber = 2;
            peerPort = PortA;
        }
        else
        {
            GD.PushError($"UdpTransport: couldn't bind {PortA} or {PortB}. Is a third copy running?");
            Failed = true;
            return;
        }

        socket.SetDestAddress("127.0.0.1", peerPort);
        GD.Print($"UdpTransport: player {LocalPlayerNumber}, listening on "
            + $"{(LocalPlayerNumber == 1 ? PortA : PortB)}, talking to {peerPort}");
    }

    public void Send(int frame, InputFrame localInput, int ackFrame, int csFrame, uint checksum)
    {
        if (Failed) return;
        tick++;

        if (ackFrame > lastAcked) lastAcked = ackFrame;
        recentlySent.Set(frame, InputCodec.Pack(localInput));

        // Keep saying hello until the peer answers, so neither side starts alone.
        if (!Ready)
        {
            var hello = new MemoryStream();
            var hw = new BinaryWriter(hello);
            hw.Write((byte)MsgType.Hello);
            hw.Write((byte)LocalPlayerNumber);
            socket.PutPacket(hello.ToArray());
            return;
        }

        // Everything the peer hasn't confirmed, newest first.
        int oldest = Math.Max(0, lastAcked + 1);
        int count = Math.Clamp(frame - oldest + 1, 1, MaxFramesPerPacket);

        var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write((byte)MsgType.Input);
        w.Write((byte)LocalPlayerNumber);
        w.Write(frame);
        w.Write((byte)count);
        for (int i = 0; i < count; i++)
            w.Write(recentlySent[frame - i]);
        w.Write(ackFrame);
        w.Write(csFrame);
        w.Write(checksum);

        socket.PutPacket(stream.ToArray());
    }

    public List<(int Frame, InputFrame Input)> Poll()
    {
        var arrived = new List<(int, InputFrame)>();
        if (Failed) return arrived;

        // Drain the socket into the delay queue, dropping some if asked to.
        while (socket.GetAvailablePacketCount() > 0)
        {
            byte[] data = socket.GetPacket();
            if (data.Length == 0) continue;
            if (DropPercent > 0 && Next(100) < DropPercent) continue;

            int delay = ExtraLatencyTicks + (JitterTicks > 0 ? (int)Next((uint)JitterTicks + 1) : 0);
            held.Add((tick + delay, data));
        }

        // Then take whatever is due.
        for (int i = held.Count - 1; i >= 0; i--)
        {
            if (held[i].ArriveTick > tick) continue;
            byte[] data = held[i].Data;
            held.RemoveAt(i);
            ReadPacket(data, arrived);
        }

        return arrived;
    }

    void ReadPacket(byte[] data, List<(int, InputFrame)> into)
    {
        try
        {
            var r = new BinaryReader(new MemoryStream(data));
            var type = (MsgType)r.ReadByte();
            r.ReadByte(); // sender's player number, unused — the port already told us

            // Any packet at all means someone's there.
            Ready = true;
            if (type == MsgType.Hello) return;

            int newest = r.ReadInt32();
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
                into.Add((newest - i, InputCodec.Unpack(r.ReadUInt16())));

            r.ReadInt32(); // their ack of us — unused for now, we resend on our own schedule
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
            GD.PushWarning("UdpTransport: malformed packet, ignoring.");
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
        socket.Close();
        held.Clear();
    }

    uint Next(uint bound)
    {
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        return rng % bound;
    }
}
