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
		
		// and based on how much we zoomed out, we also move the camera upwards by a similar amount
		// to keep the feet at the same place visually
        float highestY = Mathf.Max(p1y, p2y) + BaseY - 1.5f;
		float zoomBias = Mathf.Lerp(0, (MaxZ - BaseZ) / 2, zoomT);
        float targetY = Mathf.Clamp(highestY + zoomBias, BaseY + zoomBias, MaxY);

        return new Vector3(targetX, targetY, targetZ);
    }
}