using Godot;

public partial class DebugManager : Node
{
    public bool IsFrozen { get; private set; } = false;
    public bool AdvanceOneFrame { get; private set; } = false;

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("debug_freeze")){
            IsFrozen = !IsFrozen;
		}

        AdvanceOneFrame = IsFrozen && Input.IsActionJustPressed("debug_advance");
    }

    public bool ShouldTick => !IsFrozen || AdvanceOneFrame;
}