using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;

// Carries bytes between the two people in a Steam lobby. The host listens, the guest dials in.
public class SteamChannel : IPacketChannel
{
    // Steam allows several independent connections between the same two people. Nothing else here
    // uses one, so the number is arbitrary.
    const int VirtualPort = 0;

    // Unreliable, and sent immediately rather than batched. Reliable delivery would hold newer
    // packets back while resending a lost older one, which rollback can't afford; losses are
    // already covered by resending everything unacknowledged.
    const SendType Delivery = SendType.Unreliable | SendType.NoNagle;

    // Facepunch's two ends share no shape — one is a listening socket holding a list of
    // connections, the other a single outbound connection — so they're squared up here and the
    // host/guest split stops existing above the constructor.
    interface IEnd
    {
        bool Live { get; }
        Queue<byte[]> Inbox { get; }
        void Pump();
        void Post(byte[] data);
        void Stop();
    }

    readonly IEnd end;

    public int LocalPlayerNumber { get; }
    public bool Connected => end.Live;

    public SteamChannel(SteamLobby lobby)
    {
        LocalPlayerNumber = lobby.LocalPlayerNumber;

        if (!lobby.IsHost)
        {
            end = SteamNetworkingSockets.ConnectRelay<GuestConnection>(lobby.HostId, VirtualPort);
            return;
        }

        var host = SteamNetworkingSockets.CreateRelaySocket<HostSocket>(VirtualPort);
        host.ExpectedPeer = lobby.PeerId;
        end = host;

        // Nobody to let in, so nobody connects and the match stalls with no clue why.
        if (host.ExpectedPeer.Value == 0)
            Godot.GD.PushError("SteamChannel: hosting with no opponent in the lobby.");
    }

    public void Send(byte[] data)
    {
        // Steam is still shaking hands. Nothing is lost — these frames go out again in the next
        // packet until they're acknowledged.
        if (!end.Live) return;
        end.Post(data);
    }

    public byte[] Receive()
    {
        // Steam only delivers into the callback when asked. Called in a loop until it returns null,
        // so ask only once we've run dry.
        if (end.Inbox.Count == 0) end.Pump();
        return end.Inbox.Count > 0 ? end.Inbox.Dequeue() : null;
    }

    public void Shutdown() => end.Stop();

    // Steam hands over a pointer into its own memory, valid only for the length of the callback.
    static byte[] CopyOut(IntPtr data, int size)
    {
        byte[] copy = new byte[size];
        Marshal.Copy(data, copy, 0, size);
        return copy;
    }

    class HostSocket : SocketManager, IEnd
    {
        // The base class accepts anyone, and every accepted connection feeds the same inbox — so a
        // stranger's packets would arrive as opponent input.
        public SteamId ExpectedPeer;

        public Queue<byte[]> Inbox { get; } = new Queue<byte[]>();

        public bool Live => Connected.Count > 0;
        public void Pump() => Receive();
        public void Post(byte[] data) => Connected[0].SendMessage(data, Delivery);
        public void Stop() => Close();

        public override void OnConnecting(Connection connection, ConnectionInfo info)
        {
            if (Connected.Count == 0 && info.Identity.SteamId.Value == ExpectedPeer.Value)
                connection.Accept();
            else
                connection.Close();
        }

        public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data,
                                       int size, long messageNum, long recvTime, int channel)
            => Inbox.Enqueue(CopyOut(data, size));
    }

    class GuestConnection : ConnectionManager, IEnd
    {
        public Queue<byte[]> Inbox { get; } = new Queue<byte[]>();

        public bool Live => Connected;
        public void Pump() => Receive();
        public void Post(byte[] data) => Connection.SendMessage(data, Delivery);
        public void Stop() => Close();

        public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime,
                                       int channel)
            => Inbox.Enqueue(CopyOut(data, size));
    }
}
