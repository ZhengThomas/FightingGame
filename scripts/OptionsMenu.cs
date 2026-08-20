using Godot;

// The options screen, shared by the main menu and the pause menu.
//
// A Control rather than a CanvasLayer on purpose: whoever opens it decides where it sits and what
// it draws on top of. A CanvasLayer would fix its own layer everywhere and stop nesting.
//
// It also knows nothing about who opened it — Back saves and reports Closed, and the host decides
// what that means. That's what keeps it from growing an "am I in the pause menu?" branch.
public partial class OptionsMenu : Control
{
    [Signal] public delegate void ClosedEventHandler();

    // Index-matched to the Resolution row's Choices in the scene.
    static readonly Vector2I[] Resolutions =
    {
        new(1152, 648), new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440),
    };

    const string PagesPath = "Margin/Rows/Body/Pages/";

    Godot.Button back;
    Godot.Button[] tabs;
    Control[] pages;
    OptionRow resolution, fullscreen;
    OptionRow master, sfx, music;

    public override void _Ready()
    {
        back = GetNode<Godot.Button>("Margin/Rows/Footer/Back");
        back.Pressed += Close;

        tabs = new[]
        {
            GetNode<Godot.Button>("Margin/Rows/Tabs/Video"),
            GetNode<Godot.Button>("Margin/Rows/Tabs/Audio"),
            GetNode<Godot.Button>("Margin/Rows/Tabs/Controls"),
        };
        pages = new[]
        {
            GetNode<Control>(PagesPath + "VideoPage"),
            GetNode<Control>(PagesPath + "AudioPage"),
            GetNode<Control>(PagesPath + "ControlsPage"),
        };
        for (int i = 0; i < tabs.Length; i++)
        {
            int page = i;
            tabs[i].Pressed += () => ShowPage(page);
        }

        resolution = GetNode<OptionRow>(PagesPath + "VideoPage/Rows/ResolutionRow");
        fullscreen = GetNode<OptionRow>(PagesPath + "VideoPage/Rows/FullscreenRow");

        // Which rows apply on the arrow and which wait for accept is set per row in the scene.
        resolution.Committed += ApplyResolution;
        fullscreen.Committed += ApplyFullscreen;

        master = GetNode<OptionRow>(PagesPath + "AudioPage/Rows/MasterRow");
        sfx = GetNode<OptionRow>(PagesPath + "AudioPage/Rows/SfxRow");
        music = GetNode<OptionRow>(PagesPath + "AudioPage/Rows/MusicRow");

        master.Committed += i => ApplyVolume(s => s.MasterVolume = ToLinear(i));
        sfx.Committed += i => ApplyVolume(s => s.SfxVolume = ToLinear(i));
        music.Committed += i => ApplyVolume(s => s.MusicVolume = ToLinear(i));

        ShowPage(0);
    }

    public void Open()
    {
        LoadFromSettings();
        Show();
        ShowPage(0);
        // Lands on the tab, not inside the page — selecting a tab should leave it highlighted.
        tabs[0].GrabFocus();
    }

    void ShowPage(int index)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == index;
            // No signal, or switching pages from code would re-enter this through Pressed.
            tabs[i].SetPressedNoSignal(i == index);
        }
    }

    // Puts the rows on the saved values. Called on open so a cancelled edit elsewhere doesn't leave
    // the display stale.
    void LoadFromSettings()
    {
        Settings s = Settings.Current;
        if (s == null) return;

        resolution.Index = System.Array.IndexOf(Resolutions, s.Resolution) is int i && i >= 0 ? i : 0;
        fullscreen.Index = s.Fullscreen ? 1 : 0;

        master.Index = ToIndex(s.MasterVolume);
        sfx.Index = ToIndex(s.SfxVolume);
        music.Index = ToIndex(s.MusicVolume);
    }

    // The volume rows step in 5% intervals, so index 20 is full.
    static float ToLinear(int index) => index * 0.05f;
    static int ToIndex(float linear) => Mathf.Clamp(Mathf.RoundToInt(linear * 20f), 0, 20);

    void ApplyVolume(System.Action<Settings> write)
    {
        if (Settings.Current == null) return;
        write(Settings.Current);
        Settings.Current.ApplyAudio();
    }

    void ApplyResolution(int index)
    {
        if (Settings.Current == null) return;
        Settings.Current.Resolution = Resolutions[index];
        Settings.Current.ApplyDisplay();
    }

    void ApplyFullscreen(int index)
    {
        if (Settings.Current == null) return;
        Settings.Current.Fullscreen = index == 1;
        Settings.Current.ApplyDisplay();
    }

    // Every row writes straight to the Settings autoload, so closing only has to persist it.
    void Close()
    {
        Settings.Current?.Save();
        Hide();
        EmitSignal(SignalName.Closed);
    }
}
