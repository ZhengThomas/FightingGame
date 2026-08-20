using Godot;

// Training settings, reached from the pause menu and only in training. Built out of the same
// OptionRow the options screen uses, so the rows behave identically.
//
// It edits TrainingSettings directly. Nothing has to be applied or notified — the mode reads those
// fields fresh every frame, so setting one is the whole interaction.
public partial class TrainingMenu : Control
{
    [Signal] public delegate void ClosedEventHandler();

    const string ListPath = "Margin/Rows/Body/Pad/List/";

    OptionRow healthRegen;
    Godot.Button back;
    TrainingSettings settings;

    public override void _Ready()
    {
        healthRegen = GetNode<OptionRow>(ListPath + "HealthRegenRow");
        back = GetNode<Godot.Button>("Margin/Rows/Footer/Back");

        back.Pressed += Close;
        healthRegen.Committed += i => Write(s => s.HealthRegen = i == 1);

        OpaqueTheme.Apply(this);
        FocusFollowsMouse.Apply(this);

        Hide();
    }

    public void Open(TrainingMode mode)
    {
        settings = mode.Settings;

        healthRegen.Index = settings.HealthRegen ? 1 : 0;

        Show();
        healthRegen.GrabFocus();
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || !e.IsActionPressed("ui_cancel")) return;

        Close();
        GetViewport().SetInputAsHandled();
    }

    void Write(System.Action<TrainingSettings> change)
    {
        if (settings != null) change(settings);
    }

    void Close()
    {
        Hide();
        EmitSignal(SignalName.Closed);
    }
}
