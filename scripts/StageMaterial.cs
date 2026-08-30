using Godot;

// Blockout helper: puts one material on every mesh underneath, however many there are and whatever
// they're called.
//
// Per-node material overrides don't survive re-importing a model — the exporter renames and
// re-splits meshes, and the overrides are pinned to the old names. This reapplies on load instead,
// so exporting from Blender costs nothing on this side. Clear Override to hand every mesh its own
// material back.
[Tool]
public partial class StageMaterial : Node3D
{
    [Export]
    public Material Override
    {
        get => material;
        set { material = value; Apply(this); }
    }

    Material material;

    public override void _Ready() => Apply(this);

    void Apply(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh) mesh.MaterialOverride = material;
            Apply(child);
        }
    }
}
