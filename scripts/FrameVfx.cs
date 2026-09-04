using Godot;
using System.Collections.Generic;

// How a one-shot visual looks at a given tick of its life. Holds no timer and never advances itself
// — VfxManager hands it a tick count each frame. Nothing here is snapshotted, so a rollback has
// nothing to rewind.
//
// These nest: a FrameVfx with FrameVfx children passes the tick count down unchanged, and each part
// applies its own StartOffsetFrames and DurationFrames. That staggers parts of one effect. A part
// owns the geometry below it up to the next nested FrameVfx.
[Tool]
public partial class FrameVfx : Node3D
{
    [Export] public int StartOffsetFrames = 0;
    [Export] public int DurationFrames = 15;

    // All sampled with progress 0..1. Empty means "leave it alone".
    [Export] public Curve ScaleCurve;
    [Export] public Curve AlphaCurve;
    [Export] public Curve RotationCurve;
    // Position through a sprite sheet. Needs Hframes/Vframes, or an AnimatedSprite3D.
    [Export] public Curve SpriteFrameCurve;

    [Export] public float MaxRotationDegrees = 0f;
    [Export] public bool FlipWithFacing = false;
    // Set on an effect's root to track the player that requested it instead of staying put.
    [Export] public bool FollowSpawner = false;

    // A drawable and how it looked before any curve touched it.
    class Visual
    {
        public GeometryInstance3D Node;
        public SpriteBase3D Sprite;   // null for meshes
        public float BaseAlpha = 1f;
        public int SheetFrames;
        public bool Scissor;          // Alpha Cut = Discard: fade erodes pixels instead of dimming
        public ShaderMaterial Fade;   // vfx_sprite.gdshader: fade subtracts per pixel, not scales
    }

    Visual[] owned;
    FrameVfx[] parts;
    // Scale as authored in the scene, read once before any curve overwrites it. ScaleCurve
    // multiplies this, so a non-uniform authored scale keeps its proportions.
    Vector3 baseScale = Vector3.One;

    // Ticks until the last part finishes, so a long trailing part isn't cut off by a short root.
    public int TotalDurationFrames
    {
        get
        {
            EnsureCollected();
            int total = StartOffsetFrames + DurationFrames;
            foreach (FrameVfx p in parts)
                total = Mathf.Max(total, StartOffsetFrames + p.TotalDurationFrames);
            return total;
        }
    }

    public override void _Ready()
    {
        EnsureCollected();
        SetFrame(-1);
    }

    // frame = ticks since the whole effect started; parts shift it by their own offset.
    public void SetFrame(int frame, int facing = 1)
    {
        EnsureCollected();

        int local = frame - StartOffsetFrames;
        bool showing = local >= 0 && local < DurationFrames;

        foreach (Visual v in owned)
            v.Node.Visible = showing;

        if (showing)
            Apply((float)local / DurationFrames, facing);

        // Parts run on the effect's clock, so they can still be playing after this part's window.
        foreach (FrameVfx p in parts)
            p.SetFrame(frame, facing);
    }

    void Apply(float progress, int facing)
    {
        // Only written when this part animates it, so an unanimated authored scale survives.
        if (ScaleCurve != null || FlipWithFacing)
        {
            Vector3 s = baseScale * Sample(ScaleCurve, progress, 1f);
            if (FlipWithFacing && facing < 0) s.X = -s.X;
            Scale = s;
        }

        if (MaxRotationDegrees != 0f)
            Rotation = new Vector3(0f, 0f, Mathf.DegToRad(MaxRotationDegrees * Sample(RotationCurve, progress, 0f)));

        float alpha = Mathf.Clamp(Sample(AlphaCurve, progress, 1f), 0f, 1f);
        float sheet = Sample(SpriteFrameCurve, progress, progress);

        foreach (Visual v in owned)
        {
            // Curve subtracts from the authored opacity rather than scaling it, so dim parts drop
            // out before opaque ones.
            float a = Mathf.Clamp(v.BaseAlpha + alpha - 1f, 0f, 1f);

            if (v.Fade != null)
                v.Fade.SetShaderParameter("fade", a);
            else if (v.Sprite == null)
                v.Node.Transparency = 1f - a;
            else if (v.Scissor)
            {
                // Threshold 1 still keeps fully-opaque pixels, so hide once nothing should remain.
                v.Sprite.AlphaScissorThreshold = 1f - a;
                v.Node.Visible = a > 0f;
            }
            else
            {
                Color c = v.Sprite.Modulate;
                c.A = a;
                v.Sprite.Modulate = c;
            }

            if (v.SheetFrames > 1)
            {
                int i = Mathf.Clamp((int)(sheet * v.SheetFrames), 0, v.SheetFrames - 1);
                if (v.Sprite is AnimatedSprite3D anim) anim.Frame = i;
                else if (v.Sprite is Sprite3D spr) spr.Frame = i;
            }
        }
    }

    static float Sample(Curve curve, float progress, float fallback)
        => curve != null ? curve.Sample(progress) : fallback;

    void EnsureCollected()
    {
        if (owned != null) return;

        baseScale = Scale;

        var geometry = new List<Visual>();
        var nested = new List<FrameVfx>();
        foreach (Node child in GetChildren())
            Walk(child, geometry, nested);

        owned = geometry.ToArray();
        parts = nested.ToArray();
    }

    // Stops at a nested FrameVfx, which claims everything below itself instead.
    static void Walk(Node node, List<Visual> geometry, List<FrameVfx> nested)
    {
        if (node is FrameVfx part) { nested.Add(part); return; }

        if (node is GeometryInstance3D g) geometry.Add(Describe(g));

        foreach (Node child in node.GetChildren())
            Walk(child, geometry, nested);
    }

    static Visual Describe(GeometryInstance3D g)
    {
        var v = new Visual { Node = g, Sprite = g as SpriteBase3D };

        // Duplicated per sprite so two live copies of an effect don't share one fade value. The
        // shader itself is still shared; only the parameters are per-instance.
        if (g.MaterialOverride is ShaderMaterial shared)
        {
            v.Fade = (ShaderMaterial)shared.Duplicate();
            g.MaterialOverride = v.Fade;

            // Bind the sprite's own texture, so it's set in one place rather than twice.
            if (v.Sprite is Sprite3D src && src.Texture != null)
                v.Fade.SetShaderParameter("tex", src.Texture);
        }

        if (v.Sprite == null)
        {
            v.BaseAlpha = 1f - g.Transparency;
            return v;
        }

        v.BaseAlpha = v.Sprite.Modulate.A;
        v.Scissor = v.Sprite.AlphaCut == SpriteBase3D.AlphaCutMode.Discard;
        v.SheetFrames = v.Sprite switch
        {
            AnimatedSprite3D a => a.SpriteFrames?.GetFrameCount(a.Animation) ?? 0,
            Sprite3D s => s.Hframes * s.Vframes,
            _ => 0,
        };
        return v;
    }
}
