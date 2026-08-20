using Godot;

// Godot keeps "focused" and "hovered" apart, so a menu driven with both a mouse and a keyboard
// shows two different highlights at once. This makes hovering something focus it, leaving one.
//
// Hovering a part that can't be focused — a row's arrow buttons, a label — focuses the nearest
// thing above it that can, so the whole row lights up rather than nothing happening.
public static class FocusFollowsMouse
{
    public static void Apply(Node root) => Apply(root, null);

    static void Apply(Node node, Control focusable)
    {
        foreach (Node child in node.GetChildren())
        {
            Control target = focusable;

            if (child is Control control)
            {
                if (control.FocusMode != Control.FocusModeEnum.None) target = control;

                // Controls that ignore the mouse never emit this, so wiring them costs nothing.
                if (target != null)
                {
                    Control grab = target;
                    control.MouseEntered += () => grab.GrabFocus();
                }
            }

            Apply(child, target);
        }
    }
}
