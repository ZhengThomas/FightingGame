using Godot;

public partial class CameraController : Camera3D
{
    MatchManager match;
	const float BaseZ = 4f;         // default distance
    const float MaxZ = 4.5f;
    const float ZoomStartDistance = 3.9f;  // how far apart before zoom kicks in at all
    const float ZoomFullDistance = 4.8f;   // distance at which max zoom is reached
    const float BaseY = 0.8f;
    const float MaxY = 10f;
    const float TrackSpeed = 8f;
    const float MaxCameraDistanceFromCenter = 4.5f;
    const float TopMargin = 0.5f;  // headroom kept above the tallest hitbox top

    public override void _Ready()
    {
        match = GetNode<MatchManager>("/root/MatchManager");
        Position = new Vector3(0, BaseY, Position.Z);
    }

    public override void _Process(double delta)
    {
        if (match.Player1 == null || match.Player2 == null) return;

        Vector3 target = CalculateTargetPosition();
        Position = Position.Lerp(target, TrackSpeed * (float)delta);
    }

    private Vector3 CalculateTargetPosition()
    {
        float p1x = match.Player1.Position.X;
        float p2x = match.Player2.Position.X;
        float p1y = match.Player1.Position.Y;
        float p2y = match.Player2.Position.Y;

        float targetX = (p1x + p2x) / 2f;
        targetX = Mathf.Clamp(targetX, -MaxCameraDistanceFromCenter, MaxCameraDistanceFromCenter);

        // only start zooming out once players exceed ZoomStartDistance apart
        float distance = Mathf.Abs(p1x - p2x);
        float zoomT = Mathf.Clamp((distance - ZoomStartDistance) / (ZoomFullDistance - ZoomStartDistance), 0f, 1f);
        float targetZ = Mathf.Lerp(BaseZ, MaxZ, zoomT);

        // based on how much we zoomed out, nudge the camera up so the feet stay framed the same.
        float zoomBias = Mathf.Lerp(0, (MaxZ - BaseZ) / 2, zoomT);
        float baselineY = BaseY + zoomBias;

        // Drive upward movement from the top of the tallest player hitbox (not their origin),
        // so the top of a hitbox never rises above the visible area. For a straight-looking
        // perspective camera, the visible top world-Y is camY + tan(vFov/2) * distance, with the
        // players sitting near z = 0 and the camera at z = targetZ. Solve for the minimum camY.
        float topY = Mathf.Max(GetPlayerTopY(match.Player1), GetPlayerTopY(match.Player2));
        float halfVisibleHeight = Mathf.Tan(Mathf.DegToRad(Fov) * 0.5f) * targetZ;
        float minCamYToKeepTopVisible = topY - halfVisibleHeight + TopMargin;

        float targetY = Mathf.Clamp(Mathf.Max(baselineY, minCamYToKeepTopVisible), baselineY, MaxY);

        return new Vector3(targetX, targetY, targetZ);
    }

    // World-space Y of the top of a player's tallest box (pushbox or any current hurtbox).
    private float GetPlayerTopY(Player p)
    {
        float top = p.Position.Y + p.Pushbox.Y + p.Pushbox.Height / 2f;

        var hurtboxes = p.GetCurrentHurtboxes();
        if (hurtboxes != null)
        {
            foreach (Box b in hurtboxes)
            {
                float t = p.Position.Y + b.Y + b.Height / 2f;
                if (t > top)
                    top = t;
            }
        }
        return top;
    }
}