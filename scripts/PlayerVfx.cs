using Godot;
using System.Collections.Generic;

// Spawns and fades the per-attack slash VFX in lockstep with the simulation.
//
// Each attack has its own slash scene (res://models/SlashVFX/*.tscn), referenced by the move's
// VfxCue. The scene is instanced once, lazily, and parented under the character model so it inherits
// facing (the model mirror-flips for left/right) and the character's scale/space automatically.
//
// OBJ/glTF can't carry opacity animation, so this drives the slash shader's "opacity" uniform.
// Opacity is a pure function of the active move's frame counter, so it stays tick-locked, freezes on
// hit pause/pause (it only advances on Player.Ticked), and needs nothing snapshotted for rollback.
public partial class PlayerVfx : Node3D
{
    [Export] public NodePath ModelPath;
    // Float (0..1) shader uniform that controls slash opacity. Must match slash.gdshader.
    [Export] public string OpacityParam = "opacity";
    // Match the characters' hybrid projection so slashes aren't perspective-distorted (see PlayerAnimator).
    [Export(PropertyHint.Range, "0,1")] public float OrthoAmount = 0.7f;
    // Sorting nudge applied to the slash when this player is the "front" one, so the slash sorts in the
    // same draw index as the attacker's body. Keep this equal to PlayerAnimator.DrawOrderDepthBias.
    [Export] public float DrawOrderDepthBias = 0.003f;

    protected Player player;
    protected MatchManager matchManager;
    protected Node3D model;

    // res path -> instanced scene root. Instanced once, reused, kept hidden (opacity 0) when idle.
    protected readonly Dictionary<string, Node3D> instances = new Dictionary<string, Node3D>();
    // every slash ShaderMaterial we've instanced, so _Process can keep plane_distance camera-synced.
    protected readonly List<ShaderMaterial> slashMaterials = new List<ShaderMaterial>();
    // scene path currently being shown, so we can hide it when the move changes or ends.
    protected string activePath = "";

    public override void _Ready()
    {
        player = GetParent<Player>();
        matchManager = GetNodeOrNull<MatchManager>("/root/MatchManager");
        if (player == null)
        {
            GD.PushWarning("PlayerVfx: parent is not a Player node.");
            return;
        }

        if (ModelPath != null && !ModelPath.IsEmpty)
            model = GetNodeOrNull<Node3D>(ModelPath);
        if (model == null)
        {
            GD.PushWarning($"PlayerVfx: no model found at ModelPath '{ModelPath}'.");
            return;
        }

        // No Ticked-signal subscription: opacity is a pure function of the active move's frame
        // counter, so we can just poll from _Process each real frame. Idempotent — safe under
        // rollback catch-up, since we always compute from the (post-resim) current sim state.
    }

    // Runs every real frame. Two independent jobs:
    //   1. Poll current move state and drive slash opacity from the move's frame counter.
    //   2. Keep the slash shaders' plane_distance synced to the camera, like PlayerAnimator
    //      does for the body — same reason (perspective/scale consistency at any zoom).
    public override void _Process(double delta)
    {
        UpdateSlashOpacity();
        UpdateShaderParams();
    }

    protected virtual void UpdateSlashOpacity()
    {
        if (player == null) return;

        string desiredPath = "";
        float alpha = 0f;

        if (player.IsAttacking() && player.CurrentMove.HasMove
            && player.CurrentMove.Data is MoveData data && data.Vfx.HasScene)
        {
            // The first active frame is Startup + 1 (see Player move-frame logic); count from there.
            int framesSinceSpawn = player.CurrentMove.Frame - (data.Startup + 1);
            desiredPath = data.Vfx.ScenePath;
            alpha = data.Vfx.AlphaAt(framesSinceSpawn);
        }

        // If we switched to a different slash (or stopped attacking), hide the previous one.
        if (activePath != "" && activePath != desiredPath)
            SetOpacity(activePath, 0f);

        activePath = desiredPath;
        if (desiredPath != "")
            SetOpacity(desiredPath, alpha);
    }

    protected virtual void UpdateShaderParams()
    {
        if (slashMaterials.Count == 0)
            return;

        Camera3D cam = GetViewport()?.GetCamera3D();
        if (cam == null)
            return;

        float planeDist = cam.GlobalPosition.Z;
        if (planeDist <= 0.01f)
            return;

        // Match the attacker's body sorting: when this player is the front one, carry the same bias
        // so the slash draws in the same draw index (occluded by its own body, over the opponent).
        float depthBias = (matchManager != null && matchManager.FrontPlayer == player)
            ? DrawOrderDepthBias
            : 0f;

        foreach (ShaderMaterial mat in slashMaterials)
        {
            mat.SetShaderParameter("plane_distance", planeDist);
            mat.SetShaderParameter("depth_bias", depthBias);
        }
    }

    protected void SetOpacity(string scenePath, float alpha)
    {
        Node3D inst = GetOrCreate(scenePath);
        if (inst != null)
            ApplyOpacity(inst, OpacityParam, alpha);
    }

    // Loads + instances the slash scene the first time it's needed, then caches it. Parents it under
    // the model so it mirrors with facing and matches the character's scale/space.
    protected virtual Node3D GetOrCreate(string scenePath)
    {
        if (instances.TryGetValue(scenePath, out Node3D existing))
            return existing;

        PackedScene packed = GD.Load<PackedScene>(scenePath);
        if (packed == null)
        {
            GD.PushWarning($"PlayerVfx: could not load slash scene '{scenePath}'.");
            instances[scenePath] = null;
            return null;
        }

        Node3D inst = packed.Instantiate<Node3D>();
        model.AddChild(inst);                  // inherits facing-mirror + model scale

        // Track this scene's materials and match the character's ortho amount; _Process then keeps
        // their plane_distance synced to the camera.
        CollectShaderMaterials(inst, slashMaterials);
        foreach (ShaderMaterial mat in slashMaterials)
            mat.SetShaderParameter("ortho_amount", OrthoAmount);

        ApplyOpacity(inst, OpacityParam, 0f);  // start hidden
        instances[scenePath] = inst;
        return inst;
    }

    protected static void CollectShaderMaterials(Node node, List<ShaderMaterial> into)
    {
        if (node is MeshInstance3D mesh)
        {
            if (mesh.MaterialOverride is ShaderMaterial ov && !into.Contains(ov))
                into.Add(ov);

            for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
                if (mesh.GetActiveMaterial(i) is ShaderMaterial m && !into.Contains(m))
                    into.Add(m);
        }

        foreach (Node child in node.GetChildren())
            CollectShaderMaterials(child, into);
    }

    // Convenience wrapper for the opacity uniform.
    public static void ApplyOpacity(Node node, string opacityParam, float alpha)
        => SetParam(node, opacityParam, alpha);

    // Sets a shader uniform on every ShaderMaterial in a slash scene, whether it's a material
    // override or a per-surface override (so it works regardless of how the material was assigned).
    // Static so the editor hitbox-lab preview (EditorPose) can reuse the exact same logic.
    public static void SetParam(Node node, string param, Variant value)
    {
        if (node is MeshInstance3D mesh)
        {
            if (mesh.MaterialOverride is ShaderMaterial ov)
                ov.SetShaderParameter(param, value);

            for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is ShaderMaterial m)
                    m.SetShaderParameter(param, value);
            }
        }

        foreach (Node child in node.GetChildren())
            SetParam(child, param, value);
    }
}
