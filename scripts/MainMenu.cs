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

    public override void _Ready()
    {
        // Fully qualified — `Button` on its own is this project's input-button enum.
        var local = GetNode<Godot.Button>("Buttons/Local");
        var udp = GetNode<Godot.Button>("Buttons/Udp");
        var fake = GetNode<Godot.Button>("Buttons/Fake");

        local.Pressed += () => Begin(null);
        udp.Pressed += () => Begin(MakeUdpTransport());
        fake.Pressed += () => Begin(MakeFakeTransport());

        local.GrabFocus();
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
