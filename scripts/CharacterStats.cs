// Per-character tunable stats.
//
// These are the values that can differ between characters (movement feel, jump arcs,
// gravity, dash timings, etc.). A new character overrides Player.CreateStats() and tweaks
// only the values it cares about, leaving the rest at the defaults:
//
//   protected override CharacterStats CreateStats() =>
//       CharacterStats.Default with { Speed = 3f, JumpForce = 12f };
public record struct CharacterStats
{
    // Ground movement
    public float Speed;
    public float DashSpeed;
    public float DecelerationSpeed;
    public float StrongDecelerationSpeed;
    public float StrongDecelerationThreshold;

    // Jumping
    public float JumpForce;
    public float DoubleJumpForce;
    public float WalkJumpSpeed;
    public float DashJumpSpeed;
    public int JumpSquatDuration;
    public int LandingDuration;
    public int FramesUntilActionableAfterJump;

    // Gravity / airborne
    public float RisingGravity;
    public float FloatingGravity;
    public float ComboFloatingGravity;
    public float FallingGravity;
    public float FloatingGravityThreshold;
    public float MaxFallSpeed;

    // Hit reactions (gravity in hitstun + air-hit deceleration)
    public float HitstunRisingGravity;
    public float HitstunFloatingGravity;
    public float HitstunFallingGravity;
    public float GroundedHitDecelerationSpeed;
    public float AirHitDecelerationSpeed;
    public float AirHitMinimumSpeed;

    // Back dash / air dash
    public float BackDashSpeed;
    public int BackDashStartDuration;
    public int BackDashRecoverDuration;
    public int BackDashDuration;
    public float AirDashSpeed;
    public int AirDashStartup;
    public int AirDashDuration;
    public int DashCooldown; // frames after leaving a ground dash / backdash before you can dash again
    public int MinDashDuration; // minimum frames a ground dash must run before it can return to walk/idle

    // Knockdown / wakeup
    public int KnockdownDuration;
    public int WakeupDuration;

    // The baseline character. Matches the values that used to live in PlayerConstants.
    public static CharacterStats Default => new CharacterStats
    {
        Speed = 2f,
        DashSpeed = 7f,
        DecelerationSpeed = 0.5f,
        StrongDecelerationSpeed = 1.0f,
        StrongDecelerationThreshold = 2.5f,

        JumpForce = 15f,
        DoubleJumpForce = 11f,
        WalkJumpSpeed = 2.3f,
        DashJumpSpeed = 4.5f,
        JumpSquatDuration = 4,
        LandingDuration = 3,
        FramesUntilActionableAfterJump = 7,

        RisingGravity = 0.9f,
        FloatingGravity = 0.25f,
        ComboFloatingGravity = 0.08f,
        FallingGravity = 1.2f,
        FloatingGravityThreshold = 1.4f,
        MaxFallSpeed = -11.0f,

        HitstunRisingGravity = 0.6f,
        HitstunFloatingGravity = 0.15f,
        HitstunFallingGravity = 1.5f,
        GroundedHitDecelerationSpeed = 0.6f,
        AirHitDecelerationSpeed = 0.6f,
        AirHitMinimumSpeed = 1f,

        BackDashSpeed = 8f,
        BackDashStartDuration = 3,
        BackDashRecoverDuration = 10,
        BackDashDuration = 7,
        AirDashSpeed = 8f,
        AirDashStartup = 3,
        AirDashDuration = 7,
        DashCooldown = 8,
        MinDashDuration = 12,

        KnockdownDuration = 30,
        WakeupDuration = 25,
    };
}
