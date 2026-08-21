using Godot;

// The online screen: hosting starts by itself on the way in, the seat beside you is an invite until
// somebody fills it, and codes go through the clipboard rather than a text field.
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

    Godot.Button startButton, backButton, inviteButton, copyButton, joinButton;
    Label slot1, slot2, status;

    bool hosting;       // a Host call is in flight, so don't start a second one
    bool starting;      // one-shot: _Process runs on until the scene swap lands a frame later
    bool announced;     // the host's own copy, not read back off the lobby
    bool startFailure;  // already complained once

    public override void _Ready()
    {
        startButton = GetNode<Godot.Button>("Left/Start");
        backButton = GetNode<Godot.Button>("Left/Back");
        inviteButton = GetNode<Godot.Button>("Right/Players/Invite");
        copyButton = GetNode<Godot.Button>("Right/Copy");
        joinButton = GetNode<Godot.Button>("Right/Join");
        slot1 = GetNode<Label>("Right/Players/Slot1");
        slot2 = GetNode<Label>("Right/Players/Slot2");
        status = GetNode<Label>("Right/Status");

        startButton.Pressed += OnStart;
        backButton.Pressed += Close;
        inviteButton.Pressed += () => SteamLobby.Current?.OpenInviteOverlay();
        copyButton.Pressed += CopyCode;
        joinButton.Pressed += JoinFromClipboard;

        joinButton.Disabled = !SteamManager.Available;

        GetNode<Label>("SteamUser").Text = SteamManager.Available
            ? $"Steam: {SteamManager.PersonaName}"
            : "Steam: not running";

        OpaqueTheme.Apply(this);
        FocusFollowsMouse.Apply(this);

        Hide();
    }

    public void Open()
    {
        Show();

        // Arriving here means wanting to play someone, so there's no reason to make hosting a
        // separate button press. An invite that landed us in someone else's lobby already counts.
        if (SteamLobby.Current == null && SteamManager.Available) StartHosting();

        Refresh();
        FocusSomething();
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

    // Polled rather than event-driven. Only runs while this page is up, and reading the lobby hits
    // the Steam client's local cache rather than the network.
    public override void _Process(double delta)
    {
        if (!Visible) return;

        Refresh();

        SteamLobby lobby = SteamLobby.Current;
        // Both sides land here: the host from its own button, the other on seeing the lobby say so.
        if (lobby != null && (announced || lobby.MatchStarted) && !starting)
            TryStart(lobby);
    }

    // The whole screen is redrawn from the lobby's current state rather than patched as things
    // happen, so an invite arriving with nothing pressed shows up the same as a button press would.
    void Refresh()
    {
        SteamLobby lobby = SteamLobby.Current;

        // Disabled states are a frame behind — Godot handles input before _Process, so a click is
        // judged against last frame's answer.
        startButton.Disabled = lobby == null || !lobby.IsHost || !lobby.IsFull;
        copyButton.Disabled = lobby == null;

        string host = lobby?.NameOfPlayer(1);
        string guest = lobby?.NameOfPlayer(2);

        slot1.Text = host ?? (hosting ? "Creating a lobby..." : "—");
        slot2.Text = guest ?? "";
        slot2.Visible = guest != null;
        // Only the host can invite, and only while the seat is empty.
        inviteButton.Visible = lobby != null && lobby.IsHost && guest == null;
    }

    void FocusSomething()
    {
        if (!startButton.Disabled) startButton.GrabFocus();
        else if (inviteButton.Visible) inviteButton.GrabFocus();
        else backButton.GrabFocus();
    }

    async void StartHosting()
    {
        hosting = true;
        status.Text = "";
        if (await SteamLobby.Host() == null) status.Text = "Steam wouldn't make a lobby.";
        hosting = false;
    }

    void CopyCode()
    {
        if (SteamLobby.Current == null) return;

        DisplayServer.ClipboardSet(SteamLobby.Current.Code.ToString());
        status.Text = "Code copied.";
    }

    async void JoinFromClipboard()
    {
        if (!ulong.TryParse(DisplayServer.ClipboardGet().Trim(), out ulong code))
        {
            status.Text = "The clipboard doesn't hold a lobby code.";
            return;
        }

        joinButton.Disabled = true;
        status.Text = "Joining...";

        // Joining replaces whatever we were in — SteamLobby only drops the old one once the new one
        // is confirmed, so a failed join leaves us hosting exactly as we were.
        if (await SteamLobby.Join(code) == null)
            status.Text = "Couldn't join — wrong code, lobby closed, or already full.";
        else
            status.Text = "";

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
