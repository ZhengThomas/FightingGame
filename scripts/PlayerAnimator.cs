using Godot;
using System.Collections.Generic;

// Drives the animated character model based on its parent Player's state.
// This is a self-contained visual layer: Player has no animation code and never calls into here.
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
    [Export] public string KnockdownAnim = "HardKnockdown";     // landing from hurt is always a hard knockdown
    [Export] public string BlockAnim = "StandBlockEnd";         // standing blockstun
    [Export] public string CrouchBlockAnim = "CrouchBlockEnd";  // crouch blockstun
    [Export] public string AirBlockAnim = "IntoFalling";        // air blockstun (placeholder: no air-block clip yet)
    [Export] public string AirdashForwardAnim = "AirdashForwards";
    [Export] public string AirdashBackAnim = "AirdashBackwards";

    // One-shot transition clips: played once on a state change, then handing off to the loop.
    // Enter transitions depend only on the state entered; exit transitions depend on the state left.
    [Export] public string RunStartAnim = "RunStart";       // enter run
    [Export] public string RunStopAnim = "RunStop";         // exit run -> standing
    [Export] public string CrouchDownAnim = "CrouchDown";   // enter crouch
    [Export] public string CrouchStandAnim = "StandUp";     // exit crouch -> standing

    // Seconds of cross-fade when switching clips. This is what smooths the walk->idle snap.
    // It's time-based, but driven by our manual Advance(), so it still respects tick-lock/pause.
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
    protected string currentAnim;

    // The state we were in last tick, and how many ticks remain in the current transition clip
    // (0 = no transition in progress, just play the steady-state loop).
    protected PlayerState prevState;
    // Id of the attack that was active last tick.
    protected int lastMoveId = -1;
    protected int transitionTicksLeft;
    // While a one-shot transition is playing, a state change only cuts it short if the incoming
    // state's priority is >= this threshold. Locomotion sits below every threshold, so wandering
    // between idle/walk can't cancel a stop/turn clip; committing to a jump/crouch/attack can.
    protected int transitionCancelThreshold;

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
        matchManager = GetNodeOrNull<MatchManager>("/root/MatchManager");
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
            // We advance the animation ourselves, one game tick at a time, so it stays
            // locked to the fixed simulation and freezes whenever the game isn't ticking
            // (paused with the freeze key, single-stepping, or during hit pause).
            animPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;

            // Transition clips play once and hold; don't let them loop.
            ForceNoLoop(RunStartAnim);
            ForceNoLoop(RunStopAnim);
            ForceNoLoop(CrouchDownAnim);
            ForceNoLoop(CrouchStandAnim);

            // Reaction / one-shot state clips also hold their last frame instead of looping.
            ForceNoLoop(LandingAnim);
            ForceNoLoop(WakeupAnim);
            ForceNoLoop(HurtAnim);
            ForceNoLoop(AirHurtRiseAnim);
            ForceNoLoop(AirHurtFallAnim);
            ForceNoLoop(KnockdownAnim);
            ForceNoLoop(BlockAnim);
            ForceNoLoop(CrouchBlockAnim);
            ForceNoLoop(AirdashForwardAnim);
            ForceNoLoop(AirdashBackAnim);
        }

        if (UseHybridProjection)
            ApplyHybridProjection(model);

        prevState = player.CurrentState;

        // Player fires this once per simulation tick; that's our animation clock.
        player.Ticked += OnTick;

        // Apply an initial pose (frame 0 of idle) so we don't sit in the bind/T-pose.
        UpdatePose();
        animPlayer?.Advance(0.0);
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

    // Keeps the hybrid shader's plane_distance equal to the camera's current distance to the fight
    // plane (z = 0). This makes the character scale with the perspective world at every zoom level,
    // so it no longer grows relative to the stage (which caused ground/walls to clip in when zoomed).
    public override void _Process(double delta)
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

    // Called once per game tick (via Player.Ticked). Advances the animation by exactly one tick.
    protected virtual void OnTick()
    {
        if (player == null || model == null)
            return;

        UpdatePose();
        // Step the animation forward by a single fixed tick's worth of time.
        animPlayer?.Advance(PlayerConstants.FixedDelta);
    }

    // Sets facing (mirror) and runs the clip-selection logic, without advancing time.
    protected virtual void UpdatePose()
    {
        // Face the opponent: editor pose faces right, facing left mirrors it across X (a flip, not a turn).
        Basis basis = player.GetFacing() == FacingDirection.Left
            ? MirrorX * modelBaseBasis
            : modelBaseBasis;
        model.Transform = new Transform3D(basis, modelBaseOrigin);

        UpdateAnimation();
    }

    // Animation "commitment" priorities. A one-shot transition clip that's playing is only cut short
    // by a state change whose priority is >= that transition's CancelThreshold (see GetTransition).
    // Add finer tiers here later (e.g. a separate "hit" tier above actions) as you need them.
    protected const int PriorityLocomotion = 0; // idle / walk — never interrupts a protected transition
    protected const int PriorityAction     = 1; // jump / crouch / attack / dash / hit — does interrupt

    // A one-shot clip plus the priority needed to cancel it early.
    protected readonly record struct AnimTransition(string Clip, int CancelThreshold);

    // How disruptive it is to *enter* a state. Only idle/walk are cheap locomotion; everything else
    // is a committed action. This is what lets a stop/turn clip survive idle<->walk flicker but still
    // get cancelled the instant you actually do something.
    protected virtual int StatePriority(PlayerState state)
        => (state == PlayerState.Idle || state == PlayerState.Walking)
            ? PriorityLocomotion
            : PriorityAction;

    // Every tick:
    //  - On a state change, (re)start that transition's clip or the loop — but only if we're not
    //    already inside a protected transition that the incoming state isn't allowed to cancel.
    //  - Always count down the running transition; once it's done, resolve to the loop for whatever
    //    state we're in *now* (so a dash-stop that finishes while holding back lands on walk-back).
    protected virtual void UpdateAnimation()
    {
        PlayerState state = player.CurrentState;
        int moveId = player.IsAttacking() ? (player.CurrentMove?.Id ?? -1) : -1;
        bool newMove = moveId != -1 && moveId != lastMoveId;
        bool startedClip = false;

        if (state != prevState)
        {
            bool inProtectedTransition = transitionTicksLeft > 0;
            bool canInterrupt = !inProtectedTransition
                || StatePriority(state) >= transitionCancelThreshold;

            if (canInterrupt)
            {
                AnimTransition? transition = GetTransition(prevState, state);
                if (transition.HasValue && animPlayer != null && animPlayer.HasAnimation(transition.Value.Clip))
                    PlayTransition(transition.Value.Clip, transition.Value.CancelThreshold);
                else
                    PlayLoop();
                startedClip = true;
            }
            // else: the running transition is protected against this state — leave it playing.
        }
        else if (newMove)
        {
            // A new attack started without a state change (chaining/mashing into the same attack).
            transitionTicksLeft = 0;
            PlayClip(PickAnim(), forceRestart: true);
            startedClip = true;
        }

        if (!startedClip)
        {
            if (transitionTicksLeft > 0)
            {
                transitionTicksLeft--;
                if (transitionTicksLeft == 0)
                    PlayLoop();
            }
            else
            {
                // No transition running: keep the loop up to date (catches within-state changes
                // like walk direction flips or jump rise -> fall).
                PlayLoop();
            }
        }

        prevState = state;
        lastMoveId = moveId;
    }

    // The one-shot clip to play for a state change (plus what it takes to cancel it early), or null
    // if the switch is instant. Exit transitions use PriorityAction so idle<->walk won't cancel them
    // but any real action will.
    protected virtual AnimTransition? GetTransition(PlayerState from, PlayerState to)
    {
        // Enter transitions (depend on the state being entered).
        if (to == PlayerState.GroundDashing)                          return new AnimTransition(RunStartAnim, PriorityAction);
        // Crouch down/stand up use a locomotion threshold so anything (even standing back up) cancels
        // them instantly — keeps teabags feeling snappy instead of locked into the full clip.
        if (to == PlayerState.Crouching && IsStandingState(from))     return new AnimTransition(CrouchDownAnim, PriorityLocomotion);

        // Exit transitions (depend on the state being left, back to neutral standing).
        if (from == PlayerState.GroundDashing && IsStandingState(to)) return new AnimTransition(RunStopAnim, PriorityAction);
        if (from == PlayerState.Crouching     && IsStandingState(to)) return new AnimTransition(CrouchStandAnim, PriorityLocomotion);

        return null;
    }

    protected static bool IsStandingState(PlayerState state)
        => state == PlayerState.Idle || state == PlayerState.Walking;

    // Steady-state clip for the current state (the loop that plays once a transition finishes).
    protected virtual string PickAnim()
    {
        // Attacks are data-driven: the active move names its own clip, so a single attack state can
        // drive any of its moves' animations. Falls through to the state defaults if no clip is set.
        if (player.IsAttacking())
        {
            string moveAnim = player.CurrentMove?.Data?.AnimationName;
            if (!string.IsNullOrEmpty(moveAnim))
                return moveAnim;
        }

        return player.CurrentState switch
        {
            PlayerState.Walking          => WalkAnimForDirection(),
            PlayerState.GroundDashing    => RunAnim, // "run" == forward dash in this game
            PlayerState.Backdashing      => BackdashAnim,
            PlayerState.Crouching or PlayerState.CrouchAttacking => CrouchAnim,
            PlayerState.JumpSquat        => JumpSquatAnim,
            PlayerState.Jumping or PlayerState.AirAttacking
                => player.Physics.VelocityY > 0f ? JumpRiseAnim : FallAnim,
            PlayerState.Airdashing       => AirdashAnimForDirection(),
            PlayerState.Landing          => LandingAnim,
            PlayerState.Hitstun          => HurtAnim,
            PlayerState.AirHitstun       => player.Physics.VelocityY > 0f ? AirHurtRiseAnim : AirHurtFallAnim,
            PlayerState.Knockdown        => KnockdownAnim,
            PlayerState.Wakeup           => WakeupAnim,
            PlayerState.Blockstun        => BlockAnim,
            PlayerState.CrouchBlockstun  => CrouchBlockAnim,
            PlayerState.AirBlockstun     => AirBlockAnim,
            _ => IdleAnim, // remaining states without a dedicated clip (grabs)
        };
    }

    // Forward vs backward airdash based on the locked-in airdash direction (velocity is 0 on startup).
    protected virtual string AirdashAnimForDirection()
    {
        bool forward = (player.GetFacing() == FacingDirection.Right && player.AirDashDirection > 0)
                    || (player.GetFacing() == FacingDirection.Left  && player.AirDashDirection < 0);
        return forward ? AirdashForwardAnim : AirdashBackAnim;
    }

    // Picks forward vs backward walk based on whether we're moving toward the opponent.
    protected virtual string WalkAnimForDirection()
    {
        float vx = player.Physics.VelocityX;
        if (Mathf.Abs(vx) < 0.01f)
            return WalkForwardAnim;

        bool movingForward = (player.GetFacing() == FacingDirection.Right && vx > 0f)
                          || (player.GetFacing() == FacingDirection.Left  && vx < 0f);
        return movingForward ? WalkForwardAnim : WalkBackwardAnim;
    }

    // Plays the steady-state loop for the current state, cross-fading over BlendTime.
    protected virtual void PlayLoop()
    {
        transitionTicksLeft = 0;
        PlayClip(PickAnim());
    }

    // Plays a one-shot transition clip, arms the tick countdown for its length, and records the
    // priority needed to cancel it early.
    protected virtual void PlayTransition(string anim, int cancelThreshold)
    {
        PlayClip(anim);
        Animation clip = animPlayer?.GetAnimation(anim);
        float length = clip?.Length ?? 0f;
        transitionTicksLeft = Mathf.Max(1, Mathf.CeilToInt(length / PlayerConstants.FixedDelta));
        transitionCancelThreshold = cancelThreshold;
    }

    protected virtual void PlayClip(string anim, bool forceRestart = false)
    {
        if (animPlayer == null || string.IsNullOrEmpty(anim))
            return;
        if (currentAnim == anim && !forceRestart)
            return; // already playing it, don't restart every frame
        if (!animPlayer.HasAnimation(anim))
            return; // clip not in the model; keep whatever is playing

        animPlayer.Play(anim, BlendTime);
        if (forceRestart)
            animPlayer.Seek(0.0, true); // replay from the top even if it's the same clip name
        currentAnim = anim;
    }

    // Forces a clip to not loop (used for transition clips so they hold their last frame).
    protected virtual void ForceNoLoop(string anim)
    {
        if (animPlayer == null || string.IsNullOrEmpty(anim) || !animPlayer.HasAnimation(anim))
            return;
        Animation clip = animPlayer.GetAnimation(anim);
        if (clip != null)
            clip.LoopMode = Animation.LoopModeEnum.None;
    }
}
