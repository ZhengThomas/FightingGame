using Godot;

// Editor-only authoring helper. Add this as a child of a Player (or the player instance
// in hitbox_lab.tscn) and type X/Y/Width/Height in the Inspector to see the box drawn
// live on the character model — no need to run the game. Coordinates match gameplay:
// X/Y are offsets from the player's origin, in the same units used in Moveset.cs.
//
// It draws only in the editor. At runtime the real DebugDraw handles boxes, so this hides
// itself. The preview mesh is created without an owner, so it is never saved into the scene.
[Tool]
public partial class HitboxGizmo : Node3D
{
    public enum BoxKind { Hitbox, Hurtbox, Pushbox }

    private BoxKind kind = BoxKind.Hitbox;
    private float x = 0.6f;
    private float y = 0.5f;
    private float width = 0.6f;
    private float height = 0.3f;
    private float depth = 0.1f;
    private bool drawOnTop = true;

    [Export] public BoxKind Kind { get => kind; set { kind = value; UpdatePreview(); } }
    // Range hints render these as sliders in the Inspector. "or_greater/or_less" means you can
    // still type values beyond the slider range if you ever need to.
    [Export(PropertyHint.Range, "-3,3,0.01,or_greater,or_less")] public float X { get => x; set { x = value; UpdatePreview(); } }
    [Export(PropertyHint.Range, "-3,3,0.01,or_greater,or_less")] public float Y { get => y; set { y = value; UpdatePreview(); } }
    [Export(PropertyHint.Range, "0,5,0.01,or_greater")] public float Width { get => width; set { width = value; UpdatePreview(); } }
    [Export(PropertyHint.Range, "0,5,0.01,or_greater")] public float Height { get => height; set { height = value; UpdatePreview(); } }
    // Cosmetic thickness along Z (gameplay boxes are 2D); only affects how the preview looks.
    [Export] public float Depth { get => depth; set { depth = value; UpdatePreview(); } }
    // Draw over the model so the box is always visible while positioning it.
    [Export] public bool DrawOnTop { get => drawOnTop; set { drawOnTop = value; UpdatePreview(); } }

    // Toggle this checkbox to print copy-paste-ready code to the Output panel. It auto-resets.
    [Export] public bool PrintBoxCode { get => false; set { if (value) PrintBox(); } }

    private MeshInstance3D preview;

    public override void _Ready()
    {
        UpdatePreview();
    }

    public override void _Process(double delta)
    {
        // Only ever visible in the editor; DebugDraw owns runtime visuals.
        if (!Engine.IsEditorHint())
        {
            if (preview != null)
                preview.Visible = false;
            SetProcess(false);
        }
    }

    private const string PreviewName = "__HitboxPreview";

    private void EnsurePreview()
    {
        if (preview != null && IsInstanceValid(preview))
            return;

        // A tool-script reload (Build / reopen) nulls our field but leaves the old preview node
        // in the tree. Reuse the first leftover and delete any extras so we never stack boxes.
        foreach (Node child in GetChildren())
        {
            if (child is MeshInstance3D leftover && leftover.Name == PreviewName)
            {
                if (preview == null)
                    preview = leftover;
                else
                    leftover.QueueFree();
            }
        }

        if (preview != null && IsInstanceValid(preview))
            return;

        preview = new MeshInstance3D { Name = PreviewName };
        preview.Mesh = new BoxMesh();
        AddChild(preview);
        // No Owner set on purpose -> not serialized into the .tscn.
    }

    private void UpdatePreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        EnsurePreview();

        preview.Mesh = new BoxMesh
        {
            Size = new Vector3(
                Mathf.Max(0.001f, width),
                Mathf.Max(0.001f, height),
                Mathf.Max(0.001f, depth))
        };
        preview.Position = new Vector3(x, y, 0f);
        preview.MaterialOverride = MakeMaterial();
    }

    private StandardMaterial3D MakeMaterial()
    {
        Color c = kind switch
        {
            BoxKind.Hitbox  => new Color(1f, 0f, 0f), // red, matches DebugDraw
            BoxKind.Hurtbox => new Color(0f, 0f, 1f), // blue
            _               => new Color(0f, 1f, 0f), // green (pushbox)
        };
        c.A = 0.35f;

        return new StandardMaterial3D
        {
            AlbedoColor = c,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = drawOnTop,
        };
    }

    private void PrintBox()
    {
        GD.Print($"new Box {{ X = {x}f, Y = {y}f, Width = {width}f, Height = {height}f }}  // {kind}");
    }
}
