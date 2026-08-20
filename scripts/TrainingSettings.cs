// The knobs a training pause menu edits, kept apart from TrainingMode so a menu can hold one
// without reaching into the mode. Read every frame rather than applied on change, so a menu only
// has to set a value and nothing has to be notified.
public class TrainingSettings
{
    public bool HealthRegen = true;
}
