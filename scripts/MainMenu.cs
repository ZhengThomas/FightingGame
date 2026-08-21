using Godot;

// First screen. Three buttons; Play opens a second column of modes beside it. Versus and training
// go straight into a match, online swaps in the lobby page.
//
// It owns the netplay knobs and the one way into the fighting scene, so every page that starts a
// match asks it rather than launching one itself.
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

    // Loopback and pretend connections are for testing netcode without two machines. Off in
    // anything a player sees.
    [Export] public bool DevTransports = false;

    Control title, columns, modes;
    Godot.Button play, localVersus;
    OptionsMenu options;
    LobbyMenu lobby;

    public override void _Ready()
    {
        title = GetNode<Control>("Title");
        columns = GetNode<Control>("Columns");
        modes = GetNode<Control>("Columns/Modes");
        play = GetNode<Godot.Button>("Columns/Main/Play");
        localVersus = GetNode<Godot.Button>("Columns/Modes/Local");
        options = GetNode<OptionsMenu>("OptionsMenu");
        lobby = GetNode<LobbyMenu>("Lobby");

        play.Pressed += ToggleModes;
        GetNode<Godot.Button>("Columns/Main/Options").Pressed += OpenOptions;
        GetNode<Godot.Button>("Columns/Main/Quit").Pressed += () => GetTree().Quit();

        localVersus.Pressed += () => Begin(null);
        GetNode<Godot.Button>("Columns/Modes/Training").Pressed += () => Begin(null, GameMode.Training);
        GetNode<Godot.Button>("Columns/Modes/Online").Pressed += OpenLobby;

        var udp = GetNode<Godot.Button>("Columns/Modes/Udp");
        var fake = GetNode<Godot.Button>("Columns/Modes/Fake");
        udp.Visible = DevTransports;
        fake.Visible = DevTransports;
        udp.Pressed += () => Begin(MakeUdpTransport());
        fake.Pressed += () => Begin(MakeFakeTransport());

        // Both pages cover the screen, so the menu goes away rather than staying clickable
        // underneath. Each page reports that it closed and has no idea what opened it.
        options.Closed += ShowFrontPage;
        lobby.Closed += ShowFrontPage;

        lobby.InputDelay = InputDelay;
        lobby.StartMatch = (transport, delay) => Begin(transport, delay);

        FocusFollowsMouse.Apply(this);
        modes.Hide();
        play.GrabFocus();
    }

    // Escape closes the mode column. The pages handle their own, so this stands down while one is up.
    public override void _Input(InputEvent e)
    {
        if (options.Visible || lobby.Visible || !modes.Visible) return;
        if (!e.IsActionPressed("ui_cancel")) return;

        modes.Hide();
        play.GrabFocus();
        GetViewport().SetInputAsHandled();
    }

    // Play is a toggle, so the second column closes the same way it opened.
    void ToggleModes()
    {
        if (modes.Visible)
        {
            modes.Hide();
            return;
        }

        modes.Show();
        localVersus.GrabFocus();
    }

    // Coming back always lands on the three buttons rather than whatever column was open when the
    // page changed.
    void ShowFrontPage()
    {
        modes.Hide();
        columns.Show();
        title.Show();
        play.GrabFocus();
    }

    // The pages cover the screen but don't all paint a background, so the title has to go with the
    // buttons rather than sit on top of whatever opened.
    void HideFrontPage()
    {
        columns.Hide();
        title.Hide();
    }

    void OpenOptions()
    {
        HideFrontPage();
        options.Open();
    }

    void OpenLobby()
    {
        HideFrontPage();
        lobby.Open();
    }

    // An accepted invite joins a lobby with nothing on screen asking for one, so the page has to
    // come up by itself or the player is in a lobby they can't see.
    public override void _Process(double delta)
    {
        if (SteamLobby.Current != null && !lobby.Visible) OpenLobby();
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
