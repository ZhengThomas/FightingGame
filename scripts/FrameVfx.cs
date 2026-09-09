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
    // Drawings actually used in the sheet, when the grid has spare cells at the end. 0 = the whole
    // grid. A 4x3 sheet holding 10 frames wants 10, or it ends on two blank cells.
    [Export] public int SheetFrameCount = 0;

    [Export] public float MaxRotationDegrees = 0f;
    // Per-spawn spin picked from this range, so repeated hits don't look stamped. Applied to this
    // node's transform, so setting it on a root turns the whole effect. 0/0 = no variation.
    [Export] public float RandRotMin = 0f;
    [Export] public float RandRotMax = 0f;
    [Export] public bool FlipWithFacing = false;
    // Set on an effect's root to track the player that requested it instead of staying put.
    [Export] public bool FollowSpawner = false;
    // Set on an effect's root to keep playing through hit pause. Effects run off SimFrame, which
    // stalls while the fighters are frozen — right for dust and trails, wrong for an impact, whose
    // whole job is to animate during the freeze. Read from the root only.
    [Export] public bool IgnoreHitPause = false;

    // A drawable and how it looked before any curve touched it.
    class Visual
    {
        public GeometryInstance3D Node;
        public SpriteBase3D Sprite;   // null for meshes
        public float BaseAlpha = 1f;
        public int SheetFrames;
        public bool BaseFlipH;
        public bool Scissor;          // Alpha Cut = Discard: fade erodes pixels instead of dimming
        public ShaderMaterial Fade;   // vfx_sprite.gdshader: fade subtracts per pixel, not scales
    }

    Visual[] owned;
    FrameVfx[] parts;
    // Transform as authored in the scene, read once before any curve overwrites it. The curves
    // compose with these rather than replacing them.
    Vector3 baseScale = Vector3.One;
    Vector3 baseRotation = Vector3.Zero;
    Vector3 basePosition = Vector3.Zero;
    // False on an effect's root, whose position VfxManager owns — writing it here would fight that.
    bool isPart;
    // This spawn's pick from the random range, in radians.
    float spawnSpin;

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

    // Called once per spawn with the effect's dedup id. Deriving the spin from that id instead of a
    // random number keeps it identical on both machines and unchanged when a rollback replays the
    // same event, with no state to store.
    public void SetSpawnSeed(int seed)
    {
        EnsureCollected();

        spawnSpin = RandRotMax != RandRotMin
            ? Mathf.DegToRad(Mathf.Lerp(RandRotMin, RandRotMax, Hash01(seed)))
            : Mathf.DegToRad(RandRotMin);

        // Decorrelated per part, so parts of one effect don't all land on the same angle.
        for (int i = 0; i < parts.Length; i++)
            parts[i].SetSpawnSeed(seed * 31 + i + 1);
    }

    // Deterministic 0..1 from an int, so the same event always picks the same angle.
    static float Hash01(int seed)
    {
        uint h = (uint)seed * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFFu) / (float)0x1000000;
    }

    // frame = ticks since the whole effect started; parts shift it by their own offset.
    // inheritFlip carries an ancestor's mirror down, so FlipWithFacing on a root mirrors the whole
    // effect and a part can still opt in on its own.
    public void SetFrame(int frame, int facing = 1, bool inheritFlip = false)
    {
        EnsureCollected();

        int local = frame - StartOffsetFrames;
        bool showing = local >= 0 && local < DurationFrames;
        bool flip = inheritFlip || (FlipWithFacing && facing < 0);

        foreach (Visual v in owned)
            v.Node.Visible = showing;

        if (showing)
            Apply((float)local / DurationFrames, flip);

        // Parts run on the effect's clock, so they can still be playing after this part's window.
        foreach (FrameVfx p in parts)
            p.SetFrame(frame, facing, flip);
    }

    void Apply(float progress, bool flip)
    {
        // Only written when this part animates it, so an unanimated authored scale survives.
        if (ScaleCurve != null)
            Scale = baseScale * Sample(ScaleCurve, progress, 1f);

        // Mirrored about the effect's own origin, which VfxManager leaves unrotated on the player,
        // so a part offset to one side swings to the other however the part itself is turned.
        if (isPart)
            Position = flip ? new Vector3(-basePosition.X, basePosition.Y, basePosition.Z) : basePosition;

        float spin = MaxRotationDegrees != 0f
            ? Mathf.DegToRad(MaxRotationDegrees * Sample(RotationCurve, progress, 0f))
            : 0f;

        // A mirrored shape spins the other way, so the whole Z angle is negated, authored included.
        // Written every frame, not just while flipping: instances are pooled, so a copy left
        // negated by an earlier flipped play would keep that rotation on its next unflipped one.
        float z = baseRotation.Z + spin + spawnSpin;
        Rotation = new Vector3(baseRotation.X, baseRotation.Y, flip ? -z : z);

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

            if (v.Sprite != null)
                v.Sprite.FlipH = v.BaseFlipH ^ flip;

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
        baseRotation = Rotation;
        basePosition = Position;
        isPart = GetParent() is FrameVfx;

        var geometry = new List<Visual>();
        var nested = new List<FrameVfx>();
        foreach (Node child in GetChildren())
            Walk(child, geometry, nested);

        owned = geometry.ToArray();
        parts = nested.ToArray();

        if (SheetFrameCount > 0)
            foreach (Visual v in owned)
                if (v.SheetFrames > 0)
                    v.SheetFrames = Mathf.Min(v.SheetFrames, SheetFrameCount);
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
        v.BaseFlipH = v.Sprite.FlipH;
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
