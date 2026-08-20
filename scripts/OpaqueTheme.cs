using Godot;

// Godot's default theme draws buttons and panels at 60% alpha. That's invisible over a solid
// background and obvious over a paused match, so anything shown on top of the game gets its styles
// copied with the alpha taken out.
//
// Done in code rather than as a Theme resource so the colours stay whatever the theme says — only
// the transparency changes.
public static class OpaqueTheme
{
    static readonly string[] ButtonStyles = { "normal", "hover", "pressed", "disabled", "focus" };

    public static void Apply(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            switch (child)
            {
                case Godot.Button button:
                    foreach (string style in ButtonStyles) Override(button, style, "Button");
                    break;
                case PanelContainer panel:
                    Override(panel, "panel", "PanelContainer");
                    break;
                case Panel plain:
                    Override(plain, "panel", "Panel");
                    break;
            }

            Apply(child);
        }
    }

    // A flat box is the only kind with a colour to fix; textured ones are left alone.
    static void Override(Control control, string style, string type)
    {
        if (Opaque(control.GetThemeStylebox(style, type)) is StyleBoxFlat solid)
            control.AddThemeStyleboxOverride(style, solid);
    }

    public static StyleBox Opaque(StyleBox box)
    {
        if (box is not StyleBoxFlat flat) return box;

        var copy = (StyleBoxFlat)flat.Duplicate();
        copy.BgColor = new Color(copy.BgColor, 1f);
        copy.BorderColor = new Color(copy.BorderColor, 1f);
        return copy;
    }
}
