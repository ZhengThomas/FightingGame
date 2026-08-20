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
    TrainingMenu trainingMenu;
    Godot.Button resume, optionsButton, trainingButton, quit;
    MatchDriver driver;

    public override void _Ready()
    {
        root = GetNode<Control>("Root");
        options = GetNode<OptionsMenu>("Root/OptionsMenu");
        trainingMenu = GetNode<TrainingMenu>("Root/TrainingMenu");
        resume = GetNode<Godot.Button>("Root/Panel/Margin/Box/Resume");
        optionsButton = GetNode<Godot.Button>("Root/Panel/Margin/Box/Options");
        trainingButton = GetNode<Godot.Button>("Root/Panel/Margin/Box/Training");
        quit = GetNode<Godot.Button>("Root/Panel/Margin/Box/Quit");
        driver = GetNode<MatchDriver>("../MatchDriver");

        resume.Pressed += Resume;
        optionsButton.Pressed += OpenOptions;
        trainingButton.Pressed += OpenTraining;
        quit.Pressed += QuitToMenu;
        options.Closed += OnSubMenuClosed;
        trainingMenu.Closed += OnSubMenuClosed;

        // Only the panel — the options menu wires its own subtree in its _Ready.
        FocusFollowsMouse.Apply(GetNode("Root/Panel"));

        root.Hide();
    }

    // Leaving the scene while paused would leave the whole tree frozen for whatever loads next.
    public override void _ExitTree() => GetTree().Paused = false;

    public override void _Input(InputEvent e)
    {
        // The sub-menus handle their own escape, innermost first.
        if (options.Visible || trainingMenu.Visible || !e.IsActionPressed("ui_cancel")) return;

        if (root.Visible) Resume();
        else if (!driver.IsOnline) Pause();
        else return;

        GetViewport().SetInputAsHandled();
    }

    void Pause()
    {
        // Asked here rather than in _Ready: the mode is built in MatchDriver's own _Ready, and this
        // way the button is right even if that order ever changes.
        trainingButton.Visible = driver.Mode is TrainingMode;

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

    void OpenTraining()
    {
        if (driver.Mode is TrainingMode training) trainingMenu.Open(training);
    }

    // A sub-menu closing leaves the pause menu up, so focus has to come back to something.
    void OnSubMenuClosed() => resume.GrabFocus();

    void QuitToMenu()
    {
        GetTree().Paused = false;
        driver.ReturnToMenu();
    }
}
