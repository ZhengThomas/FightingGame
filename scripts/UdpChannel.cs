using Godot;

// Two copies of the game on one machine, talking over loopback.
//
// Which side you are is decided by which port you manage to grab: first instance takes PortA and
// becomes player 1, second finds it taken, falls back to PortB and becomes player 2.
public class UdpChannel : IPacketChannel
{
    public const int PortA = 7000;
    public const int PortB = 7001;

    // First byte of every packet. UDP has no notion of a connection, so the handshake is ours to
    // run; nothing above this ever sees a Hello.
    const byte Hello = 1;
    const byte Payload = 2;

    readonly PacketPeerUdp socket = new PacketPeerUdp();

    public bool Connected { get; private set; }
    public int LocalPlayerNumber { get; private set; }
    public bool Failed { get; private set; }

    public UdpChannel()
    {
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
            GD.PushError($"UdpChannel: couldn't bind {PortA} or {PortB}. Is a third copy running?");
            Failed = true;
            return;
        }

        socket.SetDestAddress("127.0.0.1", peerPort);
        GD.Print($"UdpChannel: player {LocalPlayerNumber}, listening on "
            + $"{(LocalPlayerNumber == 1 ? PortA : PortB)}, talking to {peerPort}");
    }

    public void Send(byte[] data)
    {
        if (Failed) return;

        // Keep saying hello until the peer answers, so neither side starts alone.
        if (!Connected)
        {
            socket.PutPacket(new[] { Hello });
            return;
        }

        var framed = new byte[data.Length + 1];
        framed[0] = Payload;
        data.CopyTo(framed, 1);
        socket.PutPacket(framed);
    }

    public byte[] Receive()
    {
        if (Failed) return null;

        while (socket.GetAvailablePacketCount() > 0)
        {
            byte[] data = socket.GetPacket();
            if (data.Length == 0) continue;

            // Any packet at all means someone's there.
            Connected = true;
            if (data[0] == Hello) continue;
            return data[1..];
        }

        return null;
    }

    public void Shutdown() => socket.Close();
}
