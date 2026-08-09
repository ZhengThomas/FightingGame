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

        if (Input.IsActionJustPressed("debug_rollback_test"))
        {
            RollbackTestEnabled = !RollbackTestEnabled;
            GD.Print($"Rollback self-test {(RollbackTestEnabled ? "ON" : "off")}");
        }
    }

    // While on, MatchDriver re-runs the last few ticks every frame and checks the result matches.
    public bool RollbackTestEnabled { get; private set; } = false;

    // Called when the self-test finds a mismatch, so one failure logs once instead of every frame.
    public void DisableRollbackTest() => RollbackTestEnabled = false;

    // True if the sim should advance this frame, clearing any pending single-step request.
    public bool ConsumeShouldTick()
    {
        if (!IsFrozen) return true;
        if (!advanceRequested) return false;
        advanceRequested = false;
        return true;
    }
}