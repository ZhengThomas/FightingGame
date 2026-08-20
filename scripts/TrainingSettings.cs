// The knobs a training pause menu edits, kept apart from TrainingMode so a menu can hold one
// without reaching into the mode. Read every frame rather than applied on change, so a menu only
// has to set a value and nothing has to be notified.
public class TrainingSettings
{
    // Nobody dies. Off makes training end rounds the way versus does.
    public bool InfiniteHealth = true;

    public bool HealthRegen = true;

    // Frames back in control before health starts returning, and how fast it comes back.
    public int RegenDelay = 15;
    public int RegenPerFrame = 16;
}
