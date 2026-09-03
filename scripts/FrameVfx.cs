using Godot;
using System.Collections.Generic;

// How a one-shot visual looks at a given point in its life. Purely presentational: it holds no
// timer, never advances itself, and knows nothing about when or why it was started.
//
// VfxManager owns the "when" — it pools these and each frame hands every live one the number of
// ticks since it started. Because that count comes from MatchManager.SimFrame, which stalls during
// hit pause, effects freeze with the game for free; because none of it is in a snapshot, there is
// nothing for a rollback to rewind.
//
// These nest. A FrameVfx with FrameVfx children is a compound effect: the raw tick count is passed
// down unchanged, and every part works out its own progress from its own StartOffsetFrames and
// DurationFrames. That's how three rings stagger two frames apart, or smoke outlasts the flash.
// A part owns the geometry beneath it up to the next nested FrameVfx, so parts never fight over
// each other's sprites.
//
// Shape a part by drawing its curves in the inspector. A new effect is a new scene — no code.
[Tool]
public partial class FrameVfx : Node3D
{
    // Ticks this part waits after the effect starts before it appears. 0 = starts immediately.
    [Export] public int StartOffsetFrames = 0;
    // Ticks this part takes to run, once it has started.
    [Export] public int DurationFrames = 15;

    // Progress (0..1) -> multiplier on BaseScale. Empty leaves the authored scale alone.
    [Export] public Curve ScaleCurve;
    // Progress (0..1) -> opacity (0..1). Empty means fully opaque the whole way.
    [Export] public Curve AlphaCurve;
    // Progress (0..1) -> degrees of spin about Z, times MaxRotationDegrees.
    [Export] public Curve RotationCurve;

    [Export] public float BaseScale = 1.0f;
    [Export] public float MaxRotationDegrees = 0f;
    // Mirrors this part on X to face the way the character does. Leave off for symmetric art.
    [Export] public bool FlipWithFacing = false;

    // Set on an effect's ROOT to make it track the player that requested it, instead of staying
    // where it spawned. Reading the player's live position each frame means there is nothing to
    // store and nothing for a rollback to rewind — a resim just corrects the player and the effect
    // follows. Facing is still locked at spawn, so a directional effect can't flip mid-play.
    [Export] public bool FollowSpawner = false;

    // Geometry belonging to this part only — the walk stops at a nested FrameVfx.
    GeometryInstance3D[] owned;
    // Nested parts, handed the same raw tick count as this node.
    FrameVfx[] parts;

    // Ticks from the effect starting to its last part finishing. VfxManager uses this to know when
    // the whole thing is done, so a long trailing part can't be cut off by a short root.
    public int TotalDurationFrames
    {
        get
        {
            EnsureCollected();
            int total = StartOffsetFrames + DurationFrames;
            foreach (FrameVfx part in parts)
                total = Mathf.Max(total, StartOffsetFrames + part.TotalDurationFrames);
            return total;
        }
    }

    public override void _Ready()
    {
        EnsureCollected();
        SetFrame(-1);   // start hidden
    }

    // frame = ticks since the whole effect started. Negative, or past this part's window, hides it.
    // Pass the same number to every part; each shifts it by its own offset.
    public void SetFrame(int frame, int facing = 1)
    {
        EnsureCollected();

        int local = frame - StartOffsetFrames;
        bool showing = local >= 0 && local < DurationFrames && DurationFrames > 0;

        if (showing)
            Apply((float)local / DurationFrames, facing);

        foreach (GeometryInstance3D g in owned)
            g.Visible = showing;

        // Parts run on the effect's clock, not this part's, so they get the raw count. A part can
        // therefore still be playing after its parent's own window has closed.
        foreach (FrameVfx part in parts)
            part.SetFrame(frame, facing);
    }

    void Apply(float progress, int facing)
    {
        // Only touch the transform when this part actually animates it, so a container's authored
        // scale/rotation survives being handed a progress.
        if (ScaleCurve != null || BaseScale != 1.0f || FlipWithFacing)
        {
            float scale = BaseScale * Sample(ScaleCurve, progress, 1f);
            float flip = (FlipWithFacing && facing < 0) ? -1f : 1f;
            Scale = new Vector3(scale * flip, scale, scale);
        }

        if (MaxRotationDegrees != 0f)
            Rotation = new Vector3(0f, 0f, Mathf.DegToRad(MaxRotationDegrees * Sample(RotationCurve, progress, 0f)));

        SetAlpha(Sample(AlphaCurve, progress, 1f));
    }

    static float Sample(Curve curve, float progress, float fallback)
        => curve != null ? curve.Sample(progress) : fallback;

    // Sprites carry their own Modulate, which is the reliable way to fade them. Meshes have no
    // modulate, so those go through GeometryInstance3D.Transparency instead. Either way the
    // material is left alone, so a shared material can't be fought over by two live effects.
    void SetAlpha(float alpha)
    {
        alpha = Mathf.Clamp(alpha, 0f, 1f);
        foreach (GeometryInstance3D g in owned)
        {
            if (g is SpriteBase3D sprite)
            {
                Color c = sprite.Modulate;
                c.A = alpha;
                sprite.Modulate = c;
            }
            else
            {
                g.Transparency = 1f - alpha;
            }
        }
    }

    void EnsureCollected()
    {
        if (owned != null) return;

        var geometry = new List<GeometryInstance3D>();
        var nested = new List<FrameVfx>();
        foreach (Node child in GetChildren())
            Walk(child, geometry, nested);

        owned = geometry.ToArray();
        parts = nested.ToArray();
    }

    // Descends until it hits a nested FrameVfx, which claims everything below itself instead.
    static void Walk(Node node, List<GeometryInstance3D> geometry, List<FrameVfx> nested)
    {
        if (node is FrameVfx part)
        {
            nested.Add(part);
            return;
        }

        if (node is GeometryInstance3D g) geometry.Add(g);

        foreach (Node child in node.GetChildren())
            Walk(child, geometry, nested);
    }
}
