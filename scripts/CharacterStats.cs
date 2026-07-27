// Per-character tunable stats.
//
// Physics values are fixed-point ints: world_float * PlayerConstants.PhysicsScale (10000).
// Sim math uses these directly (no /10000). Only Godot Node3D.Position divides for rendering.
//
// A new character overrides Player.CreateStats() and tweaks only what it cares about:
//
//   protected override CharacterStats CreateStats() =>
//       CharacterStats.Default with { Speed = 500, JumpForce = 3000 };
public record struct CharacterStats
{
    // Ground movement (physics units per frame)
    public int Speed;
    public int DashSpeed;
    public int DecelerationSpeed;
    public int StrongDecelerationSpeed;
    public int StrongDecelerationThreshold;

    // Jumping
    public int JumpForce;
    public int DoubleJumpForce;
    public int WalkJumpSpeed;
    public int DashJumpSpeed;
    public int JumpSquatDuration;
    public int LandingDuration;
    public int FramesUntilActionableAfterJump;

    // Gravity / airborne
    public int RisingGravity;
    public int FloatingGravity;
    public int ComboFloatingGravity;
    public int FallingGravity;
    public int FloatingGravityThreshold;
    public int MaxFallSpeed;

    // Hit reactions (gravity in hitstun + air-hit deceleration)
    public int HitstunRisingGravity;
    public int HitstunFloatingGravity;
    public int HitstunFallingGravity;
    public int GroundedHitDecelerationSpeed;
    public int AirHitDecelerationSpeed;
    public int AirHitMinimumSpeed;

    // Back dash / air dash
    public int BackDashSpeed;
    public int BackDashStartDuration;
    public int BackDashRecoverDuration;
    public int BackDashDuration;
    public int AirDashSpeed;
    public int AirDashFinishSpeed;
    public int AirDashStartup;
    public int AirDashDuration;
    public int AirDashRecovery;
    public int DashCooldown; // frames after leaving a ground dash / backdash before you can dash again
    public int MinDashDuration; // minimum frames a ground dash must run before it can return to walk/idle

    // Knockdown / wakeup
    public int SoftKnockdownDuration; // short grounded recover after a normal air hit lands
    public int SoftKnockdownSlideSpeed; // constant retreat speed while soft-knocked down
    public int KnockdownDuration;     // hard knockdown lie-down
    public int WakeupDuration;        // hard knockdown get-up (invulnerable)

    // Baseline character. Values are prior per-frame floats * PhysicsScale (10000).
    public static CharacterStats Default => new CharacterStats
    {
        Speed = 330,
        DashSpeed = 1170,
        DecelerationSpeed = 80,
        StrongDecelerationSpeed = 160,
        StrongDecelerationThreshold = 410,

        JumpForce = 2500,
        DoubleJumpForce = 2000,
        WalkJumpSpeed = 380,
        DashJumpSpeed = 750,
        JumpSquatDuration = 4,
        LandingDuration = 3,
        FramesUntilActionableAfterJump = 7,

        RisingGravity = 150,
        FloatingGravity = 40,
        ComboFloatingGravity = 10,
        FallingGravity = 200,
        FloatingGravityThreshold = 230,
        MaxFallSpeed = -1830,

        HitstunRisingGravity = 100,
        HitstunFloatingGravity = 30,
        HitstunFallingGravity = 250,
        GroundedHitDecelerationSpeed = 100,
        AirHitDecelerationSpeed = 100,
        AirHitMinimumSpeed = 170,

        BackDashSpeed = 1330,
        BackDashStartDuration = 3,
        BackDashRecoverDuration = 10,
        BackDashDuration = 7,
        AirDashSpeed = 2000,
        AirDashFinishSpeed = 830,
        AirDashStartup = 5,
        AirDashDuration = 7,
        AirDashRecovery = 3,
        DashCooldown = 8,
        MinDashDuration = 12,

        SoftKnockdownDuration = 20,
        SoftKnockdownSlideSpeed = 830,
        KnockdownDuration = 30,
        WakeupDuration = 25,
    };
}
