using Godot;

// The online screen: make or join a Steam lobby, then start the match. All of this used to sit
// permanently on the front page.
//
// Named LobbyMenu, not Lobby — Steamworks has its own Lobby type and the plain name shadows it.
//
// It doesn't launch the match itself. StartMatch is filled in by whoever showed it, because the
// netplay knobs belong to the menu and a page shouldn't own them.
public partial class LobbyMenu : Control
{
    [Signal] public delegate void ClosedEventHandler();

    // Set by the menu before it shows this. The int is the input delay both sides must agree on.
    public System.Action<IInputTransport, int> StartMatch;
    public int InputDelay = 2;

    Godot.Button hostButton, joinButton, copyButton, inviteButton, startButton, backButton;
    LineEdit codeEntry;
    Label status;

    bool starting;      // one-shot: _Process runs on until the scene swap lands a frame later
    bool announced;     // the host's own copy, not read back off the lobby
    bool startFailure;  // already complained once

    public override void _Ready()
    {
        hostButton = GetNode<Godot.Button>("Box/Host");
        joinButton = GetNode<Godot.Button>("Box/Join");
        copyButton = GetNode<Godot.Button>("Box/Copy");
        inviteButton = GetNode<Godot.Button>("Box/Invite");
        startButton = GetNode<Godot.Button>("Box/Start");
        backButton = GetNode<Godot.Button>("Box/Back");
        codeEntry = GetNode<LineEdit>("Box/CodeEntry");
        status = GetNode<Label>("Box/Status");

        hostButton.Pressed += OnHost;
        joinButton.Pressed += OnJoin;
        startButton.Pressed += OnStart;
        backButton.Pressed += Close;
        copyButton.Pressed += CopyCode;
        inviteButton.Pressed += () => SteamLobby.Current?.OpenInviteOverlay();
        GetNode<Godot.Button>("Box/Paste").Pressed += () => codeEntry.Text = DisplayServer.ClipboardGet();

        // All three need a lobby, and their Disabled state is a frame behind — Godot handles input
        // before _Process, so a click is judged against last frame's answer.
        copyButton.Disabled = true;
        inviteButton.Disabled = true;
        startButton.Disabled = true;

        if (!SteamManager.Available)
        {
            hostButton.Disabled = true;
            joinButton.Disabled = true;
        }

        OpaqueTheme.Apply(this);
        FocusFollowsMouse.Apply(this);

        Hide();
    }

    public void Open()
    {
        Show();
        (SteamManager.Available ? hostButton : backButton).GrabFocus();
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || !e.IsActionPressed("ui_cancel")) return;

        Close();
        GetViewport().SetInputAsHandled();
    }

    // Leaving the screen leaves the lobby, or we stay advertised as joinable from the front page.
    void Close()
    {
        SteamLobby.Current?.Leave();
        Hide();
        EmitSignal(SignalName.Closed);
    }

    void CopyCode()
    {
        if (SteamLobby.Current != null)
            DisplayServer.ClipboardSet(SteamLobby.Current.Code.ToString());
    }

    async void OnHost()
    {
        hostButton.Disabled = true;
        status.Text = "Creating a lobby...";
        if (await SteamLobby.Host() == null) status.Text = "Steam wouldn't make a lobby.";
        hostButton.Disabled = false;
    }

    async void OnJoin()
    {
        if (!ulong.TryParse(codeEntry.Text.Trim(), out ulong code))
        {
            status.Text = "That doesn't look like a lobby code.";
            return;
        }

        joinButton.Disabled = true;
        status.Text = "Joining...";
        if (await SteamLobby.Join(code) == null)
            status.Text = "Couldn't join — wrong code, lobby closed, or already full.";
        joinButton.Disabled = false;
    }

    void OnStart()
    {
        SteamLobby lobby = SteamLobby.Current;
        if (lobby == null) return;

        // A refused write means the guest never sees the go signal, so starting here would leave us
        // alone in a match.
        if (!lobby.AnnounceStart(InputDelay))
        {
            status.Text = "Steam rejected the start signal. Press Start again.";
            return;
        }

        announced = true;
    }

    // Polled rather than event-driven. Only runs while this page is up, and reading the lobby hits
    // the Steam client's local cache rather than the network.
    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Both need a lobby to act on, and one can appear without this page doing anything — an
        // accepted invite joins in the background.
        SteamLobby lobby = SteamLobby.Current;
        copyButton.Disabled = lobby == null;
        inviteButton.Disabled = lobby == null;
        // Only the host starts it, and only once there's someone to play.
        startButton.Disabled = lobby == null || !lobby.IsHost || !lobby.IsFull;

        if (lobby == null)
        {
            if (!SteamManager.Available) status.Text = "Steam isn't running, so lobbies are unavailable.";
            return;
        }

        status.Text = $"Lobby {lobby.Code}\n{lobby.MemberNames()}\n"
            + $"You are player {lobby.LocalPlayerNumber}"
            + (lobby.IsFull ? "" : " — waiting for an opponent");

        // Both sides land here: the host from its own button, the other on seeing the lobby say so.
        if ((announced || lobby.MatchStarted) && !starting)
            TryStart(lobby);
    }

    // `starting` is set last, so a throw in here is retried next frame rather than wedging the page
    // behind its own guard.
    void TryStart(SteamLobby lobby)
    {
        try
        {
            // The host's number, so both sides prime the same frames. Falls back to our own only on
            // the host, where the two are the same value anyway.
            int delay = lobby.AgreedInputDelay >= 0 ? lobby.AgreedInputDelay : InputDelay;

            lobby.SealForMatch();
            StartMatch?.Invoke(new NetTransport(new SteamChannel(lobby)), delay);
            starting = true;
        }
        catch (System.Exception e)
        {
            // Retried every frame from here on, so only say it the first time.
            if (startFailure) return;
            startFailure = true;
            GD.PushError($"Couldn't start the match ({e.Message}). Retrying each frame.");
        }
    }
}
