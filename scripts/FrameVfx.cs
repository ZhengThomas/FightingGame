using Godot;

// How a one-shot visual looks at a given point in its life. Purely presentational: it holds no
// timer, never advances itself, and knows nothing about when or why it was started.
//
// VfxManager owns the "when" — it pools these and each frame hands every live one a 0..1 progress
// derived from MatchManager.SimFrame. Because SimFrame stalls during hit pause, effects freeze with
// the game for free; because none of this is in a snapshot, there is nothing for a rollback to
// rewind.
//
// Shape an effect by drawing ScaleCurve / AlphaCurve in the inspector. A new effect is a new scene
// with different curves — no code at all.
[Tool]
public partial class FrameVfx : Node3D
{
    // Ticks from spawn to finished. PlayerVfx divides its counter by this to get progress.
    [Export] public int DurationFrames = 15;

    // Progress (0..1) -> multiplier on BaseScale. Left flat/empty means constant BaseScale.
    [Export] public Curve ScaleCurve;
    // Progress (0..1) -> opacity (0..1). Left empty means fully opaque the whole way.
    [Export] public Curve AlphaCurve;
    // Progress (0..1) -> degrees of spin about Z, times MaxRotationDegrees.
    [Export] public Curve RotationCurve;

    [Export] public float BaseScale = 1.0f;
    [Export] public float MaxRotationDegrees = 0f;
    // Scales the whole effect on X to face the way the character does. Leave off for symmetric art.
    [Export] public bool FlipWithFacing = false;

    Node3D visual;
    GeometryInstance3D[] tinted;

    public override void _Ready()
    {
        visual = this;
        tinted = CollectGeometry();
        Hide();
    }

    // progress: 0 on the spawn frame, 1 when finished. Anything outside 0..1 hides the effect.
    public void SetProgress(float progress, int facing = 1)
    {
        tinted ??= CollectGeometry();

        if (progress < 0f || progress > 1f)
        {
            if (Visible) Hide();
            return;
        }

        if (!Visible) Show();

        float scale = BaseScale * Sample(ScaleCurve, progress, 1f);
        float flip = (FlipWithFacing && facing < 0) ? -1f : 1f;
        Scale = new Vector3(scale * flip, scale, scale);

        if (MaxRotationDegrees != 0f)
            Rotation = new Vector3(0f, 0f, Mathf.DegToRad(MaxRotationDegrees * Sample(RotationCurve, progress, 0f)));

        SetAlpha(Sample(AlphaCurve, progress, 1f));
    }

    static float Sample(Curve curve, float progress, float fallback)
        => curve != null ? curve.Sample(progress) : fallback;

    // Sprites carry their own Modulate, which is the reliable way to fade them. Meshes have no
    // modulate, so those go through GeometryInstance3D.Transparency instead. Either way the
    // material is left alone, so a shared material can't be fought over by two players' effects.
    void SetAlpha(float alpha)
    {
        alpha = Mathf.Clamp(alpha, 0f, 1f);
        foreach (GeometryInstance3D g in tinted)
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

    GeometryInstance3D[] CollectGeometry()
    {
        var found = new Godot.Collections.Array<GeometryInstance3D>();
        Walk(this, found);
        var result = new GeometryInstance3D[found.Count];
        for (int i = 0; i < found.Count; i++) result[i] = found[i];
        return result;
    }

    static void Walk(Node node, Godot.Collections.Array<GeometryInstance3D> into)
    {
        if (node is GeometryInstance3D g) into.Add(g);
        foreach (Node child in node.GetChildren())
            Walk(child, into);
    }
}
