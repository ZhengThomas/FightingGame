using Godot;

// Polls both players' Health each frame and drives the yellow fills + red trails.
// Yellow snaps to the current value. The red trail stays frozen during a combo
// (any hit resets a settle counter) and only starts closing on yellow once
// TrailDelayFrames sim frames have passed with no damage.
//
// All timing is in sim frames, not real time — hitpause freezes the counter so
// the trail doesn't drift while the world is paused.
//
// Editor setup for the 4 fill sprites:
//  - Centered = false
//  - Region Enabled = true (safety-net'd on in _Ready)
//  - Offset set to the padding inside the bg (e.g. (10, 4)).
public partial class HealthBarHud : Node3D
{
    [Export] public Sprite3D P1Fill;
    [Export] public Sprite3D P1Trail;
    [Export] public Sprite3D P2Fill;
    [Export] public Sprite3D P2Trail;
    [Export] public Player P1;
    [Export] public Player P2;
    [Export] public MatchManager Match;

    // Sim frames after the last hit before the red trail starts catching up.
    [Export] public int TrailDelayFrames = 45;
    // Health-percent the trail closes on yellow per sim frame, once the delay expires.
    [Export] public float TrailFollowPerFrame = 0.01f;

    float p1TrailPct = 1f, p2TrailPct = 1f;
    // Sim frames since last damage. Starts large so match-start doesn't hold the trail.
    int p1SettleFrames = int.MaxValue;
    int p2SettleFrames = int.MaxValue;
    float p1LastPct = 1f, p2LastPct = 1f;
    int lastSimFrame;
    Vector2 p1FillOffsetHome, p1TrailOffsetHome, p2FillOffsetHome, p2TrailOffsetHome;

    public override void _Ready()
    {
        P1Fill.RegionEnabled = true;
        P1Trail.RegionEnabled = true;
        P2Fill.RegionEnabled = true;
        P2Trail.RegionEnabled = true;

        p1FillOffsetHome  = P1Fill.Offset;
        p1TrailOffsetHome = P1Trail.Offset;
        p2FillOffsetHome  = P2Fill.Offset;
        p2TrailOffsetHome = P2Trail.Offset;

        lastSimFrame = Match.SimFrame;
    }

    public override void _Process(double delta)
    {
        int simDelta = Match.SimFrame - lastSimFrame;
        lastSimFrame = Match.SimFrame;

        float p1Pct = (float)P1.Health / Player.MaxHealth;
        float p2Pct = (float)P2.Health / Player.MaxHealth;

        p1TrailPct = UpdateTrail(p1Pct, p1TrailPct, ref p1SettleFrames, ref p1LastPct, simDelta);
        p2TrailPct = UpdateTrail(p2Pct, p2TrailPct, ref p2SettleFrames, ref p2LastPct, simDelta);

        SetRegion(P1Fill,  p1FillOffsetHome,  p1Pct,      shrinkFromRight: false);
        SetRegion(P1Trail, p1TrailOffsetHome, p1TrailPct, shrinkFromRight: false);
        SetRegion(P2Fill,  p2FillOffsetHome,  p2Pct,      shrinkFromRight: true);
        SetRegion(P2Trail, p2TrailOffsetHome, p2TrailPct, shrinkFromRight: true);
    }

    // Freeze the trail while a combo is in progress (any yellow drop resets settleFrames).
    // Once no damage has been taken for TrailDelayFrames sim frames, the trail eases down.
    float UpdateTrail(float yellowPct, float trailPct, ref int settleFrames, ref float lastPct, int simDelta)
    {
        if (yellowPct < lastPct) settleFrames = 0;
        else settleFrames = SafeAdd(settleFrames, simDelta);
        lastPct = yellowPct;

        // Heal / round reset — snap up so red doesn't awkwardly lag above yellow.
        if (trailPct < yellowPct) return yellowPct;

        if (settleFrames < TrailDelayFrames) return trailPct;

        return Mathf.Max(yellowPct, trailPct - TrailFollowPerFrame * simDelta);
    }

    static int SafeAdd(int a, int b) => (a > int.MaxValue - b) ? int.MaxValue : a + b;

    static void SetRegion(Sprite3D fill, Vector2 offsetHome, float pct, bool shrinkFromRight)
    {
        pct = Mathf.Clamp(pct, 0f, 1f);
        fill.Visible = pct > 0f;
        if (pct <= 0f) return;

        float texW = fill.Texture.GetWidth();
        float texH = fill.Texture.GetHeight();
        float visibleW = texW * pct;
        fill.RegionRect = new Rect2(texW - visibleW, 0f, visibleW, texH);

        if (!shrinkFromRight)
        {
            fill.Offset = new Vector2(offsetHome.X + texW * (1f - pct), offsetHome.Y);
        }
    }
}
