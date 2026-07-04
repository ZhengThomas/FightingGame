using System.Collections.Generic;
using System.Numerics;

public static class Moveset
{
    public static readonly AttackData StandingLight = new AttackData
    {
        Startup = 6,
        Active = 2,
        Recovery = 12,
        Damage = 30,
        HitStun = 12,
        BlockStun = 8,
        Pushback = 4f,
        LaunchForce = 3f,
        HitboxesPerFrame =
        [
            [new Box { X = 0.6f, Y = 0.5f, Width = 0.6f, Height = 0.3f }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Light, MoveType.CrouchLight, MoveType.Medium, MoveType.CrouchMedium, MoveType.Heavy, MoveType.CrouchHeavy],
        Strength = MoveType.Light
    };

    public static readonly AttackData CrouchingLight = new AttackData
    {
        Startup = 5,
        Active = 3,
        Recovery = 10,
        Damage = 25,
        HitStun = 10,
        BlockStun = 8,
        Pushback = 4f,
        LaunchForce = 3f,
        HitboxesPerFrame = [
            [new Box { X = 0.5f, Y = -0.3f, Width = 0.6f, Height = 0.2f }]
        ],
        IsOverhead = false,
        IsLow = true,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Light, MoveType.CrouchLight, MoveType.Medium, MoveType.CrouchMedium, MoveType.Heavy, MoveType.CrouchHeavy],
        Strength = MoveType.CrouchLight
    };

    public static readonly AttackData AirLight = new AttackData
    {
        Startup = 7,
        Active = 4,
        Recovery = 18,
        Damage = 25,
        HitStun = 10,
        BlockStun = 8,
        Pushback = 3f,
        LaunchForce = 3f,
        HitboxesPerFrame = [
            [new Box { X = 0.5f, Y = -0.3f, Width = 0.6f, Height = 0.4f }]
        ],
        IsOverhead = true,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Light, MoveType.Medium, MoveType.Heavy, MoveType.Jump, MoveType.Dash],
        Strength = MoveType.Light
    };

    public static readonly AttackData StandingMedium = new AttackData
    {
        Startup = 10,
        Active = 5,
        Recovery = 17,
        Damage = 70,
        HitStun = 18,
        BlockStun = 10,
        Pushback = 4f,
        LaunchForce = 6f,
        HitboxesPerFrame =
        [
            [new Box { X = 0.6f, Y = 0.5f, Width = 2.7f, Height = 0.3f }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Heavy, MoveType.CrouchHeavy, MoveType.CrouchMedium],
        Strength = MoveType.Medium
    };

    public static readonly AttackData CrouchingMedium = new AttackData
    {
        Startup = 10,
        Active = 4,
        Recovery = 18,
        Damage = 60,
        HitStun = 15,
        BlockStun = 10,
        Pushback = 4f,
        LaunchForce = 4f,
        HitboxesPerFrame = [
            [new Box { X = 0.5f, Y = -0.3f, Width = 2.1f, Height = 0.2f }]
        ],
        IsOverhead = false,
        IsLow = true,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Heavy, MoveType.CrouchHeavy, MoveType.Medium],
        Strength = MoveType.CrouchMedium
    };

    public static readonly AttackData AirMedium = new AttackData
    {
        Startup = 9,
        Active = 3,
        Recovery = 20,
        Damage = 60,
        HitStun = 15,
        BlockStun = 14,
        Pushback = 5f,
        LaunchForce = 5f,
        HitboxesPerFrame = [
            [new Box { X = 0f, Y = -0.4f, Width = 2.4f, Height = 0.7f }]
        ],
        IsOverhead = true,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Heavy, MoveType.Jump, MoveType.Dash],
        Strength = MoveType.Medium
    };

    public static readonly AttackData StandingHeavy = new AttackData
    {
        Startup = 13,
        Active = 3,
        Recovery = 21,
        Damage = 100,
        HitStun = 20,
        BlockStun = 180,
        Pushback = 5f,
        LaunchForce = 5f,
        HitboxesPerFrame =
        [
            [new Box { X = 0.6f, Y = 0.5f, Width = 1.2f, Height = 0.6f }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.CrouchHeavy],
        Strength = MoveType.Heavy
    };

    public static readonly AttackData CrouchingHeavy = new AttackData
    {
        Startup = 13,
        Active = 3,
        Recovery = 22,
        Damage = 80,
        HitStun = 30,
        BlockStun = 14,
        Pushback = 3f,
        LaunchForce = 12f,
        LaunchForceOnBlock = 4f,
        HitboxesPerFrame = [
            [new Box { X = 0.7f, Y = 0.6f, Width = 1.3f, Height = 1.5f }]
        ],
        HurtboxesPerFrame = [
            [new Box { X = 0, Y = -0.15f, Width = 1f, Height = 0.9f }]
        ],
        IsOverhead = false,
        IsLow = false,
        LaunchesOpponent = true,
        CancellableInto = [MoveType.Jump, MoveType.Heavy],
        Strength = MoveType.CrouchHeavy
    };

    public static readonly AttackData AirHeavy = new AttackData
    {
        Startup = 11,
        Active = 3,
        Recovery = 22,
        Damage = 100,
        HitStun = 30,
        BlockStun = 20,
        Pushback = 4f,
        LaunchForce = 4f,
        PushbackOnBlock = 4f,
        LaunchForceOnBlock = 4f,
        HitboxesPerFrame = [
            [new Box { X = 1f, Y = 0f, Width = 1.6f, Height = 1.5f }]
        ],
        IsOverhead = true,
        IsLow = false,
        LaunchesOpponent = false,
        CancellableInto = [MoveType.Jump, MoveType.Dash],
        Strength = MoveType.Heavy
    };

    public static readonly GrabData FowardGrab = new GrabData
    {
        Startup = 4,
        Active = 2,
        Recovery = 20,
        Damage = 100,
        HitPauseDuration = 1,
        HitboxesPerFrame = [
            [new Box { X = 0.7f, Y = 0f, Width = 0.4f, Height = 1.5f }]
        ],
        Strength = MoveType.Grab,
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
            [new Box { X = 0.7f, Y = 0f, Width = 0.4f, Height = 1.5f }]
        ],
        Strength = MoveType.Grab,
        GrabSequenceDuration = 65,
        DefenderSnapOffsetX = 1f,
        DefenderSnapOffsetY = 0f,
        IsBackThrow = true,
    };
}