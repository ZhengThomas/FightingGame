using System.Collections.Generic;

// Static per-move-hit data describing WHAT a hit does when it lands — geometry, damage,
// hitstun, pushback, etc. A move (see MoveData) contains a schedule of HitboxSpawns.
public class HitboxData
{
    // Stable numeric identity assigned by Register. HitboxInstance stores this id instead of
    // a class reference so it stays a value type that snapshots by copy.
    // 0 = unregistered, which HitboxInstance reads as an inactive slot.
    public int Id { get; internal set; }

    static HitboxData[] byId;

    // Ids are 1-based so a zeroed HitboxSlots reads as eight empty slots rather than eight
    // live copies of the hitbox registered first.
    public static void Register(params HitboxData[] hitboxes)
    {
        byId = hitboxes;
        for (int i = 0; i < hitboxes.Length; i++) hitboxes[i].Id = i + 1;
    }

    public static HitboxData LookupById(int id)
        => (byId != null && id >= 1 && id <= byId.Length) ? byId[id - 1] : null;

    // Geometry per frame-since-this-hitbox-spawned (0-based). Missing/null entries fall back to
    // the most recent non-null one via LookBackForBoxes, matching the MoveData.HurtboxesPerFrame
    // convention — a single-shape hitbox is just `[[oneBox]]`.
    public List<Box>[] BoxesPerFrame;

    // How many ticks this hitbox stays alive after spawn. Frame counter runs 0..ActiveDuration-1
    // inclusive; the hitbox is expired the tick after Frame reaches ActiveDuration.
    public int ActiveDuration;

    // --- Hit properties applied when this hitbox connects with a defender's hurtbox ---
    public int Damage;
    public int HitStun;
    public int BlockStun;
    public int Pushback;
    public int PushbackOnBlock;
    public int LaunchForce;
    public int LaunchForceOnBlock;

    // How long the world freezes on impact (both attacker + defender). Bigger for stronger
    // hits. Zero uses MatchManager's per-strength default (see TriggerHitPause).
    public int HitPauseDuration;

    // Strength bucket used only for the hit-pause default when HitPauseDuration is zero.
    // Not the same as the parent move's strength — different hits from the same multi-hit
    // move can feel different by using different values here.
    public MoveType Strength;

    // Block-side classification. Airborne defenders block both.
    public bool IsOverhead; // must be blocked standing
    public bool IsLow;      // must be blocked crouching

    // If true, hit sends the defender airborne (into AirHitstun instead of grounded Hitstun).
    public bool LaunchesOpponent;

    // If true, landing from the resulting air hitstun goes to hard Knockdown+Wakeup instead
    // of the default soft knockdown recovery.
    public bool CausesHardKnockdown;

    // Combo-scaling steps applied when this hit connects.
    public int DamageProrationSteps = 1;
    public int HitstunProrationSteps = 1;

    // When IsGrab is true this hitbox connects only against a defender in a grabbable state.
    public bool IsGrab;
    public int GrabSequenceDuration;
    public int DefenderSnapOffsetX; // Meant for grabs
    public int DefenderSnapOffsetY; // Meant for grabs
    public bool IsBackThrow;
}
