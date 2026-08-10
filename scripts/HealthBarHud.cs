using Godot;

// Drives the yellow health fills and the red damage trails from both players' Health.
// Yellow snaps to the current value. Red holds while a combo is running — any drop restarts a
// settle counter — and only closes on yellow once TrailDelayFrames sim frames pass without damage.
//
// Timing is in sim frames, so hit pause freezes the trail along with everything else.
//
// Editor setup for the 4 fill sprites: Centered = false, Region Enabled = true (forced on in
// _Ready), Offset set to the padding inside the background (e.g. (10, 4)).
public partial class HealthBarHud : Node3D
{
    [Export] public Sprite3D P1Fill;
    [Export] public Sprite3D P1Trail;
    [Export] public Sprite3D P2Fill;
    [Export] public Sprite3D P2Trail;
    [Export] public Player P1;
    [Export] public Player P2;

    // Sim frames after the last hit before the red trail starts catching up.
    [Export] public int TrailDelayFrames = 45;
    // Health-percent the trail closes on yellow per sim frame, once the delay expires.
    [Export] public float TrailFollowPerFrame = 0.01f;

    // One side's sprites plus its trail animation state. Purely visual — all of it is derived
    // from Player.Health, so none of it is snapshotted.
    class Bar
    {
        public Sprite3D Fill, Trail;
        public Player Player;
        public bool ShrinkFromRight;
        public Vector2 FillHome, TrailHome;
        public float TrailPct = 1f;
        public float LastPct = 1f;
        public int SettleFrames;
    }

    MatchManager match;
    Bar[] bars;
    int lastSimFrame;

    public override void _Ready()
    {
        match = MatchManager.Current;
        bars = new[]
        {
            MakeBar(P1Fill, P1Trail, P1, shrinkFromRight: false),
            MakeBar(P2Fill, P2Trail, P2, shrinkFromRight: true),
        };
        lastSimFrame = match.SimFrame;
    }

    Bar MakeBar(Sprite3D fill, Sprite3D trail, Player player, bool shrinkFromRight)
    {
        fill.RegionEnabled = true;
        trail.RegionEnabled = true;
        return new Bar
        {
            Fill = fill,
            Trail = trail,
            Player = player,
            ShrinkFromRight = shrinkFromRight,
            FillHome = fill.Offset,
            TrailHome = trail.Offset,
            SettleFrames = TrailDelayFrames, // start settled so match start doesn't hold the trail
        };
    }

    public override void _Process(double delta)
    {
        // Clamped at zero: a rollback rewinds SimFrame, and a negative delta would walk the settle
        // counter down and grow the trail back.
        int simDelta = Mathf.Max(0, match.SimFrame - lastSimFrame);
        lastSimFrame = match.SimFrame;

        foreach (Bar bar in bars)
            UpdateBar(bar, simDelta);
    }

    void UpdateBar(Bar bar, int simDelta)
    {
        float pct = (float)bar.Player.Health / Player.MaxHealth;

        // Capped at the threshold so it can't run away over a long match.
        bar.SettleFrames = pct < bar.LastPct
            ? 0
            : Mathf.Min(bar.SettleFrames + simDelta, TrailDelayFrames);
        bar.LastPct = pct;

        if (bar.TrailPct < pct)
            bar.TrailPct = pct; // heal / round reset — snap up so red isn't stranded below yellow
        else if (bar.SettleFrames >= TrailDelayFrames)
            bar.TrailPct = Mathf.Max(pct, bar.TrailPct - TrailFollowPerFrame * simDelta);

        SetRegion(bar.Fill, bar.FillHome, pct, bar.ShrinkFromRight);
        SetRegion(bar.Trail, bar.TrailHome, bar.TrailPct, bar.ShrinkFromRight);
    }

    static void SetRegion(Sprite3D sprite, Vector2 offsetHome, float pct, bool shrinkFromRight)
    {
        pct = Mathf.Clamp(pct, 0f, 1f);
        sprite.Visible = pct > 0f;
        if (pct <= 0f) return;

        float texW = sprite.Texture.GetWidth();
        float texH = sprite.Texture.GetHeight();
        float visibleW = texW * pct;
        sprite.RegionRect = new Rect2(texW - visibleW, 0f, visibleW, texH);

        if (!shrinkFromRight)
            sprite.Offset = new Vector2(offsetHome.X + texW * (1f - pct), offsetHome.Y);
    }
}
