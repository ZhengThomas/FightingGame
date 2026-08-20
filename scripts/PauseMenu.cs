using Godot;

// The in-match pause screen. Escape opens it, and only offline: a paused game stops ticking, and a
// machine that stops ticking mid-match stalls its opponent with no explanation.
//
// Pausing is GetTree().Paused, so nothing in the match has to know this exists. This layer runs
// with ProcessMode.Always to stay alive while everything under it is frozen.
public partial class PauseMenu : CanvasLayer
{
    Control root;
    OptionsMenu options;
    Godot.Button resume, optionsButton, quit;
    MatchDriver driver;

    public override void _Ready()
    {
        root = GetNode<Control>("Root");
        options = GetNode<OptionsMenu>("Root/OptionsMenu");
        resume = GetNode<Godot.Button>("Root/Panel/Margin/Box/Resume");
        optionsButton = GetNode<Godot.Button>("Root/Panel/Margin/Box/Options");
        quit = GetNode<Godot.Button>("Root/Panel/Margin/Box/Quit");
        driver = GetNode<MatchDriver>("../MatchDriver");

        resume.Pressed += Resume;
        optionsButton.Pressed += OpenOptions;
        quit.Pressed += QuitToMenu;
        options.Closed += OnOptionsClosed;

        // Only the panel — the options menu wires its own subtree in its _Ready.
        FocusFollowsMouse.Apply(GetNode("Root/Panel"));

        root.Hide();
    }

    // Leaving the scene while paused would leave the whole tree frozen for whatever loads next.
    public override void _ExitTree() => GetTree().Paused = false;

    public override void _Input(InputEvent e)
    {
        // The options screen and its popup handle their own escape, innermost first.
        if (options.Visible || !e.IsActionPressed("ui_cancel")) return;

        if (root.Visible) Resume();
        else if (!driver.IsOnline) Pause();
        else return;

        GetViewport().SetInputAsHandled();
    }

    void Pause()
    {
        GetTree().Paused = true;
        root.Show();
        resume.GrabFocus();
    }

    void Resume()
    {
        GetTree().Paused = false;
        root.Hide();
    }

    void OpenOptions() => options.Open();

    // Options closing leaves the pause menu up, so focus has to come back to something.
    void OnOptionsClosed() => resume.GrabFocus();

    void QuitToMenu()
    {
        GetTree().Paused = false;
        driver.ReturnToMenu();
    }
}
