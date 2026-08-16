using Godot;

// Shows each player's rematch/quit choice once the match is decided. Player 1 on the left, player 2
// on the right.
//
// Display only, deliberately: it reads MatchManager and never handles input. A clicked Godot button
// would be a local event the other machine never sees, so the choices come off the input log along
// with everything else, and this just draws whatever the simulation currently says.
public partial class MatchOverMenu : CanvasLayer
{
    static readonly Color Idle = new Color(0.55f, 0.55f, 0.6f);
    static readonly Color Hovered = new Color(1f, 1f, 1f);
    static readonly Color Locked = new Color(0.4f, 1f, 0.5f);

    Control root;
    Label[] p1Options;
    Label[] p2Options;
    Label banner;

    public override void _Ready()
    {
        root = GetNode<Control>("Root");
        banner = GetNode<Label>("Root/Banner");
        p1Options = new[] { GetNode<Label>("Root/P1/Rematch"), GetNode<Label>("Root/P1/Quit") };
        p2Options = new[] { GetNode<Label>("Root/P2/Rematch"), GetNode<Label>("Root/P2/Quit") };
    }

    public override void _Process(double delta)
    {
        MatchManager match = MatchManager.Current;
        if (match == null) return;

        // Ending still shows it — the scene hangs around a few frames before leaving.
        RoundFlow flow = match.Flow;
        root.Visible = flow.Decided;
        if (!flow.Decided) return;

        banner.Text = $"PLAYER {flow.Winner} WINS";
        Paint(p1Options, flow.HoverFor(1), flow.ChoiceFor(1));
        Paint(p2Options, flow.HoverFor(2), flow.ChoiceFor(2));
    }

    static void Paint(Label[] options, MatchOverChoice hover, MatchOverChoice choice)
    {
        Tint(options[0], MatchOverChoice.Rematch, hover, choice);
        Tint(options[1], MatchOverChoice.Quit, hover, choice);
    }

    static void Tint(Label label, MatchOverChoice option, MatchOverChoice hover, MatchOverChoice choice)
    {
        label.Modulate = choice == option ? Locked
            : choice == MatchOverChoice.None && hover == option ? Hovered
            : Idle;
    }
}
