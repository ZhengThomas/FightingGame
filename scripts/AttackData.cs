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

// Scheduled hitbox spawn point on a move. 
public struct HitboxSpawn
{
    public int Frame;
    public HitboxData Hitbox;
}

// Base class for all move data. Contains the frame structure and hitbox-spawn schedule
// shared by every move type.
public class MoveData
{
    // Stable numeric identity for this move, assigned once at startup by MoveData.Register.
    // 0 = unregistered, which ActiveMoveState reads as "no move".
    public int Id { get; internal set; }

    static MoveData[] byId;

    // Called once during static init from Moveset. Ids are 1-based so that a zeroed
    // ActiveMoveState / PlayerSnapshot can't masquerade as the move registered first.
    public static void Register(params MoveData[] moves)
    {
        byId = moves;
        for (int i = 0; i < moves.Length; i++)
            moves[i].Id = i + 1;
    }

    public static MoveData LookupById(int id)
        => (byId != null && id >= 1 && id <= byId.Length) ? byId[id - 1] : null;

    // Name of the AnimationPlayer clip this move plays.
    public string AnimationName = "";

    public int Startup;      // frames before active
    public int Active;       // frames hitbox is out
    public int Recovery;     // frames after active before the player can act

    public MoveType Strength;

    // When during the move to spawn each hitbox 
    public HitboxSpawn[] HitboxSpawns;

    // per-frame hurtbox overrides (0-based over startup+active+recovery); null entry = use default pose
    public List<Box>[] HurtboxesPerFrame;

    // slash VFX to fade in/out over this move's frames (default ScenePath = "" = none)
    public VfxCue Vfx;
}

// Data for a strike move — any move that can be hit-cancelled into other moves. Grabs use
// the base MoveData directly.
public class AttackData : MoveData
{
    public List<MoveType> CancellableInto;
}
