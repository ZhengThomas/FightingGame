using System.Collections.Generic;
using System.Numerics;

public enum MoveType { Light, Medium, Heavy, CrouchLight, CrouchMedium, CrouchHeavy, Jump, Dash }
public class AttackData
{
    // frame data
    public int Startup;       // frames before active
    public int Active;        // frames hitbox is out
    public int Recovery;      // frames after active before you can act

    // properties
    public int Damage;
    public int HitStun;       // frames opponent is in hitstun on hit
    public int BlockStun;   // frames opponent is in blockstun on block
    public float Pushback;    // horizontal slide applied while grounded in Hitstun
    public float PushbackOnBlock;    // horizontal slide applied while grounded in Blockstun
    public float LaunchForce; // launch velocity applied when sent airborne
    public float LaunchForceOnBlock; // launch velocity applied when sent airborne on block

    // hitbox — where the attack hits
    public List<Box>[] HitboxesPerFrame;

    // index = any attack frame (0-based, covers startup+active+recovery), value = list of hurtboxes that frame
    public List<Box>[] HurtboxesPerFrame;

    // properties flags
    public bool IsOverhead;           // hits crouching block
    public bool IsLow;                // must be blocked crouching
    public bool LaunchesOpponent;     // sends them airborne on hit
    public List<MoveType> CancellableInto;
    public MoveType Strength;    // Light, Medium, or Heavy

    public int HitPauseDuration;
}