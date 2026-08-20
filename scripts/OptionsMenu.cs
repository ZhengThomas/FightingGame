using Godot;
using System.Collections.Generic;

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
    RebindPopup rebind;
    // Everything the popup has to take out of the focus order while it's up.
    Control[] background;

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

        Godot.Button p1 = GetNode<Godot.Button>(PagesPath + "ControlsPage/Rows/Player1Button");
        Godot.Button p2 = GetNode<Godot.Button>(PagesPath + "ControlsPage/Rows/Player2Button");
        p1.Pressed += () => OpenRebind(1);
        p2.Pressed += () => OpenRebind(2);

        rebind = GetNode<RebindPopup>("RebindPopup");
        rebind.Closed += CloseRebind;

        background = CollectFocusable(GetNode<Control>("Margin"));

        // Shown over a paused match, where the theme's translucent styles read as smeared.
        OpaqueTheme.Apply(this);
        FocusFollowsMouse.Apply(this);

        ShowPage(0);
    }

    void OpenRebind(int player)
    {
        SetBackgroundFocusable(false);
        rebind.Open(player);
    }

    void CloseRebind()
    {
        SetBackgroundFocusable(true);
        GetNode<Godot.Button>(PagesPath + "ControlsPage/Rows/Player1Button").GrabFocus();
    }

    // The dimmer stops the mouse reaching the menu, but focus navigation would still walk into it.
    // Making the background unfocusable is what actually makes the popup modal.
    void SetBackgroundFocusable(bool on)
    {
        foreach (Control c in background)
            c.FocusMode = on ? FocusModeEnum.All : FocusModeEnum.None;
    }

    static Control[] CollectFocusable(Node root)
    {
        var found = new List<Control>();
        Walk(root, found);
        return found.ToArray();
    }

    static void Walk(Node node, List<Control> into)
    {
        foreach (Node child in node.GetChildren())
        {
            // Anything already unfocusable stays that way — the arrow buttons inside a row are
            // deliberately skipped by navigation and restoring them would change behaviour.
            if (child is Control c && c.FocusMode != FocusModeEnum.None) into.Add(c);
            Walk(child, into);
        }
    }

    // Escape backs out, same as the Back button. The popup handles its own escape first, so this
    // stands down while it's up rather than closing both at once.
    public override void _Input(InputEvent e)
    {
        if (!Visible || rebind.Visible || !e.IsActionPressed("ui_cancel")) return;

        Close();
        GetViewport().SetInputAsHandled();
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
