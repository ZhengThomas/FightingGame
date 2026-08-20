using Godot;

// First screen. Picks how the next match is connected, hands that to MatchDriver, then swaps in the
// fighting scene.
public partial class MainMenu : Control
{
    const string MatchScene = "res://node_3d.tscn";

    // InputDelay needs to be >= FakeLatencyTicks + FakeJitterTicks or the game constantly stalls.
    // Set it lower on purpose to watch that happen.
    [Export] public int InputDelay = 2;
    [Export] public int RollbackFrames = 5;

    // Bad-connection conditions faked on top of whatever the transport really does. Two windows on
    // one desk talk in well under a millisecond with no loss, so without these a test proves little.
    [Export] public int FakeLatencyTicks = 4;   // ~66ms at 60fps
    [Export] public int FakeJitterTicks = 2;
    [Export] public int FakeDropPercent = 5;

    // Which character this instance drives when using the pretend connection. The real one works it
    // out from which port it managed to grab.
    [Export] public int FakeLocalPlayer = 1;

    Godot.Button hostButton;
    Godot.Button copyButton;
    Godot.Button joinButton;
    Godot.Button inviteButton;
    Godot.Button startButton;
    LineEdit codeEntry;
    Label lobbyStatus;

    bool starting;      // one-shot: _Process runs on until the scene swap lands a frame later
    bool announced;     // the host's own copy, not read back off the lobby
    bool startFailure;  // already complained once

    void OnStart()
    {
        SteamLobby lobby = SteamLobby.Current;
        if (lobby == null) return;

        // A refused write means the guest never sees the go signal, so starting here would leave us
        // alone in a match.
        if (!lobby.AnnounceStart(InputDelay))
        {
            GD.PushError("Steam rejected the start signal. Press Start again.");
            return;
        }

        announced = true;
    }

    Control buttons;
    OptionsMenu options;

    // Options covers the whole screen, so the menu behind it goes away rather than staying clickable
    // underneath. Both halves are driven from here — the options scene reports that it closed and
    // has no idea what opened it.
    void OpenOptions()
    {
        buttons.Hide();
        options.Open();
    }

    public override void _Ready()
    {
        // Fully qualified — `Button` on its own is this project's input-button enum.
        var local = GetNode<Godot.Button>("Buttons/Local");
        var udp = GetNode<Godot.Button>("Buttons/Udp");
        var fake = GetNode<Godot.Button>("Buttons/Fake");

        local.Pressed += () => Begin(null);
        GetNode<Godot.Button>("Buttons/Training").Pressed += () => Begin(null, GameMode.Training);
        udp.Pressed += () => Begin(MakeUdpTransport());
        fake.Pressed += () => Begin(MakeFakeTransport());

        GetNode<Label>("Buttons/SteamStatus").Text = SteamManager.Available
            ? $"Steam: {SteamManager.PersonaName}"
            : "Steam: not running";

        hostButton = GetNode<Godot.Button>("Buttons/Host");
        copyButton = GetNode<Godot.Button>("Buttons/Copy");
        joinButton = GetNode<Godot.Button>("Buttons/Join");
        inviteButton = GetNode<Godot.Button>("Buttons/Invite");
        startButton = GetNode<Godot.Button>("Buttons/Start");
        codeEntry = GetNode<LineEdit>("Buttons/CodeEntry");
        lobbyStatus = GetNode<Label>("Buttons/LobbyStatus");

        hostButton.Pressed += OnHost;
        joinButton.Pressed += OnJoin;
        // All three need a lobby, and their Disabled state is a frame behind — Godot handles input
        // before _Process, so a click is judged against last frame's answer.
        copyButton.Disabled = true;
        inviteButton.Disabled = true;
        startButton.Disabled = true;

        copyButton.Pressed += () =>
        {
            if (SteamLobby.Current != null) DisplayServer.ClipboardSet(SteamLobby.Current.Code.ToString());
        };
        inviteButton.Pressed += () => SteamLobby.Current?.OpenInviteOverlay();
        startButton.Pressed += OnStart;
        GetNode<Godot.Button>("Buttons/Paste").Pressed += () => codeEntry.Text = DisplayServer.ClipboardGet();

        if (!SteamManager.Available)
        {
            hostButton.Disabled = true;
            joinButton.Disabled = true;
            lobbyStatus.Text = "Steam isn't running, so lobbies are unavailable.";
        }

        buttons = GetNode<Control>("Buttons");
        options = GetNode<OptionsMenu>("OptionsMenu");
        GetNode<Godot.Button>("Buttons/Options").Pressed += OpenOptions;
        options.Closed += buttons.Show;

        local.GrabFocus();
    }

    async void OnHost()
    {
        hostButton.Disabled = true;
        lobbyStatus.Text = "Creating a lobby...";
        if (await SteamLobby.Host() == null) lobbyStatus.Text = "Steam wouldn't make a lobby.";
        hostButton.Disabled = false;
    }

    async void OnJoin()
    {
        if (!ulong.TryParse(codeEntry.Text.Trim(), out ulong code))
        {
            lobbyStatus.Text = "That doesn't look like a lobby code.";
            return;
        }

        joinButton.Disabled = true;
        lobbyStatus.Text = "Joining...";
        if (await SteamLobby.Join(code) == null)
            lobbyStatus.Text = "Couldn't join — wrong code, lobby closed, or already full.";
        joinButton.Disabled = false;
    }

    // Polled rather than event-driven. Only runs while the menu is up, and reading the lobby hits
    // the Steam client's local cache rather than the network.
    public override void _Process(double delta)
    {
        // Both need a lobby to act on, and one can appear without the menu doing anything — an
        // accepted invite joins in the background.
        SteamLobby lobby = SteamLobby.Current;
        copyButton.Disabled = lobby == null;
        inviteButton.Disabled = lobby == null;
        // Only the host starts it, and only once there's someone to play.
        startButton.Disabled = lobby == null || !lobby.IsHost || !lobby.IsFull;
        if (lobby == null) return;

        lobbyStatus.Text = $"Lobby {lobby.Code}\n{lobby.MemberNames()}\n"
            + $"You are player {lobby.LocalPlayerNumber}"
            + (lobby.IsFull ? "" : " — waiting for an opponent");

        // Both sides land here: the host from its own button, the other on seeing the lobby say so.
        if ((announced || lobby.MatchStarted) && !starting)
            TryStart(lobby);
    }

    // `starting` is set last, so a throw in here is retried next frame rather than wedging the menu
    // behind its own guard.
    void TryStart(SteamLobby lobby)
    {
        try
        {
            // The host's number, so both sides prime the same frames. Falls back to our own only on
            // the host, where the two are the same value anyway.
            int delay = lobby.AgreedInputDelay >= 0 ? lobby.AgreedInputDelay : InputDelay;

            lobby.SealForMatch();
            Begin(new NetTransport(new SteamChannel(lobby)), delay);
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

    // Built by hand rather than with ChangeSceneToFile so the setup is in place before anything in
    // the fighting scene runs — the players register with MatchManager as it's added to the tree.
    void Begin(IInputTransport transport, GameMode mode = GameMode.Versus)
        => Begin(transport, InputDelay, mode);

    // Only the delay is passed in: over a connection both sides must use the same one, and that's
    // the host's. RollbackFrames stays local — it only decides how far ahead of confirmed input
    // this machine will run, so the two sides may differ without diverging.
    void Begin(IInputTransport transport, int inputDelay, GameMode mode = GameMode.Versus)
    {
        Node match = GD.Load<PackedScene>(MatchScene).Instantiate();
        match.GetNode<MatchDriver>("MatchDriver").Setup = new MatchSetup
        {
            Transport = transport,
            Mode = mode,
            InputDelay = inputDelay,
            RollbackFrames = RollbackFrames,
        };

        GetTree().Root.AddChild(match);
        GetTree().CurrentScene = match;
        QueueFree();
    }

    // Another copy of the game on this machine, over loopback. Run two instances and press this in
    // both.
    IInputTransport MakeUdpTransport() => new NetTransport(new UdpChannel())
    {
        ExtraLatencyTicks = FakeLatencyTicks,
        JitterTicks = FakeJitterTicks,
        DropPercent = FakeDropPercent,
    };

    // No second copy — the opponent is the other set of keys on this keyboard, delivered late.
    IInputTransport MakeFakeTransport()
    {
        var fake = new FakeNetwork(seed: 12345)
        {
            LocalPlayerNumber = FakeLocalPlayer,
            DelayTicks = FakeLatencyTicks,
            JitterTicks = FakeJitterTicks,
            DropPercent = FakeDropPercent,
        };
        fake.PeerInputSource = _ => InputManager.ReadInputFor(FakeLocalPlayer == 1 ? 2 : 1);
        return fake;
    }
}
