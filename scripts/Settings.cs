using Godot;

// Player preferences — display, audio, controls. An autoload because the main menu and the pause
// menu both edit the same values and the game reads them from anywhere; if a menu owned them there'd
// be two copies quietly drifting apart.
//
// Adding a setting is three edits in this file and nowhere else: the property, a line in Load, a
// line in Save. Apply is where a value actually reaches the engine.
public partial class Settings : Node
{
    public static Settings Current { get; private set; }

    const string SavePath = "user://settings.cfg";
    const string Section = "options";

    // --- Display ---
    public bool Fullscreen { get; set; } = false;

    // Windowed size only — fullscreen takes the monitor's.
    public Vector2I Resolution { get; set; } = new Vector2I(1152, 648);

    // --- Audio --- kept as 0..1 because that's what a slider wants; decibels happen in Apply.
    public float MasterVolume { get; set; } = 1f;

    public override void _EnterTree()
    {
        Current = this;
        Load();
        Apply();
    }

    // Pushes the values at the engine. Called after loading and whenever the options menu changes
    // something, so a change is heard immediately rather than after a restart.
    public void Apply()
    {
        DisplayServer.WindowSetMode(Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);

        // Resizing a fullscreen window fights the mode change, and the size is restored on the way
        // back to windowed anyway.
        if (!Fullscreen)
        {
            DisplayServer.WindowSetSize(Resolution);
            Vector2I screen = DisplayServer.ScreenGetSize();
            DisplayServer.WindowSetPosition((screen - Resolution) / 2);
        }

        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"),
                                   Mathf.LinearToDb(MasterVolume));
    }

    // Missing file or missing key leaves the default in place, so a new install and a half-written
    // config behave the same.
    public void Load()
    {
        var file = new ConfigFile();
        if (file.Load(SavePath) != Error.Ok) return;

        Fullscreen = file.GetValue(Section, "fullscreen", Fullscreen).AsBool();
        Resolution = file.GetValue(Section, "resolution", Resolution).AsVector2I();
        MasterVolume = file.GetValue(Section, "master_volume", MasterVolume).AsSingle();
    }

    public void Save()
    {
        var file = new ConfigFile();
        file.SetValue(Section, "fullscreen", Fullscreen);
        file.SetValue(Section, "resolution", Resolution);
        file.SetValue(Section, "master_volume", MasterVolume);
        file.Save(SavePath);
    }
}
