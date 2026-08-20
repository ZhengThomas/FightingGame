using Godot;

// The controls editor, shown over the options menu. Pressing a row's key box listens for the next
// key and binds it; a key already in use is refused and the old bind stays.
//
// The rows are BindRow instances placed in the scene, so their names and order are edited there.
// This only pushes the current keys into them and takes new ones back out.
//
// It doesn't block input on its own — whoever opens it takes the background out of the focus order.
// A Control has no modal mode in Godot 4, so that has to be somebody's job, and the opener is the
// only one that knows what "the background" is.
public partial class RebindPopup : Control
{
    [Signal] public delegate void ClosedEventHandler();

    Label title, error;
    VBoxContainer rows;
    Godot.Button close;

    int player = 1;
    // Non-null while waiting for a key, which is also what makes this swallow every key event.
    BindRow capturing;

    public override void _Ready()
    {
        title = GetNode<Label>("Panel/Margin/Box/Title");
        error = GetNode<Label>("Panel/Margin/Box/Error");
        rows = GetNode<VBoxContainer>("Panel/Margin/Box/Scroll/List/Rows");
        close = GetNode<Godot.Button>("Panel/Margin/Box/Close");
        close.Pressed += Close;

        foreach (Node child in rows.GetChildren())
            if (child is BindRow row)
                row.RebindRequested += () => BeginCapture(row);

        Hide();
    }

    public void Open(int playerNumber)
    {
        player = playerNumber;
        title.Text = $"Player {player} Controls";
        capturing = null;
        error.Text = "";
        RefreshRows();

        Show();
        // Focus the first bind so the popup owns the keyboard the moment it appears.
        if (rows.GetChildCount() > 0 && rows.GetChild(0) is BindRow first) first.FocusKey();
        else close.GrabFocus();
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;

        if (capturing != null)
        {
            // Everything is swallowed while listening, or the key being bound would also press
            // whatever it's bound to in the menu.
            if (e is InputEventKey key && key.Pressed && !key.Echo)
            {
                Capture(key.Keycode);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (!e.IsActionPressed("ui_cancel")) return;

        Close();
        GetViewport().SetInputAsHandled();
    }

    void BeginCapture(BindRow row)
    {
        capturing = row;
        error.Text = "";
        row.ShowCapturing();
    }

    void Capture(Key key)
    {
        BindRow row = capturing;
        capturing = null;

        // Escape backs out of listening rather than binding itself, since it's how the popup and
        // every menu above it are closed.
        if (key == Key.Escape)
        {
            RefreshRows();
            return;
        }

        if (Taken(key, row.Bind, out string owner))
        {
            error.Text = $"{OS.GetKeycodeString(key)} is already used by {owner}.";
            RefreshRows();
            return;
        }

        // Settings is where bindings live, so without it there's nowhere to put this.
        if (Settings.Current == null) return;

        InputBindings bindings = InputManager.BindingsFor(player);
        bindings.SetKey(row.Bind, key);

        if (player == 1) Settings.Current.Player1 = bindings;
        else Settings.Current.Player2 = bindings;

        RefreshRows();
    }

    // Both players share one keyboard in local versus, so a clash across players is as broken as
    // one within a player.
    bool Taken(Key key, Button ignoring, out string owner)
    {
        for (int p = 1; p <= 2; p++)
        {
            InputBindings bindings = InputManager.BindingsFor(p);
            foreach (Button button in InputBindings.Bindable)
            {
                if (p == player && button == ignoring) continue;
                if (bindings.KeyFor(button) != key) continue;

                owner = $"Player {p} {button}";
                return true;
            }
        }

        owner = null;
        return false;
    }

    void RefreshRows()
    {
        InputBindings bindings = InputManager.BindingsFor(player);
        foreach (Node child in rows.GetChildren())
            (child as BindRow)?.ShowBinding(bindings);
    }

    void Close()
    {
        capturing = null;
        Hide();
        EmitSignal(SignalName.Closed);
    }
}
