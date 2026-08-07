using Godot;

public partial class DebugManager : Node
{
    public bool IsFrozen { get; private set; } = false;
    bool advanceRequested = false;

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("debug_freeze")){
            IsFrozen = !IsFrozen;
		}

        if (IsFrozen && Input.IsActionJustPressed("debug_advance"))
            advanceRequested = true;
    }

    // True if the sim should advance this frame, clearing any pending single-step request.
    public bool ConsumeShouldTick()
    {
        if (!IsFrozen) return true;
        if (!advanceRequested) return false;
        advanceRequested = false;
        return true;
    }
}