using System.Collections.Generic;

public enum MoveType { Light, Medium, Heavy, CrouchLight, CrouchMedium, CrouchHeavy, Jump, Dash, Grab }

// Base class for all move data. Contains the frame structure and hitbox information
// shared by every move type.
public class MoveData
{
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
}

// Data for a normal (non-grab) attack.
public class AttackData : MoveData
{
    public int HitStun;
    public int BlockStun;
    public float Pushback;
    public float PushbackOnBlock;
    public float LaunchForce;
    public float LaunchForceOnBlock;

    public bool IsOverhead;       // must be blocked standing
    public bool IsLow;            // must be blocked crouching
    public bool LaunchesOpponent; // sends the opponent airborne on hit
    public List<MoveType> CancellableInto;
}

// Data for a grab move. Bypasses blocking and hands control of the defender to the attacker.
// The attacker scripts the grab in HandleGrabHitState using the Player movement API
// (SetVelocity, AddVelocity, SnapToPosition) on their grabPartner.
public class GrabData : MoveData
{
    public int GrabSequenceDuration; // total frames before the attacker returns to Idle

    // Where the defender snaps the moment the grab connects,
    // as an offset from the attacker's position. X is in the attacker's forward direction.
    public float DefenderSnapOffsetX;
    public float DefenderSnapOffsetY;

    // If true, the attacker turns around before the defender snaps into position,
    // placing the defender behind the attacker (a backthrow).
    public bool IsBackThrow;
}
