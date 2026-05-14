using System.Collections.Generic;
using System.Numerics;

public enum MoveType { Light, Medium, Heavy, CrouchLight, CrouchMedium, CrouchHeavy, Jump, Dash }

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

// Data for a grab move.
public class GrabData : MoveData
{
    public int GrabSequenceDuration; // total frames of the whole animation

    // Positional offsets for both the attacker and the defender per frame
    // These offsets are "how much do i move on frame x" rather than "where am i on frame x"
    public List<Vector3> AttackerOffsetsPerFrame;
    public List<Vector3> DefenderOffsetsPerFrame;
}
