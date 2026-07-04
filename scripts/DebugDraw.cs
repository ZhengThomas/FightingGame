using Godot;
using System.Collections.Generic;

public partial class DebugDraw : Node2D
{
    struct DrawCall
    {
        public Rect2 Rect;
        public Color Color;
    }

    List<DrawCall> drawCalls = new List<DrawCall>();

    // Caches the PlayerAnimator per Player so we can read its hybrid-projection params
    // without searching the tree every box, every frame.
    Dictionary<Player, PlayerAnimator> animatorCache = new Dictionary<Player, PlayerAnimator>();

    public void DrawWorldBox(Player player, Box box, Color color, float facingMult = 1f)
    {
        var camera = GetViewport().GetCamera3D();

        Vector3 worldCenter = new Vector3(
            player.Position.X + box.X * facingMult,
            player.Position.Y + box.Y,
            player.Position.Z
        );

        Vector3 topLeft3D     = worldCenter + new Vector3(-box.Width / 2,  box.Height / 2, 0);
        Vector3 bottomRight3D = worldCenter + new Vector3( box.Width / 2, -box.Height / 2, 0);

        Vector2 topLeft     = ProjectHybrid(camera, topLeft3D, player);
        Vector2 bottomRight = ProjectHybrid(camera, bottomRight3D, player);

        drawCalls.Add(new DrawCall { 
            Rect = new Rect2(topLeft, bottomRight - topLeft), 
            Color = color 
        });

        QueueRedraw();
    }

    // Projects a world point to screen, matching the character's hybrid (horizontal-orthographic)
    // shader so debug boxes ride along with the visually-shifted mesh instead of the raw
    // perspective position. If the player has no active hybrid projection this is a plain unproject.
    Vector2 ProjectHybrid(Camera3D camera, Vector3 world, Player player)
    {
        Vector2 persp = camera.UnprojectPosition(world);

        PlayerAnimator anim = GetAnimator(player);
        if (anim == null || !anim.UseHybridProjection || anim.OrthoAmount <= 0f)
            return persp;

        float t = anim.OrthoAmount;
        float planeDistance = anim.PlaneDistance;

        // Depth = distance in front of the camera along its forward (-Z in camera space).
        Vector3 local = camera.GlobalTransform.AffineInverse() * world;
        float depth = -local.Z;
        if (depth <= 0.0001f || planeDistance <= 0.0001f)
            return persp; // behind camera / degenerate: nothing sensible to shift

        // The shader blends perspective NDC-x with an orthographic NDC-x that swaps the point's
        // real depth for a fixed plane_distance. In screen space that's a scale toward center:
        //   final_x = center + (persp_x - center) * ((1 - t) + t * depth / planeDistance)
        float cx = GetViewport().GetVisibleRect().Size.X * 0.5f;
        float factor = (1f - t) + t * (depth / planeDistance);
        float x = cx + (persp.X - cx) * factor;
        return new Vector2(x, persp.Y);
    }

    PlayerAnimator GetAnimator(Player player)
    {
        if (player == null)
            return null;
        if (animatorCache.TryGetValue(player, out PlayerAnimator cached) && IsInstanceValid(cached))
            return cached;

        PlayerAnimator found = FindAnimator(player);
        animatorCache[player] = found;
        return found;
    }

    static PlayerAnimator FindAnimator(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is PlayerAnimator anim)
                return anim;
            PlayerAnimator nested = FindAnimator(child);
            if (nested != null)
                return nested;
        }
        return null;
    }

    public override void _Draw()
    {
        foreach (var call in drawCalls)
            DrawRect(call.Rect, call.Color, false, 6f);
        
        drawCalls.Clear(); // clear AFTER drawing
    }
}
