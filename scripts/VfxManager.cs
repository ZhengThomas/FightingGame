using Godot;
using System.Collections.Generic;

// Which effect to show. Order must match VfxManager.EffectScenes.
public enum VfxKind
{
    None = 0,
    JumpRing = 1,
    DashCloud = 2,
    DashCloudBackwards = 3,
    AirdashForward = 4,
    AirdashBackward = 5,
    HitImpactLight = 6,
    HitImpactMedium = 7,
    HitImpactHeavy = 8,
    BlockImpact = 9,
}

// Owns every one-shot visual effect: when it starts, where it sits, and when it's done.
//
// Effects are EVENTS, not state. The sim calls Request() at the moment something happens and then
// forgets about it. Nothing about a live effect is snapshotted, so a rollback never rewinds one.
//
// Three things make that safe:
//
//   Dedup. A rollback replays the same tick, so Request() gets called again for an event already
//   showing. Every request carries an id built from frame + player (same convention as hitbox
//   InstanceId), and a request whose id is already live is ignored. So a replayed tick is a no-op
//   instead of a duplicate ring.
//
//   Id retention. A finished effect keeps its slot, invisible, for IdKeepFrames afterwards, so its
//   id is still on file to reject a replay. This has to outlast the furthest the game can rewind
//   (MatchManager.StateHistorySize), or a late resim could spawn a duplicate of an effect that has
//   already ended.
//
//   Pooling. Instances are made once at load and reused, so nothing is created or freed while the
//   match runs. That's what lets several copies of one effect overlap: each takes its own slot.
//
// Progress runs off MatchManager.SimFrame, which stalls during hit pause, so effects freeze with the
// game without knowing anything about hit pause.
public partial class VfxManager : Node3D
{
    public static VfxManager Current;

    // Indexed by (int)VfxKind — element 0 is VfxKind.None and stays empty.
    [Export] public string[] EffectScenes =
    {
        "",
        "res://sprites/vfx/double_jump_ring.tscn",
        "res://sprites/vfx/Dash Cloud.tscn",
        "res://sprites/vfx/Dash CloudBackwards.tscn",
        "res://sprites/vfx/Airdash forwards.tscn",
        "res://sprites/vfx/Airdash Backwards.tscn",
        "res://sprites/vfx/hit_impactLight.tscn",
        "res://sprites/vfx/hit_impactSliceMedium.tscn",
        "res://sprites/vfx/hit_impactSliceHeavy.tscn",
        "res://sprites/vfx/block_impact.tscn",
    };

    // Ceiling on how many copies of one effect can exist. Pools start empty and grow only when a
    // request finds nothing free, so each effect settles at its own peak and allocates no further.
    // Only a runaway request rate should ever reach this.
    [Export] public int MaxPerEffect = 12;
    // Ticks a finished effect holds its slot so its id can still reject a resim replay.
    // Must exceed the longest possible rollback.
    [Export] public int IdKeepFrames = 64;

    class Slot
    {
        public FrameVfx Node;
        public int Id;          // 0 = free
        public int SpawnFrame;  // SimFrame the effect started on
        public int Facing;
        public int OwnerNumber; // player that requested it, for FollowSpawner effects
    }

    List<Slot>[] pools;
    PackedScene[] loaded;

    public override void _Ready()
    {
        Current = this;
        BuildPools();
    }

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
    }

    // Loads the scenes but instances nothing. Effects are created on first use by Grow().
    void BuildPools()
    {
        pools = new List<Slot>[EffectScenes.Length];
        loaded = new PackedScene[EffectScenes.Length];

        for (int kind = 0; kind < EffectScenes.Length; kind++)
        {
            pools[kind] = new List<Slot>();

            string path = EffectScenes[kind];
            if (string.IsNullOrEmpty(path)) continue;

            loaded[kind] = GD.Load<PackedScene>(path);
            if (loaded[kind] == null)
                GD.PushWarning($"VfxManager: could not load '{path}'.");
        }
    }

    // Adds one more copy of an effect, or returns null at the ceiling.
    Slot Grow(int kind)
    {
        if (pools[kind].Count >= MaxPerEffect || loaded[kind] == null) return null;

        FrameVfx node = loaded[kind].Instantiate() as FrameVfx;
        if (node == null)
        {
            GD.PushWarning($"VfxManager: root of '{EffectScenes[kind]}' is not a FrameVfx.");
            loaded[kind] = null;   // don't retry a scene that can't work
            return null;
        }

        AddChild(node);
        node.SetFrame(-1);

        var slot = new Slot { Node = node };
        pools[kind].Add(slot);
        return slot;
    }

    // Called from the sim the tick an effect should start. Idempotent: the same id twice is ignored,
    // which is what makes it safe to call from inside a rollback resim.
    //
    // simX/simY are sim-space (PhysicsScale) coords; the effect stays there for its whole life.
    public void Request(VfxKind kind, int id, int simX, int simY, int facing = 1, int ownerNumber = 0)
    {
        if (pools == null || id == 0) return;

        int k = (int)kind;
        if (k <= 0 || k >= pools.Length) return;

        List<Slot> slots = pools[k];

        // Already showing (or still holding its id) — a replayed tick lands here and does nothing.
        foreach (Slot s in slots)
            if (s.Id == id) return;

        // A free slot costs nothing, so take one if there is one.
        Slot target = null;
        foreach (Slot s in slots)
            if (s.Id == 0) { target = s; break; }

        // Otherwise make a new copy rather than evict: every occupied slot is still holding an id a
        // resim might replay, and taking one throws that id away.
        target ??= Grow(k);

        // At the ceiling. Evict the longest-finished slot, or failing that the longest-running one.
        if (target == null)
        {
            int targetRank = int.MaxValue;
            foreach (Slot s in slots)
            {
                int rank = ClockFor(s.Node) - s.SpawnFrame >= s.Node.TotalDurationFrames ? 0 : 1;
                if (rank < targetRank || (rank == targetRank && target != null && s.SpawnFrame < target.SpawnFrame))
                {
                    target = s;
                    targetRank = rank;
                }
            }
        }
        if (target?.Node == null) return;

        target.Id = id;
        target.SpawnFrame = ClockFor(target.Node);
        target.Facing = facing >= 0 ? 1 : -1;
        target.OwnerNumber = ownerNumber;

        PlaceAt(target.Node, simX, simY);
        target.Node.SetSpawnSeed(id);   // before the first frame, so it spawns already turned
        target.Node.SetFrame(0, target.Facing);
    }

    // SimFrame freezes during hit pause; FrameCount never does. An effect's whole life — its spawn
    // stamp and every progress read — has to use one or the other consistently.
    static int ClockFor(FrameVfx node)
    {
        MatchManager m = MatchManager.Current;
        if (m == null) return 0;
        return node != null && node.IgnoreHitPause ? m.FrameCount : m.SimFrame;
    }

    void PlaceAt(FrameVfx node, int simX, int simY)
    {
        const float scale = PlayerConstants.PhysicsScale;
        node.GlobalPosition = new Vector3(simX / scale, simY / scale, GlobalPosition.Z);
    }

    // Drops every live effect. Called on round reset so last round's effects don't linger.
    public void Reset()
    {
        if (pools == null) return;

        foreach (List<Slot> slots in pools)
            foreach (Slot s in slots)
            {
                s.Id = 0;
                s.Node.SetFrame(-1);
            }
    }

    public override void _Process(double delta)
    {
        if (pools == null) return;

        foreach (List<Slot> slots in pools)
            foreach (Slot s in slots)
            {
                if (s.Id == 0) continue;

                int frame = ClockFor(s.Node) - s.SpawnFrame;
                int duration = s.Node.TotalDurationFrames;

                // Re-read the owner's position every frame rather than trusting the spawn point.
                // After a rollback the player is already corrected, so this needs no history.
                if (s.Node.FollowSpawner && frame >= 0 && frame < duration)
                {
                    Player owner = MatchManager.Current?.PlayerFromNumber(s.OwnerNumber);
                    if (owner != null) PlaceAt(s.Node, owner.SimX, owner.SimY);
                }

                if (frame < 0)
                {
                    // Rolled back past this effect's start. Keep the slot: if the event really did
                    // happen, the resim re-requests the same id and it picks up where it was.
                    s.Node.SetFrame(-1);
                }
                else if (frame < duration)
                {
                    s.Node.SetFrame(frame, s.Facing);
                }
                else
                {
                    s.Node.SetFrame(-1);
                    if (frame >= duration + IdKeepFrames) s.Id = 0;  // id no longer needed for dedup
                }
            }
    }
}
