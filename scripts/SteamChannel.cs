using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;

// Carries bytes between the two people in a Steam lobby.
//
// Steam's connection is lopsided to set up — the host listens, the guest dials in — and that's the
// only reason this is bigger than UdpChannel. Once the connection exists both sides do the same two
// things.
public class SteamChannel : IPacketChannel
{
    // Steam allows several independent connections between the same two people. Nothing else here
    // uses one, so the number is arbitrary.
    const int VirtualPort = 0;

    // Unreliable, and sent immediately rather than batched. Reliable delivery would hold newer
    // packets back while resending a lost older one 
    const SendType Delivery = SendType.Unreliable | SendType.NoNagle;

    readonly HostSocket socket;          // set when we're the host
    readonly GuestConnection connection; // set when we're not

    public int LocalPlayerNumber { get; }

    public bool Connected => socket != null ? socket.Connected.Count > 0 : connection.Connected;

    public SteamChannel(SteamLobby lobby)
    {
        LocalPlayerNumber = lobby.LocalPlayerNumber;

        if (lobby.IsHost)
            socket = SteamNetworkingSockets.CreateRelaySocket<HostSocket>(VirtualPort);
        else
            connection = SteamNetworkingSockets.ConnectRelay<GuestConnection>(lobby.HostId, VirtualPort);
    }

    public void Send(byte[] data)
    {
        // Nowhere to send yet — Steam is still shaking hands. Nothing is lost: the frames in this
        // packet go out again in the next one until they're acknowledged.
        if (!Connected) return;

        if (socket != null) socket.Connected[0].SendMessage(data, Delivery);
        else connection.Connection.SendMessage(data, Delivery);
    }

    public byte[] Receive()
    {
        Queue<byte[]> inbox = socket != null ? socket.Inbox : connection.Inbox;

        // Steam only delivers into the callback when asked to. Ask once we've run dry rather than
        // on every call, since this gets called in a loop until it returns null.
        if (inbox.Count == 0)
        {
            if (socket != null) socket.Receive();
            else connection.Receive();
        }

        return inbox.Count > 0 ? inbox.Dequeue() : null;
    }

    public void Shutdown()
    {
        socket?.Close();
        connection?.Close();
    }

    // Steam hands over a pointer into its own memory, valid only for the length of the callback.
    static byte[] CopyOut(IntPtr data, int size)
    {
        byte[] copy = new byte[size];
        Marshal.Copy(data, copy, 0, size);
        return copy;
    }

    // The host's end. The base class accepts incoming connections and tracks them in Connected.
    class HostSocket : SocketManager
    {
        public readonly Queue<byte[]> Inbox = new Queue<byte[]>();

        public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data,
                                       int size, long messageNum, long recvTime, int channel)
            => Inbox.Enqueue(CopyOut(data, size));
    }

    // The guest's end — one connection, to the host.
    class GuestConnection : ConnectionManager
    {
        public readonly Queue<byte[]> Inbox = new Queue<byte[]>();

        public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime,
                                       int channel)
            => Inbox.Enqueue(CopyOut(data, size));
    }
}
