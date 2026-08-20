using Godot;

// One settings row: a name on the left, "< value >" on the right. Left/right cycle the choice,
// accept commits it.
//
// The row itself takes focus and the arrow buttons don't, so navigation lands here once rather than
// on three separate targets. Left/right are swallowed while focused, which is why they cycle the
// value instead of moving focus sideways.
//
// [Tool] so the editor shows the real title and value rather than the placeholders.
[Tool]
public partial class OptionRow : HBoxContainer
{
    // Arrow pressed — the shown choice moved.
    [Signal] public delegate void ChangedEventHandler(int index);
    // Accept pressed — the shown choice is the one the player wants.
    [Signal] public delegate void CommittedEventHandler(int index);

    [Export] public string Title { get => title; set { title = value; RefreshTitle(); } }
    [Export] public string[] Choices { get => choices; set { choices = value; RefreshValue(); } }

    string title = "Option";
    string[] choices = System.Array.Empty<string>();
    int index;

    Label titleLabel, valueLabel;

    // Which choice is shown. Setting it doesn't emit — that's for loading saved values.
    public int Index
    {
        get => index;
        set { index = Wrap(value); RefreshValue(); }
    }

    public override void _Ready()
    {
        titleLabel = GetNode<Label>("Title");
        valueLabel = GetNode<Label>("Value");
        GetNode<Godot.Button>("Left").Pressed += () => Step(-1);
        GetNode<Godot.Button>("Right").Pressed += () => Step(1);

        RefreshTitle();
        RefreshValue();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_left")) Step(-1);
        else if (e.IsActionPressed("ui_right")) Step(1);
        else if (e.IsActionPressed("ui_accept")) EmitSignal(SignalName.Committed, index);
        else return;

        // Stops the event reaching focus navigation, which would otherwise move off this row.
        AcceptEvent();
    }

    void Step(int direction)
    {
        if (choices.Length == 0) return;

        GrabFocus();
        index = Wrap(index + direction);
        RefreshValue();
        EmitSignal(SignalName.Changed, index);
    }

    int Wrap(int i)
    {
        if (choices.Length == 0) return 0;
        return (i % choices.Length + choices.Length) % choices.Length;
    }

    // Both guard on the label because the setters run while the scene loads, before _Ready.
    void RefreshTitle()
    {
        if (titleLabel != null) titleLabel.Text = title;
    }

    void RefreshValue()
    {
        if (valueLabel != null)
            valueLabel.Text = choices.Length == 0 ? "-" : choices[Wrap(index)];
    }
}
