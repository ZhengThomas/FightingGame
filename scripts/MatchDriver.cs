using Godot;

// Owns the order of operations for one real frame: collect this tick's inputs, advance the sim,
// then draw. Nothing else runs _PhysicsProcess, so the sequence is defined here rather than
// emerging from autoload registration order.
//
// Rollback slots in between the input poll and the tick — restore a past snapshot, then call
// Tick() repeatedly to catch back up to the present. That needs a single caller that treats
// "how many ticks is this frame worth" as a decision, which is what this class is for.
public partial class MatchDriver : Node
{
    DebugManager debug;
    InputManager inputManager;
    MatchManager match;

    public override void _Ready()
    {
        debug = GetNode<DebugManager>("/root/DebugManager");
        inputManager = GetNode<InputManager>("/root/InputManager");
        match = GetNode<MatchManager>("/root/MatchManager");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (debug.ConsumeShouldTick())
        {
            // Inputs are recorded under the frame the tick is about to consume.
            inputManager.PollLocalInputs(match.FrameCount);
            match.Tick();
        }

        // Runs whether or not the sim advanced, so boxes stay visible while frozen.
        match.DrawDebugBoxes();
    }
}
