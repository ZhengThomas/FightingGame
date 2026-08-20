using Godot;

// The options screen, shared by the main menu and the pause menu.
//
// A Control rather than a CanvasLayer on purpose: whoever opens it decides where it sits and what
// it draws on top of. A CanvasLayer would fix its own layer everywhere and stop nesting.
//
// It also knows nothing about who opened it — Back saves and reports Closed, and the host decides
// what that means. That's what keeps it from growing an "am I in the pause menu?" branch.
public partial class OptionsMenu : Control
{
    [Signal] public delegate void ClosedEventHandler();

    Godot.Button back;

    public override void _Ready()
    {
        back = GetNode<Godot.Button>("Panel/Back");
        back.Pressed += Close;
    }

    public void Open()
    {
        Show();
        back.GrabFocus();
    }

    // Every row writes straight to the Settings autoload, so closing only has to persist it.
    void Close()
    {
        Settings.Current?.Save();
        Hide();
        EmitSignal(SignalName.Closed);
    }
}
