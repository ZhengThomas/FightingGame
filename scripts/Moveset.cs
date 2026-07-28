using System.Collections.Generic;
using System.Numerics;

public static class Moveset
{
    public static readonly AttackData StandingLight = new AttackData
    {
        AnimationName = "StandLight",
        Startup = 5,
        Active = 2,
        Recovery = 12,
        Damage = 30,
        HitStun = 12,
        BlockStun = 8,
        Pushback = 667,
        LaunchForce = 500,
        HitstunProrationSteps = 0,
        HitboxesPerFrame =
        [
            [new Box { X = 8000, Y = 3000, Width = 3000, Height = 1500 }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Light, MoveType.CrouchLight, MoveType.Medium, MoveType.CrouchMedium, MoveType.Heavy, MoveType.CrouchHeavy],
        Strength = MoveType.Light,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/StandLight.tscn", FadeFrames = 4 }
    };

    public static readonly AttackData CrouchingLight = new AttackData
    {
        AnimationName = "CrouchLight",
        Startup = 5,
        Active = 3,
        Recovery = 10,
        Damage = 25,
        HitStun = 10,
        BlockStun = 8,
        Pushback = 667,
        LaunchForce = 500,
        HitstunProrationSteps = 0,
        HitboxesPerFrame = [
            [new Box { X = 7000, Y = 9000, Width = 3000, Height = 1500 }]
        ],
        IsOverhead = false,
        IsLow = true,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Light, MoveType.CrouchLight, MoveType.Medium, MoveType.CrouchMedium, MoveType.Heavy, MoveType.CrouchHeavy],
        Strength = MoveType.CrouchLight,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/CrouchLight.tscn", FadeFrames = 4 }
    };

    public static readonly AttackData AirLight = new AttackData
    {
        AnimationName = "JumpingLight",
        Startup = 5,
        Active = 4,
        Recovery = 18,
        Damage = 25,
        HitStun = 10,
        BlockStun = 8,
        Pushback = 500,
        LaunchForce = 500,
        HitboxesPerFrame = [
            [new Box { X = 9000, Y = 17000, Width = 3500, Height = 3500 }]
        ],
        IsOverhead = true,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Light, MoveType.Medium, MoveType.Heavy, MoveType.Jump, MoveType.Dash],
        Strength = MoveType.Light,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/JumpLight.tscn", FadeFrames = 4 }
    };

    public static readonly AttackData StandingMedium = new AttackData
    {
        AnimationName = "StandMedium",
        Startup = 9,
        Active = 5,
        Recovery = 17,
        Damage = 70,
        HitStun = 18,
        BlockStun = 10,
        Pushback = 667,
        LaunchForce = 1000,
        HitboxesPerFrame =
        [
            [new Box { X = 11000, Y = 10500, Width = 11500, Height = 3500 }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Heavy, MoveType.CrouchHeavy, MoveType.CrouchMedium],
        Strength = MoveType.Medium,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/StandMedium.tscn", FadeFrames = 8 }
    };

    public static readonly AttackData CrouchingMedium = new AttackData
    {
        AnimationName = "CrouchMedium",
        Startup = 9,
        Active = 4,
        Recovery = 18,
        Damage = 60,
        HitStun = 15,
        BlockStun = 10,
        Pushback = 667,
        LaunchForce = 667,
        HitboxesPerFrame = [
            [new Box { X = 8000, Y = 3500, Width = 14500, Height = 2500 }]
        ],
        IsOverhead = false,
        IsLow = true,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Heavy, MoveType.CrouchHeavy, MoveType.Medium],
        Strength = MoveType.CrouchMedium,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/CrouchMedium.tscn", FadeFrames = 9 }
    };

    public static readonly AttackData AirMedium = new AttackData
    {
        AnimationName = "JumpingMedium",
        Startup = 9,
        Active = 3,
        Recovery = 20,
        Damage = 60,
        HitStun = 15,
        BlockStun = 14,
        Pushback = 833,
        LaunchForce = 833,
        HitboxesPerFrame = [
            [new Box { X = 0, Y = 5000, Width = 12500, Height = 7500 }]
        ],
        IsOverhead = true,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Heavy, MoveType.Jump, MoveType.Dash],
        Strength = MoveType.Medium,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/JumpMedium.tscn", FadeFrames = 10 }
    };

    public static readonly AttackData StandingHeavy = new AttackData
    {
        AnimationName = "StandHeavy",
        Startup = 11,
        Active = 3,
        Recovery = 21,
        Damage = 100,
        HitStun = 20,
        BlockStun = 180,
        Pushback = 833,
        LaunchForce = 833,
        HitboxesPerFrame =
        [
            [new Box { X = 10500, Y = 11800, Width = 12100, Height = 5500 }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.CrouchHeavy],
        Strength = MoveType.Heavy,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/StandHeavy.tscn", FadeFrames = 12 }
    };

    public static readonly AttackData CrouchingHeavy = new AttackData
    {
        AnimationName = "CrouchHeavy",
        Startup = 10,
        Active = 3,
        Recovery = 22,
        Damage = 80,
        HitStun = 30,
        BlockStun = 14,
        Pushback = 500,
        LaunchForce = 2000,
        HitboxesPerFrame = [
            [new Box { X = 5000, Y = 16000, Width = 6500, Height = 12500 }]
        ],
        HurtboxesPerFrame = [
            [new Box { X = 0, Y = -1500, Width = 5000, Height = 4500 }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = true,
        CancellableInto = [MoveType.Jump, MoveType.Heavy],
        Strength = MoveType.CrouchHeavy,
        Vfx = new VfxCue { ScenePath = "res://models/SlashVFX/CrouchHeavy.tscn", FadeFrames = 10 }
    };

    public static readonly AttackData AirHeavy = new AttackData
    {
        AnimationName = "JumpingHeavy",
        Startup = 11,
        Active = 3,
        Recovery = 22,
        Damage = 100,
        HitStun = 30,
        BlockStun = 20,
        Pushback = 667,
        LaunchForce = 667,
        PushbackOnBlock = 667,
        HitboxesPerFrame = [
            [new Box { X = 6800, Y = 7500, Width = 9000, Height = 7750 }]
        ],
        IsOverhead = true,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Jump, MoveType.Dash],
        Strength = MoveType.Heavy,
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

    public static readonly GrabData FowardGrab = new GrabData
    {
        Startup = 4,
        Active = 2,
        Recovery = 20,
        Damage = 100,
        HitPauseDuration = 1,
        HitboxesPerFrame = [
            [new Box { X = 6000, Y = 9000, Width = 2500, Height = 9000 }]
        ],
        Strength = MoveType.Grab,
        CausesHardKnockdown = true,
        GrabSequenceDuration = 65,
        DefenderSnapOffsetX = 1f,
        DefenderSnapOffsetY = 0f,
    };

    public static readonly GrabData BackGrab = new GrabData
    {
        Startup = 4,
        Active = 2,
        Recovery = 20,
        Damage = 100,
        HitPauseDuration = 1,
        HitboxesPerFrame = [
            [new Box { X = 6000, Y = 9000, Width = 2500, Height = 9000 }]
        ],
        Strength = MoveType.Grab,
        CausesHardKnockdown = true,
        GrabSequenceDuration = 65,
        DefenderSnapOffsetX = 1f,
        DefenderSnapOffsetY = 0f,
        IsBackThrow = true,
    };
}