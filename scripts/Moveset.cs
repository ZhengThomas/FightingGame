using System.Collections.Generic;

public static class Moveset
{
    // Registers every move + hitbox with the id-based registries. Order determines the stable
    // id assigned to each entry — appending is safe, reordering desyncs any in-flight
    // ActiveMoveState / HitboxInstance. Runs once at type init.
    static Moveset()
    {
        MoveData.Register(
            StandingLight, CrouchingLight, AirLight,
            StandingMedium, CrouchingMedium, AirMedium,
            StandingHeavy, CrouchingHeavy, AirHeavy,
            FowardGrab, BackGrab
        );
        HitboxData.Register(
            StandingLightHitbox, CrouchingLightHitbox, AirLightHitbox,
            StandingMediumHitbox, CrouchingMediumHitbox, AirMediumHitbox,
            StandingHeavyHitbox, CrouchingHeavyHitbox, AirHeavyHitbox,
            FowardGrabHitbox, BackGrabHitbox
        );
    }

    // ------------------------------ HITBOXES ------------------------------
    // Each hit-dealing hitbox is defined here as its own HitboxData. Moves spawn these via
    // their HitboxSpawns list at specific move-frame numbers. Multi-hit moves would list
    // multiple HitboxSpawns pointing at different HitboxData entries.

    public static readonly HitboxData StandingLightHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 8000, Y = 3000, Width = 3000, Height = 1500 }]],
        ActiveDuration = 2,
        Damage = 30, HitStun = 12, BlockStun = 8, Pushback = 667, LaunchForce = 500,
        Strength = MoveType.Light,
        HitstunProrationSteps = 0,
    };

    public static readonly HitboxData CrouchingLightHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 7000, Y = 9000, Width = 3000, Height = 1500 }]],
        ActiveDuration = 3,
        Damage = 25, HitStun = 10, BlockStun = 8, Pushback = 667, LaunchForce = 500,
        Strength = MoveType.Light, IsLow = true,
        HitstunProrationSteps = 0,
    };

    public static readonly HitboxData AirLightHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 9000, Y = 17000, Width = 3500, Height = 3500 }]],
        ActiveDuration = 4,
        Damage = 25, HitStun = 10, BlockStun = 8, Pushback = 500, LaunchForce = 500,
        Strength = MoveType.Light, IsOverhead = true,
    };

    public static readonly HitboxData StandingMediumHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 11000, Y = 10500, Width = 11500, Height = 3500 }]],
        ActiveDuration = 5,
        Damage = 70, HitStun = 18, BlockStun = 10, Pushback = 667, LaunchForce = 1000,
        Strength = MoveType.Medium,
    };

    public static readonly HitboxData CrouchingMediumHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 8000, Y = 3500, Width = 14500, Height = 2500 }]],
        ActiveDuration = 4,
        Damage = 60, HitStun = 15, BlockStun = 10, Pushback = 667, LaunchForce = 667,
        Strength = MoveType.Medium, IsLow = true,
    };

    public static readonly HitboxData AirMediumHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 0, Y = 5000, Width = 12500, Height = 7500 }]],
        ActiveDuration = 3,
        Damage = 60, HitStun = 15, BlockStun = 14, Pushback = 833, LaunchForce = 833,
        Strength = MoveType.Medium, IsOverhead = true,
    };

    public static readonly HitboxData StandingHeavyHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 10500, Y = 11800, Width = 12100, Height = 5500 }]],
        ActiveDuration = 3,
        Damage = 100, HitStun = 20, BlockStun = 180, Pushback = 833, LaunchForce = 833,
        Strength = MoveType.Heavy,
    };

    public static readonly HitboxData CrouchingHeavyHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 5000, Y = 16000, Width = 6500, Height = 12500 }]],
        ActiveDuration = 3,
        Damage = 80, HitStun = 30, BlockStun = 14, Pushback = 500, LaunchForce = 2000,
        Strength = MoveType.Heavy, LaunchesOpponent = true,
    };

    public static readonly HitboxData AirHeavyHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 6800, Y = 7500, Width = 9000, Height = 7750 }]],
        ActiveDuration = 3,
        Damage = 100, HitStun = 30, BlockStun = 20, Pushback = 667, PushbackOnBlock = 667, LaunchForce = 667,
        Strength = MoveType.Heavy, IsOverhead = true,
    };

    // ------------------------------ MOVES ------------------------------
    // Each move controls the animation + state machine + cancels, and spawns one or more
    // hitboxes at scheduled frames of its active window. Single-hit moves list one spawn;
    // multi-hit moves would list multiple; projectiles would use a HitboxData with a long
    // ActiveDuration so it outlives the move's recovery.

    public static readonly AttackData StandingLight = new AttackData
    {
        AnimationName = "StandLight",
        Startup = 5, Active = 2, Recovery = 12,
        Strength = MoveType.Light,
        HitboxSpawns = [new HitboxSpawn { Frame = 6, Hitbox = StandingLightHitbox }],
        CancellableInto = [MoveType.Light, MoveType.CrouchLight, MoveType.Medium, MoveType.CrouchMedium, MoveType.Heavy, MoveType.CrouchHeavy],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/StandLight.tscn", FadeFrames = 4 }
    };

    public static readonly AttackData CrouchingLight = new AttackData
    {
        AnimationName = "CrouchLight",
        Startup = 5, Active = 3, Recovery = 10,
        Strength = MoveType.CrouchLight,
        HitboxSpawns = [new HitboxSpawn { Frame = 6, Hitbox = CrouchingLightHitbox }],
        CancellableInto = [MoveType.Light, MoveType.CrouchLight, MoveType.Medium, MoveType.CrouchMedium, MoveType.Heavy, MoveType.CrouchHeavy],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/CrouchLight.tscn", FadeFrames = 4 }
    };

    public static readonly AttackData AirLight = new AttackData
    {
        AnimationName = "JumpingLight",
        Startup = 5, Active = 4, Recovery = 18,
        Strength = MoveType.Light,
        HitboxSpawns = [new HitboxSpawn { Frame = 6, Hitbox = AirLightHitbox }],
        CancellableInto = [MoveType.Light, MoveType.Medium, MoveType.Heavy, MoveType.Jump, MoveType.Dash],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/JumpLight.tscn", FadeFrames = 4 }
    };

    public static readonly AttackData StandingMedium = new AttackData
    {
        AnimationName = "StandMedium",
        Startup = 9, Active = 5, Recovery = 17,
        Strength = MoveType.Medium,
        HitboxSpawns = [new HitboxSpawn { Frame = 10, Hitbox = StandingMediumHitbox }],
        CancellableInto = [MoveType.Heavy, MoveType.CrouchHeavy, MoveType.CrouchMedium],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/StandMedium.tscn", FadeFrames = 8 }
    };

    public static readonly AttackData CrouchingMedium = new AttackData
    {
        AnimationName = "CrouchMedium",
        Startup = 9, Active = 4, Recovery = 18,
        Strength = MoveType.CrouchMedium,
        HitboxSpawns = [new HitboxSpawn { Frame = 10, Hitbox = CrouchingMediumHitbox }],
        CancellableInto = [MoveType.Heavy, MoveType.CrouchHeavy, MoveType.Medium],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/CrouchMedium.tscn", FadeFrames = 9 }
    };

    public static readonly AttackData AirMedium = new AttackData
    {
        AnimationName = "JumpingMedium",
        Startup = 9, Active = 3, Recovery = 20,
        Strength = MoveType.Medium,
        HitboxSpawns = [new HitboxSpawn { Frame = 10, Hitbox = AirMediumHitbox }],
        CancellableInto = [MoveType.Heavy, MoveType.Jump, MoveType.Dash],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/JumpMedium.tscn", FadeFrames = 10 }
    };

    public static readonly AttackData StandingHeavy = new AttackData
    {
        AnimationName = "StandHeavy",
        Startup = 11, Active = 3, Recovery = 21,
        Strength = MoveType.Heavy,
        HitboxSpawns = [new HitboxSpawn { Frame = 12, Hitbox = StandingHeavyHitbox }],
        CancellableInto = [MoveType.CrouchHeavy],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/StandHeavy.tscn", FadeFrames = 12 }
    };

    public static readonly AttackData CrouchingHeavy = new AttackData
    {
        AnimationName = "CrouchHeavy",
        Startup = 10, Active = 3, Recovery = 22,
        Strength = MoveType.CrouchHeavy,
        HitboxSpawns = [new HitboxSpawn { Frame = 11, Hitbox = CrouchingHeavyHitbox }],
        HurtboxesPerFrame = [[new Box { X = 0, Y = -1500, Width = 5000, Height = 4500 }]],
        CancellableInto = [MoveType.Jump, MoveType.Heavy],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/CrouchHeavy.tscn", FadeFrames = 10 }
    };

    public static readonly AttackData AirHeavy = new AttackData
    {
        AnimationName = "JumpingHeavy",
        Startup = 11, Active = 3, Recovery = 22,
        Strength = MoveType.Heavy,
        HitboxSpawns = [new HitboxSpawn { Frame = 12, Hitbox = AirHeavyHitbox }],
        CancellableInto = [MoveType.Jump, MoveType.Dash],
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/JumpHeavy.tscn", FadeFrames = 10 }
    };

    // All normal attacks, for lookups (e.g. the hitbox-lab slash preview matching an animation clip).
    private static readonly AttackData[] AllAttacks =
    {
        StandingLight, CrouchingLight, AirLight,
        StandingMedium, CrouchingMedium, AirMedium,
        StandingHeavy, CrouchingHeavy, AirHeavy,
    };

    // Returns the attack whose AnimationName matches, or null if none.
    public static AttackData FindByAnimationName(string animationName)
    {
        if (string.IsNullOrEmpty(animationName)) return null;
        foreach (AttackData a in AllAttacks)
            if (a.AnimationName == animationName) return a;
        return null;
    }

    // Grabs are just moves that spawn a grab-flagged HitboxData. 

    public static readonly HitboxData FowardGrabHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 6000, Y = 9000, Width = 2500, Height = 9000 }]],
        ActiveDuration = 2,
        Strength = MoveType.Grab,
        HitPauseDuration = 1,
        CausesHardKnockdown = true,
        IsGrab = true,
        GrabSequenceDuration = 65,
        DefenderSnapOffsetX = 10000, DefenderSnapOffsetY = 0,
    };

    public static readonly HitboxData BackGrabHitbox = new HitboxData
    {
        BoxesPerFrame = [[new Box { X = 6000, Y = 9000, Width = 2500, Height = 9000 }]],
        ActiveDuration = 2,
        Strength = MoveType.Grab,
        HitPauseDuration = 1,
        CausesHardKnockdown = true,
        IsGrab = true,
        IsBackThrow = true,
        GrabSequenceDuration = 65,
        DefenderSnapOffsetX = 10000, DefenderSnapOffsetY = 0,
    };

    public static readonly MoveData FowardGrab = new MoveData
    {
        Startup = 4, Active = 2, Recovery = 20,
        Strength = MoveType.Grab,
        HitboxSpawns = [new HitboxSpawn { Frame = 5, Hitbox = FowardGrabHitbox }],
    };

    public static readonly MoveData BackGrab = new MoveData
    {
        Startup = 4, Active = 2, Recovery = 20,
        Strength = MoveType.Grab,
        HitboxSpawns = [new HitboxSpawn { Frame = 5, Hitbox = BackGrabHitbox }],
    };
}
