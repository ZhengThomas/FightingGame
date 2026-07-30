using System.Collections.Generic;

public enum MoveType { Light, Medium, Heavy, CrouchLight, CrouchMedium, CrouchHeavy, Jump, Dash, Grab }

// A slash VFX tied to a move. The scene is instanced once (cached) under the character model, so it
// inherits facing (the model mirror-flips) and scale for free. glTF/OBJ can't carry opacity
// animation, so PlayerVfx drives the shader's "opacity" uniform instead.
//
// The slash pops to full opacity on the move's first active frame, then fades linearly to 0 over
// FadeFrames. Opacity is a pure function of the frame count -> tick-locked, freezes on hit pause,
// rollback-consistent.
public struct VfxCue
{
    public string ScenePath; // res:// slash scene; null/"" = no VFX for this move
    public int FadeFrames;   // frames to fade from full opacity to 0, starting on the first active frame

    public readonly bool HasScene => !string.IsNullOrEmpty(ScenePath);

    // framesSinceSpawn = 0 on the first active frame.
    public readonly float AlphaAt(int framesSinceSpawn)
    {
        if (framesSinceSpawn < 0 || framesSinceSpawn >= FadeFrames) return 0f;
        return 1f - (float)framesSinceSpawn / FadeFrames;
    }
}

// Base class for all move data. Contains the frame structure and hitbox information
// shared by every move type.
public class MoveData
{
    // Stable numeric identity for this move, assigned once at startup by MoveData.Register.
    public int Id { get; internal set; } = -1;

    static MoveData[] byId;

    // Called once during static init from Moveset. Assigns each move a stable id equal to its
    // position in the argument list, then remembers the array for LookupById.
    public static void Register(params MoveData[] moves)
    {
        byId = moves;
        for (int i = 0; i < moves.Length; i++)
            moves[i].Id = i;
    }

    public static MoveData LookupById(int id)
        => (byId != null && id >= 0 && id < byId.Length) ? byId[id] : null;

    // Name of the AnimationPlayer clip this move plays.
    public string AnimationName = "";

    public int Startup;      // frames before active
    public int Active;       // frames hitbox is out
    public int Recovery;     // frames after active before the player can act

    public int Damage;
    public MoveType Strength;
    public int HitPauseDuration;

    // per-frame hitboxes (indexed from the first active frame)
    public List<Box>[] HitboxesPerFrame;

    // per-frame hurtbox overrides (0-based over startup+active+recovery); null entry = use default pose
    public List<Box>[] HurtboxesPerFrame;

    // slash VFX to fade in/out over this move's frames (default ScenePath = "" = none)
    public VfxCue Vfx;

    // If true, landing from the resulting air hitstun goes to hard Knockdown+Wakeup.
    // Otherwise (default) they soft-knockdown and stand up quickly. Shared by normals and throws.
    public bool CausesHardKnockdown;

    // How many steps to advance on each ComboScaling table after this hit connects.
    public int DamageProrationSteps = 1;
    public int HitstunProrationSteps = 1;
}

// Data for a normal (non-grab) attack.
public class AttackData : MoveData
{
    public int HitStun;
    public int BlockStun;
    public int Pushback;
    public int PushbackOnBlock;
    public int LaunchForce;
    public int LaunchForceOnBlock;

    public bool IsOverhead;       // must be blocked standing
    public bool IsLow;            // must be blocked crouching
    public bool LaunchesOpponent; // sends the opponent airborne on hit
    public List<MoveType> CancellableInto;
}

// Data for a grab move. Bypasses blocking and hands control of the defender to the attacker.
// The attacker scripts the grab in HandleGrabHitState using the Player movement API
// (SetVelocity, AddVelocity, SnapToSim) on their grabPartner.
public class GrabData : MoveData
{
    public int GrabSequenceDuration; // total frames before the attacker returns to Idle

    // Where the defender snaps the moment the grab connects, as an offset from the attacker's
    // position, in sim units. X is in the attacker's forward direction.
    public int DefenderSnapOffsetX;
    public int DefenderSnapOffsetY;

    // If true, the attacker turns around before the defender snaps into position,
    // placing the defender behind the attacker (a backthrow).
    public bool IsBackThrow;
}
