using Godot;
using Steamworks;
using Steamworks.Data;

// Starts Steam and keeps it fed.
//
// Steam collects things for us — lobby joins, invites, arriving packets — but only hands them over
// when RunCallbacks is called. Miss that and everything silently stops happening, so it runs every
// frame for as long as the game is open. An autoload rather than a scene node for the same reason:
// Steam is signed in across menus, matches and everything else, unlike a match.
public partial class SteamManager : Node
{
    // Valve's public test app. Works without owning anything, but nothing can ship on it — that
    // needs a real app id, which needs a Steamworks partner account.
    public const uint SpacewarAppId = 480;

    // False when Steam isn't running or failed to start. Anything Steam-related has to check this;
    // local and loopback play don't care either way.
    public static bool Available { get; private set; }

    // Who's signed in, for showing on a menu. Empty when Steam isn't available.
    public static string PersonaName { get; private set; } = "";

    public override void _Ready()
    {
        try
        {
            // asyncCallbacks off — we pump them below instead, on the game's own clock.
            SteamClient.Init(SpacewarAppId, asyncCallbacks: false);
            Available = true;
            PersonaName = SteamClient.Name;
            GD.Print($"Steam: signed in as {PersonaName} ({SteamClient.SteamId.Value})");

            SteamFriends.OnGameLobbyJoinRequested += OnJoinRequested;
        }
        catch (System.Exception e)
        {
            Available = false;
            GD.Print($"Steam unavailable ({e.Message}). Local and loopback play still work.");
        }
    }

    public override void _Process(double delta)
    {
        if (Available) SteamClient.RunCallbacks();
    }

    // Fires when a friend's invite is accepted, or they hit "Join Game" on the friends list, while
    // the game is already open.
    static async void OnJoinRequested(Lobby lobby, SteamId inviter)
    {
        // Mid-match, being yanked into someone else's lobby would be baffling.
        if (MatchManager.Current != null)
        {
            GD.Print($"Ignoring a lobby invite from {inviter.Value} — already in a match.");
            return;
        }

        if (await SteamLobby.Join(lobby.Id.Value) == null)
            GD.PushWarning("Couldn't join the invited lobby.");
    }

    public override void _ExitTree()
    {
        if (!Available) return;
        SteamFriends.OnGameLobbyJoinRequested -= OnJoinRequested;
        SteamClient.Shutdown();
        Available = false;
    }
}
