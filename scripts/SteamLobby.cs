using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;

// One Steam lobby — the room two copies of the game meet in before a match starts.
//
// Deliberately not a node. A lobby has to survive the menu handing over to the fighting scene, so
// it hangs off a static instead of living in a scene that gets thrown away. Null when not in one.
public class SteamLobby
{
    // How many people a lobby holds. Two, for now.
    const int Capacity = 2;

    public static SteamLobby Current { get; private set; }

    Lobby lobby;

    SteamLobby(Lobby lobby) { this.lobby = lobby; }

    // The number to paste to an opponent.
    public ulong Code => lobby.Id.Value;

    // Whoever made the lobby plays player 1. Both sides get the same answer because both are asking
    // Steam rather than deciding for themselves.
    public bool IsHost => lobby.Owner.Id.Value == SteamClient.SteamId.Value;
    public int LocalPlayerNumber => IsHost ? 1 : 2;

    public int MemberCount => lobby.MemberCount;
    public bool IsFull => MemberCount >= Capacity;

    // Who the guest dials to open a connection.
    public SteamId HostId => lobby.Owner.Id;

    // How the host tells the other side to load the fighting scene. Written on the lobby rather
    // than sent down the game connection, which doesn't exist yet at this point.
    const string StateKey = "state";
    const string Playing = "playing";

    public void AnnounceStart() => lobby.SetData(StateKey, Playing);
    public bool MatchStarted => lobby.GetData(StateKey) == Playing;

    // The other person, or default while we're in here alone. This is who SteamChannel will send to.
    public SteamId PeerId
    {
        get
        {
            foreach (Friend member in lobby.Members)
                if (member.Id.Value != SteamClient.SteamId.Value) return member.Id;
            return default;
        }
    }

    // Null if Steam wouldn't make one.
    public static async Task<SteamLobby> Host()
    {
        Current?.Leave();

        Lobby? made = await SteamMatchmaking.CreateLobbyAsync(Capacity);
        if (made == null) return null;

        // Public so a code from anyone works, and the friends-list "Join Game" can find it
        made.Value.SetPublic();
        made.Value.SetJoinable(true);

        Current = new SteamLobby(made.Value);
        Current.Advertise();
        return Current;
    }

    // Null if the code is wrong, the lobby is gone, or it's already full.
    public static async Task<SteamLobby> Join(ulong code)
    {
        Current?.Leave();

        Lobby? joined = await SteamMatchmaking.JoinLobbyAsync(new SteamId { Value = code });
        if (joined == null) return null;

        Current = new SteamLobby(joined.Value);
        Current.Advertise();
        return Current;
    }

    // Puts "Join Game" next to our name on friends' lists. 
    void Advertise() => SteamFriends.SetRichPresence("connect", $"+connect_lobby {Code}");

    // Steam's own friend picker, over the game. Whoever they pick gets an invite; accepting it lands
    // in SteamManager.OnJoinRequested on their side.
    public void OpenInviteOverlay() => SteamFriends.OpenGameInviteOverlay(lobby.Id);

    public void Leave()
    {
        lobby.Leave();
        SteamFriends.ClearRichPresence();
        if (Current == this) Current = null;
    }

    // Who's in here, for showing on the menu.
    public string MemberNames()
    {
        ulong owner = lobby.Owner.Id.Value;
        var names = new List<string>();
        foreach (Friend member in lobby.Members)
            names.Add(member.Id.Value == owner ? $"{member.Name} (host)" : member.Name);
        return string.Join(", ", names);
    }
}
