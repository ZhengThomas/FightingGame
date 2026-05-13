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

        Vector2 topLeft     = camera.UnprojectPosition(topLeft3D);
        Vector2 bottomRight = camera.UnprojectPosition(bottomRight3D);

        drawCalls.Add(new DrawCall { 
            Rect = new Rect2(topLeft, bottomRight - topLeft), 
            Color = color 
        });

        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var call in drawCalls)
            DrawRect(call.Rect, call.Color, false, 6f);
        
        drawCalls.Clear(); // clear AFTER drawing
    }
}