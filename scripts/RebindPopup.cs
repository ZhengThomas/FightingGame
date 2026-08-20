using Godot;

// The controls editor, shown over the options menu. Stubbed: it lists what each player's buttons
// are currently bound to, but pressing a row doesn't rebind anything yet.
//
// The rows are BindRow instances placed in the scene, so their names and order are edited there.
// This only pushes the current keys into them.
//
// It doesn't block input on its own — whoever opens it takes the background out of the focus order.
// A Control has no modal mode in Godot 4, so that has to be somebody's job, and the opener is the
// only one that knows what "the background" is.
public partial class RebindPopup : Control
{
    [Signal] public delegate void ClosedEventHandler();

    Label title;
    VBoxContainer rows;
    Godot.Button close;

    public override void _Ready()
    {
        title = GetNode<Label>("Panel/Margin/Box/Title");
        rows = GetNode<VBoxContainer>("Panel/Margin/Box/Scroll/List/Rows");
        close = GetNode<Godot.Button>("Panel/Margin/Box/Close");
        close.Pressed += Close;

        Hide();
    }

    public void Open(int player)
    {
        title.Text = $"Player {player} Controls";
        InputBindings bindings = player == 1 ? InputManager.P1Bindings : InputManager.P2Bindings;
        foreach (Node child in rows.GetChildren())
            (child as BindRow)?.ShowBinding(bindings);

        Show();
        // Focus the first bind so the popup owns the keyboard the moment it appears.
        if (rows.GetChildCount() > 0 && rows.GetChild(0) is BindRow first) first.FocusKey();
        else close.GrabFocus();
    }

    // Escape backs out, the same as the Close button.
    public override void _Input(InputEvent e)
    {
        if (!Visible || !e.IsActionPressed("ui_cancel")) return;

        Close();
        GetViewport().SetInputAsHandled();
    }

    void Close()
    {
        Hide();
        EmitSignal(SignalName.Closed);
    }
}
