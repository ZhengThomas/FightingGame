using Godot;

// Editor-only helper for hitbox_lab.tscn. Poses the character model at a chosen animation
// and time so you can line up hitboxes against the real attack pose (e.g. the fully extended
// punch) instead of the bind/T-pose. Add as a child of the Player instance.
//
// It finds the AnimationPlayer inside the player model automatically. It only acts in the
// editor; at runtime PlayerAnimator drives animation and this does nothing.
[Tool]
public partial class EditorPose : Node3D
{
    private string animation = "";
    private int frame = 0;

    // The instanced slash VFX shown in the lab, and which scene it is, so we only rebuild on change.
    private Node3D slashPreview;
    private string slashPreviewPath = "";

    private string vfxScene = "";
    private int vfxFrame = 0;
    // The instanced state VFX (jump ring, dash trail, ...) and which scene it is.
    private FrameVfx vfxPreview;
    private string vfxPreviewPath = "";

    // Name of the animation clip to hold. Use "List Animations" below to see valid names.
    [Export] public string Animation { get => animation; set { animation = value; ApplyPose(); } }
    // Frame within the clip (game ticks, matching Startup/Active counts). Frame 0 = start.
    // Converted to seconds with the sim tick rate, exactly like the game advances animation.
    [Export] public int Frame { get => frame; set { frame = value; ApplyPose(); } }

    // A state VFX scene (res://sprites/vfx/*.tscn) to hold on screen for positioning. These aren't
    // tied to an attack, so unlike the slash preview there's nothing to look up from the animation
    // — scrub VfxFrame by hand to see any point of the effect.
    [Export(PropertyHint.File, "*.tscn")] public string PreviewVfxScene { get => vfxScene; set { vfxScene = value; ApplyPose(); } }
    // Frame within the effect, 0 to its own DurationFrames. Progress is derived from that, so the
    // shape you see here is exactly what that frame looks like in game.
    [Export] public int VfxFrame { get => vfxFrame; set { vfxFrame = value; ApplyPose(); } }

    // Toggle to re-apply the pose (auto-resets). Handy after reopening the scene.
    [Export] public bool ApplyNow { get => false; set { if (value) ApplyPose(); } }
    // Toggle to print every clip name and its length (seconds) to the Output panel.
    [Export] public bool ListAnimations { get => false; set { if (value) PrintAnimationList(); } }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            ApplyPose();
    }

    private AnimationPlayer FindAnimPlayer()
    {
        Node search = GetParent() ?? this;
        // owned:false so it descends into the instanced .glb, whose nodes we don't own.
        return search.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
    }

    private void ApplyPose()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        ApplyModelPose();
        UpdateSlashPreview();
        UpdateVfxPreview();
    }

    private void ApplyModelPose()
    {
        if (string.IsNullOrEmpty(animation))
            return;

        AnimationPlayer anim = FindAnimPlayer();
        if (anim == null)
        {
            GD.PushWarning("EditorPose: no AnimationPlayer found under the player model.");
            return;
        }
        if (!anim.HasAnimation(animation))
        {
            GD.PushWarning($"EditorPose: no animation named '{animation}'. Use 'List Animations' to see names.");
            return;
        }

        float time = frame * PlayerConstants.FixedDelta; // frames -> seconds, same as the game

        // Play + Seek(update:true) + Pause applies the pose for that frame and then holds it
        // static (Pause stops it advancing). The skeleton wireframe you may see is Godot's own
        // Skeleton3D editor gizmo, not drawn by this script - hide it via the viewport's
        // View > Gizmos > Skeleton3D toggle.
        anim.Play(animation);
        anim.Seek(time, true);
        anim.Pause();
    }

    // Shows the slash VFX for the current animation at the current frame, using the exact same
    // timing/opacity math the game uses at runtime, so hitboxes can be lined up against the smear.
    private void UpdateSlashPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        Node3D model = GetParent()?.GetNodeOrNull<Node3D>("Model");
        if (model == null)
            return;

        AttackData move = Moveset.FindByAnimationName(animation);
        bool hasVfx = move != null && move.Vfx.HasScene;
        string desiredPath = hasVfx ? move.Vfx.ScenePath : "";

        // Rebuild only when the target scene changes (also clears leftovers after a script reload).
        if (slashPreview == null || !IsInstanceValid(slashPreview) || slashPreviewPath != desiredPath)
        {
            foreach (Node child in model.GetChildren())
                if (child.Name == "__SlashPreview")
                    child.QueueFree();

            slashPreview = null;
            slashPreviewPath = desiredPath;

            if (desiredPath != "")
            {
                PackedScene packed = GD.Load<PackedScene>(desiredPath);
                if (packed != null)
                {
                    slashPreview = packed.Instantiate<Node3D>();
                    slashPreview.Name = "__SlashPreview";
                    model.AddChild(slashPreview); // no owner set -> not saved into the scene file
                    // The lab character isn't hybrid-projected (PlayerAnimator doesn't run in-editor),
                    // so keep the preview full-perspective to match it.
                    PlayerVfx.SetParam(slashPreview, "ortho_amount", 0f);
                }
            }
        }

        if (slashPreview != null && IsInstanceValid(slashPreview))
        {
            // framesSinceSpawn = 0 on the first active frame (Startup + 1), matching PlayerVfx.
            float alpha = hasVfx ? move.Vfx.AlphaAt(frame - (move.Startup + 1)) : 0f;
            PlayerVfx.ApplyOpacity(slashPreview, "opacity", alpha);
        }
    }

    // Holds a state VFX at VfxFrame so it can be positioned and sized against the real character.
    // Parented to the Player (not the Model) to match runtime, where PlayerVfx adds these to itself.
    private void UpdateVfxPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        Node parent = GetParent();
        if (parent == null)
            return;

        if (vfxPreview == null || !IsInstanceValid(vfxPreview) || vfxPreviewPath != vfxScene)
        {
            foreach (Node child in parent.GetChildren())
                if (child.Name == "__VfxPreview")
                    child.QueueFree();

            vfxPreview = null;
            vfxPreviewPath = vfxScene;

            if (!string.IsNullOrEmpty(vfxScene))
            {
                PackedScene packed = GD.Load<PackedScene>(vfxScene);
                vfxPreview = packed?.Instantiate() as FrameVfx;
                if (vfxPreview == null)
                {
                    GD.PushWarning($"EditorPose: '{vfxScene}' is missing or its root is not a FrameVfx.");
                    return;
                }

                vfxPreview.Name = "__VfxPreview";
                parent.AddChild(vfxPreview); // no owner set -> not saved into the scene file
            }
        }

        if (vfxPreview != null && IsInstanceValid(vfxPreview) && vfxPreview.DurationFrames > 0)
            vfxPreview.SetProgress((float)vfxFrame / vfxPreview.DurationFrames);
    }

    private void PrintAnimationList()
    {
        AnimationPlayer anim = FindAnimPlayer();
        if (anim == null)
        {
            GD.PushWarning("EditorPose: no AnimationPlayer found under the player model.");
            return;
        }

        GD.Print("--- Animations ---");
        foreach (string name in anim.GetAnimationList())
        {
            Animation clip = anim.GetAnimation(name);
            int frames = Mathf.RoundToInt(clip.Length / PlayerConstants.FixedDelta);
            GD.Print($"'{name}'  {frames} frames  ({clip.Length:0.###}s)");
        }
    }
}
