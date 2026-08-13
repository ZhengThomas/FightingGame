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
    LineEdit codeEntry;
    Label lobbyStatus;

    public override void _Ready()
    {
        // Fully qualified — `Button` on its own is this project's input-button enum.
        var local = GetNode<Godot.Button>("Buttons/Local");
        var udp = GetNode<Godot.Button>("Buttons/Udp");
        var fake = GetNode<Godot.Button>("Buttons/Fake");

        local.Pressed += () => Begin(null);
        udp.Pressed += () => Begin(MakeUdpTransport());
        fake.Pressed += () => Begin(MakeFakeTransport());

        GetNode<Label>("Buttons/SteamStatus").Text = SteamManager.Available
            ? $"Steam: {SteamManager.PersonaName}"
            : "Steam: not running";

        hostButton = GetNode<Godot.Button>("Buttons/Host");
        copyButton = GetNode<Godot.Button>("Buttons/Copy");
        joinButton = GetNode<Godot.Button>("Buttons/Join");
        codeEntry = GetNode<LineEdit>("Buttons/CodeEntry");
        lobbyStatus = GetNode<Label>("Buttons/LobbyStatus");

        hostButton.Pressed += OnHost;
        joinButton.Pressed += OnJoin;
        copyButton.Pressed += () => DisplayServer.ClipboardSet(SteamLobby.Current.Code.ToString());
        GetNode<Godot.Button>("Buttons/Paste").Pressed += () => codeEntry.Text = DisplayServer.ClipboardGet();

        if (!SteamManager.Available)
        {
            hostButton.Disabled = true;
            joinButton.Disabled = true;
            lobbyStatus.Text = "Steam isn't running, so lobbies are unavailable.";
        }

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
        SteamLobby lobby = SteamLobby.Current;
        copyButton.Disabled = lobby == null;
        if (lobby == null) return;

        lobbyStatus.Text = $"Lobby {lobby.Code}\n{lobby.MemberNames()}\n"
            + $"You are player {lobby.LocalPlayerNumber}"
            + (lobby.IsFull ? "" : " — waiting for an opponent");
    }

    // Built by hand rather than with ChangeSceneToFile so the setup is in place before anything in
    // the fighting scene runs — the players register with MatchManager as it's added to the tree.
    void Begin(IInputTransport transport)
    {
        Node match = GD.Load<PackedScene>(MatchScene).Instantiate();
        match.GetNode<MatchDriver>("MatchDriver").Setup = new MatchSetup
        {
            Transport = transport,
            InputDelay = InputDelay,
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
