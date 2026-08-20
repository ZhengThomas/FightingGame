using Godot;

// One row of the controls list: what the action is called on the left, the key it's on as a button
// on the right.
//
// The name and the order live in the scene; only the key text comes from data, because that's the
// only part that changes between players and between saves.
[Tool]
public partial class BindRow : HBoxContainer
{
    // The key box was pressed — the popup decides what listening for a key means.
    [Signal] public delegate void RebindRequestedEventHandler();

    [Export] public string Action { get => action; set { action = value; RefreshName(); } }

    // Which button this row edits. Drives nothing in the editor — it's read when the popup opens.
    [Export] public Button Bind { get; set; }

    string action = "Action";
    Label nameLabel;
    Godot.Button keyButton;

    public override void _Ready()
    {
        nameLabel = GetNode<Label>("Name");
        keyButton = GetNode<Godot.Button>("Key");
        RefreshName();

        if (Engine.IsEditorHint()) return;
        keyButton.Pressed += RequestRebind;
    }

    void RequestRebind() => EmitSignal(SignalName.RebindRequested);

    // Waiting for a key. The box empties so it's obvious the old bind no longer applies.
    public void ShowCapturing() => keyButton.Text = "-";

    public void FocusKey() => keyButton.GrabFocus();

    // Called each time the popup opens so the row shows whichever player is being edited.
    public void ShowBinding(InputBindings bindings)
    {
        Key key = bindings.KeyFor(Bind);
        keyButton.Text = key == Key.None ? "-" : OS.GetKeycodeString(key);
    }

    void RefreshName()
    {
        if (nameLabel != null) nameLabel.Text = action;
    }
}
