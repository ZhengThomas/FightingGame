using Godot;
using System.Collections.Generic;

// Draws whatever animation state the sim decided on. All the decisions live in PlayerAnimFsm —
// this node maps AnimState to a clip name, keeps the clip at the frame the FSM says, and mirrors
// the model to face the opponent.
//
// It deliberately keeps no timeline of its own. The only state here is what was drawn last frame,
// used to pick between crossfading, advancing, and hard-seeking. If that's ever wrong the worst
// case is one frame of visual pop.
//
// Put this on a child node of a Player, with ModelPath pointing at the imported model
// (the node that contains an AnimationPlayer).
public partial class PlayerAnimator : Node3D
{
    [Export] public NodePath ModelPath;
    [Export] public string IdleAnim = "IdleStand";
    [Export] public string WalkForwardAnim = "WalkForwards";
    [Export] public string WalkBackwardAnim = "WalkBackwards";
    [Export] public string RunAnim = "RunLoop";
    [Export] public string BackdashAnim = "Backdash";
    [Export] public string CrouchAnim = "CrouchIdle";
    [Export] public string JumpSquatAnim = "JumpSquat";
    [Export] public string JumpRiseAnim = "JumpUp";
    [Export] public string FallAnim = "IntoFalling";
    [Export] public string LandingAnim = "Landing";
    [Export] public string WakeupAnim = "WakeupSlow";       // rising after a knockdown

    // Reaction clips. The "...End"/hurt clips are recovery-style animations; used here as the whole
    // reaction until dedicated held-hurt/held-block clips exist (placeholders, easy to swap later).
    [Export] public string HurtAnim = "StandingHurtEnd";        // grounded hitstun
    [Export] public string AirHurtRiseAnim = "LaunchingHurt";   // airborne hitstun, rising
    [Export] public string AirHurtFallAnim = "FallingHurt";     // airborne hitstun, falling
    [Export] public string SoftKnockdownAnim = "SoftKnockdown"; // short KD (placeholder until a soft clip exists)
    [Export] public string KnockdownAnim = "HardKnockdown";     // hard knockdown lie-down
    [Export] public string BlockAnim = "StandBlockEnd";         // standing blockstun
    [Export] public string CrouchBlockAnim = "CrouchBlockEnd";  // crouch blockstun
    [Export] public string AirBlockAnim = "IntoFalling";        // air blockstun (placeholder: no air-block clip yet)
    [Export] public string AirdashForwardAnim = "AirdashForwards";
    [Export] public string AirdashBackAnim = "AirdashBackwards";

    // One-shot clips. Their frame counts are mirrored in PlayerAnimFsm, which needs the lengths
    // sim-side to know when each one ends — change a clip's length and update it there too.
    [Export] public string RunStartAnim = "RunStart";
    [Export] public string RunStopAnim = "RunStop";
    [Export] public string CrouchDownAnim = "CrouchDown";
    [Export] public string CrouchStandAnim = "StandUp";

    // Seconds of cross-fade when switching clips.
    [Export] public float BlendTime = 0.08f;

    // --- Hybrid (horizontal-orthographic) projection ---
    // On ready, swaps every surface of the model to a shader that blends orthographic and
    // perspective on the horizontal axis (like Guilty Gear / SF) so characters don't look angled
    // when they slide toward the screen edges. Textures are copied over automatically.
    [ExportGroup("Hybrid Projection")]
    [Export] public bool UseHybridProjection = true;
    [Export(PropertyHint.Range, "0,1")] public float OrthoAmount = 0.7f;
    [Export] public float PlaneDistance = 4.0f;
    // A surface is treated as the outline if its material has no texture, culls front faces, or
    // its name contains this hint. Set this to your outline material/object name if detection is off.
    [Export] public string OutlineNameHint = "outline";
    // Tiny reverse-Z depth nudge applied when this player is the "front" one (see MatchManager)
    // so it renders on top of the other where they overlap
    [Export] public float DrawOrderDepthBias = 0.003f;

    protected Player player;
    protected MatchManager matchManager;
    protected Node3D model;
    protected AnimationPlayer animPlayer;
    protected Basis modelBaseBasis;   // the model's editor orientation/scale = "facing right"
    protected Vector3 modelBaseOrigin;

    // What was on screen last frame, so we can tell a fresh clip from a continuing one and a
    // rollback rewind from normal forward progress.
    protected string currentAnim;
    protected AnimState lastState;
    protected int lastToken;
    protected int lastFrame = -1;

    // Every hybrid-projection material we created, so we can update plane_distance each frame to
    // match the camera's current distance (keeps character size consistent with the world at any zoom).
    protected readonly List<ShaderMaterial> hybridMaterials = new List<ShaderMaterial>();

    // Mirrors across the X axis (parent space), so the model flips to face the other side
    // instead of physically turning around.
    private static readonly Basis MirrorX = new Basis(
        new Vector3(-1f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 0f, 1f));

    public override void _Ready()
    {
        player = GetParent<Player>();
        matchManager = MatchManager.Current;
        if (player == null)
        {
            GD.PushWarning("PlayerAnimator: parent is not a Player node.");
            return;
        }

        if (ModelPath != null && !ModelPath.IsEmpty)
            model = GetNodeOrNull<Node3D>(ModelPath);

        if (model == null)
        {
            GD.PushWarning($"PlayerAnimator: no model found at ModelPath '{ModelPath}'.");
            return;
        }

        // Remember the model's editor transform as its "facing right" pose.
        modelBaseBasis = model.Basis;
        modelBaseOrigin = model.Position;
        // Imported glTF/glb scenes carry their AnimationPlayer somewhere inside them.
        animPlayer = model.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
        if (animPlayer == null)
        {
            GD.PushWarning("PlayerAnimator: model has no AnimationPlayer child.");
        }
        else
        {
            // We drive the clip position ourselves from the FSM's frame counter, so it stays
            // locked to the fixed simulation and freezes whenever the game isn't ticking
            // (paused with the freeze key, single-stepping, or during hit pause).
            animPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;

            // One-shot and reaction clips hold their last frame instead of looping.
            ForceNoLoop(RunStartAnim);
            ForceNoLoop(RunStopAnim);
            ForceNoLoop(CrouchDownAnim);
            ForceNoLoop(CrouchStandAnim);
            ForceNoLoop(LandingAnim);
            ForceNoLoop(WakeupAnim);
            ForceNoLoop(HurtAnim);
            ForceNoLoop(AirHurtRiseAnim);
            ForceNoLoop(AirHurtFallAnim);
            ForceNoLoop(SoftKnockdownAnim);
            ForceNoLoop(KnockdownAnim);
            ForceNoLoop(BlockAnim);
            ForceNoLoop(CrouchBlockAnim);
            ForceNoLoop(AirdashForwardAnim);
            ForceNoLoop(AirdashBackAnim);
        }

        if (UseHybridProjection)
            ApplyHybridProjection(model);

        // Apply an initial pose so we don't sit in the bind/T-pose.
        UpdateFacing();
        UpdateAnimation();
    }

    // Walks the model and replaces each surface's material with our hybrid-projection shader,
    // copying over the original texture/color so the look is preserved. Runs once at startup.
    protected virtual void ApplyHybridProjection(Node root)
    {
        Shader bodyShader = GD.Load<Shader>("res://shaders/hybrid_projection.gdshader");
        Shader outlineShader = GD.Load<Shader>("res://shaders/hybrid_outline.gdshader");
        if (bodyShader == null || outlineShader == null)
        {
            GD.PushWarning("PlayerAnimator: hybrid projection shaders not found under res://shaders/.");
            return;
        }
        ApplyHybridToNode(root, bodyShader, outlineShader);
    }

    private void ApplyHybridToNode(Node node, Shader bodyShader, Shader outlineShader)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                // The material the surface renders with today (the imported glTF material).
                BaseMaterial3D original = mesh.GetActiveMaterial(i) as BaseMaterial3D;

                // Detect the outline purely by name
                bool isOutline = !string.IsNullOrEmpty(OutlineNameHint)
                    && (original?.ResourceName ?? "").ToLower().Contains(OutlineNameHint.ToLower());

                ShaderMaterial swapped = new ShaderMaterial();
                if (isOutline)
                {
                    swapped.Shader = outlineShader;
                    if (original != null)
                        swapped.SetShaderParameter("outline_color", original.AlbedoColor);
                }
                else
                {
                    swapped.Shader = bodyShader;
                    if (original != null)
                    {
                        // Colors/textures may live in either the albedo OR the emission channel
                        // (your model uses emission). Prefer whichever actually has data.
                        bool useEmission = original.EmissionEnabled
                            && (original.EmissionTexture != null || original.Emission != new Color(0, 0, 0, 1));

                        Color displayColor = useEmission ? original.Emission : original.AlbedoColor;
                        Texture2D displayTex = original.AlbedoTexture
                            ?? (useEmission ? original.EmissionTexture : null);

                        swapped.SetShaderParameter("albedo_color", displayColor);
                        if (displayTex != null)
                        {
                            swapped.SetShaderParameter("albedo_texture", displayTex);
                            swapped.SetShaderParameter("use_texture", true);
                        }
                    }
                }
                swapped.SetShaderParameter("ortho_amount", OrthoAmount);
                swapped.SetShaderParameter("plane_distance", PlaneDistance);
                hybridMaterials.Add(swapped);

                mesh.SetSurfaceOverrideMaterial(i, swapped);
            }
        }

        foreach (Node child in node.GetChildren())
            ApplyHybridToNode(child, bodyShader, outlineShader);
    }

    public override void _Process(double delta)
    {
        if (player == null || model == null) return;
        UpdateFacing();
        UpdateAnimation();
        UpdateHybridProjection();
    }

    // Face the opponent: editor pose faces right, facing left mirrors it across X (a flip, not a turn).
    protected virtual void UpdateFacing()
    {
        Basis basis = player.GetFacing() == FacingDirection.Left
            ? MirrorX * modelBaseBasis
            : modelBaseBasis;
        model.Transform = new Transform3D(basis, modelBaseOrigin);
    }

    // Put the clip where the FSM says it should be.
    //
    // Entering a state crossfades from frame 0; staying in one advances by however many ticks
    // elapsed (0 during hit pause, several after a rollback catch-up). A frame counter that moved
    // backwards means the sim rewound, so we jump straight to the right spot instead.
    protected virtual void UpdateAnimation()
    {
        if (animPlayer == null) return;

        AnimatorState a = player.Anim;
        string clip = ClipFor(a.Current);
        if (string.IsNullOrEmpty(clip) || !animPlayer.HasAnimation(clip))
            return;

        bool entered = a.Current != lastState || a.ClipToken != lastToken || lastFrame < 0;

        if (entered)
        {
            // Same clip re-entered (a chain into the same attack, or a re-hit) has nothing to
            // crossfade from, so it hard-cuts. Reactions snap too, so frame 1 reads immediately.
            bool sameClip = clip == currentAnim;
            float blend = sameClip || IsReaction(a.Current) ? 0f : BlendTime;
            animPlayer.Play(clip, blend);
            currentAnim = clip;
            if (a.Frame > 0 || blend <= 0f)
                animPlayer.Seek(SeekTime(clip, a.Frame), true);
            else
                // Callback mode is Manual, so Play alone doesn't pose the skeleton — this applies
                // frame 0 without moving time. Without it a fresh clip holds the previous pose for
                // a frame, and the very first clip sits in the bind pose.
                animPlayer.Advance(0.0);
        }
        else if (a.Frame > lastFrame)
        {
            animPlayer.Advance((a.Frame - lastFrame) * PlayerConstants.FixedDelta);
        }
        else if (a.Frame < lastFrame)
        {
            animPlayer.Seek(SeekTime(clip, a.Frame), true);
        }

        lastState = a.Current;
        lastToken = a.ClipToken;
        lastFrame = a.Frame;
    }

    // Frame number to clip time, wrapped for looping clips so a long-running loop doesn't seek
    // past its end.
    protected double SeekTime(string clip, int frame)
    {
        double t = frame * PlayerConstants.FixedDelta;
        Animation a = animPlayer.GetAnimation(clip);
        if (a != null && a.LoopMode != Animation.LoopModeEnum.None && a.Length > 0f)
            t %= a.Length;
        return t;
    }

    // The one place AnimState turns into a clip name. Attacks name their own clip in MoveData.
    protected virtual string ClipFor(AnimState state) => state switch
    {
        AnimState.Idle           => IdleAnim,
        AnimState.WalkForward    => WalkForwardAnim,
        AnimState.WalkBack       => WalkBackwardAnim,
        AnimState.RunStart       => RunStartAnim,
        AnimState.Run            => RunAnim,
        AnimState.RunStop        => RunStopAnim,
        AnimState.CrouchDown     => CrouchDownAnim,
        AnimState.Crouch         => CrouchAnim,
        AnimState.StandUp        => CrouchStandAnim,
        AnimState.Backdash       => BackdashAnim,
        AnimState.JumpSquat      => JumpSquatAnim,
        AnimState.JumpRise       => JumpRiseAnim,
        AnimState.Fall           => FallAnim,
        AnimState.Landing        => LandingAnim,
        AnimState.AirdashForward => AirdashForwardAnim,
        AnimState.AirdashBack    => AirdashBackAnim,
        AnimState.Attack         => player.CurrentMove.Data?.AnimationName ?? IdleAnim,
        AnimState.Hurt           => HurtAnim,
        AnimState.AirHurtRise    => AirHurtRiseAnim,
        AnimState.AirHurtFall    => AirHurtFallAnim,
        AnimState.SoftKnockdown  => SoftKnockdownAnim,
        AnimState.HardKnockdown  => KnockdownAnim,
        AnimState.Wakeup         => WakeupAnim,
        AnimState.Block          => BlockAnim,
        AnimState.CrouchBlock    => CrouchBlockAnim,
        AnimState.AirBlock       => AirBlockAnim,
        _ => IdleAnim,
    };

    protected static bool IsReaction(AnimState state)
        => state == AnimState.Hurt
        || state == AnimState.AirHurtRise
        || state == AnimState.AirHurtFall
        || state == AnimState.Block
        || state == AnimState.CrouchBlock
        || state == AnimState.AirBlock;

    protected virtual void UpdateHybridProjection()
    {
        if (!UseHybridProjection || hybridMaterials.Count == 0)
            return;

        Camera3D cam = GetViewport()?.GetCamera3D();
        if (cam == null)
            return;

        // Players live on z = 0 and the camera looks straight down -Z, so its distance to the
        // plane is just its world Z.
        float planeDist = cam.GlobalPosition.Z;
        if (planeDist <= 0.01f)
            return;

        // If we're the current front player, nudge our sorting depth so we draw over the other
        // where the two overlap (no size change, unlike physically moving in Z).
        float depthBias = (matchManager != null && matchManager.FrontPlayer == player)
            ? DrawOrderDepthBias
            : 0f;

        foreach (ShaderMaterial mat in hybridMaterials)
        {
            mat.SetShaderParameter("plane_distance", planeDist);
            mat.SetShaderParameter("depth_bias", depthBias);
        }
    }

    // Forces a clip to not loop (used for one-shot clips so they hold their last frame).
    protected virtual void ForceNoLoop(string anim)
    {
        if (animPlayer == null || string.IsNullOrEmpty(anim) || !animPlayer.HasAnimation(anim))
            return;
        Animation clip = animPlayer.GetAnimation(anim);
        if (clip != null)
            clip.LoopMode = Animation.LoopModeEnum.None;
    }
}
