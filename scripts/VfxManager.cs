using Godot;

// Which effect to show. Order must match VfxManager.EffectScenes.
public enum VfxKind
{
    None = 0,
    DoubleJumpRing = 1,
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
    };

    // Copies of each effect available to overlap at once. Requests past this recycle the oldest.
    [Export] public int PoolPerEffect = 6;
    // Ticks a finished effect holds its slot so its id can still reject a resim replay.
    // Must exceed the longest possible rollback.
    [Export] public int IdKeepFrames = 64;

    class Slot
    {
        public FrameVfx Node;
        public int Id;          // 0 = free
        public int SpawnFrame;  // SimFrame the effect started on
        public int Facing;
    }

    Slot[][] pools;

    public override void _Ready()
    {
        Current = this;
        BuildPools();
    }

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
    }

    void BuildPools()
    {
        pools = new Slot[EffectScenes.Length][];

        for (int kind = 0; kind < EffectScenes.Length; kind++)
        {
            string path = EffectScenes[kind];
            pools[kind] = new Slot[0];
            if (string.IsNullOrEmpty(path)) continue;

            PackedScene packed = GD.Load<PackedScene>(path);
            if (packed == null)
            {
                GD.PushWarning($"VfxManager: could not load '{path}'.");
                continue;
            }

            var slots = new Slot[PoolPerEffect];
            for (int i = 0; i < PoolPerEffect; i++)
            {
                FrameVfx node = packed.Instantiate() as FrameVfx;
                if (node == null)
                {
                    GD.PushWarning($"VfxManager: root of '{path}' is not a FrameVfx.");
                    break;
                }
                AddChild(node);
                node.SetProgress(-1f);
                slots[i] = new Slot { Node = node };
            }
            pools[kind] = slots;
        }
    }

    // Called from the sim the tick an effect should start. Idempotent: the same id twice is ignored,
    // which is what makes it safe to call from inside a rollback resim.
    //
    // simX/simY are sim-space (PhysicsScale) coords; the effect stays there for its whole life.
    public void Request(VfxKind kind, int id, int simX, int simY, int facing = 1)
    {
        if (pools == null || id == 0) return;

        int k = (int)kind;
        if (k <= 0 || k >= pools.Length) return;

        Slot[] slots = pools[k];
        if (slots == null || slots.Length == 0) return;

        // Already showing (or still holding its id) — a replayed tick lands here and does nothing.
        foreach (Slot s in slots)
            if (s != null && s.Id == id) return;

        int now = MatchManager.Current?.SimFrame ?? 0;

        // Claim in order of least damage: a free slot, then one that has finished playing and is
        // only holding its id, and only then one that's still visible. Taking a slot discards the
        // id it held, so preferring finished ones keeps live effects' dedup protection intact.
        Slot target = null;
        int targetRank = int.MaxValue;
        foreach (Slot s in slots)
        {
            if (s?.Node == null) continue;

            int rank = s.Id == 0 ? 0
                     : (now - s.SpawnFrame >= s.Node.DurationFrames ? 1 : 2);

            if (rank < targetRank || (rank == targetRank && target != null && s.SpawnFrame < target.SpawnFrame))
            {
                target = s;
                targetRank = rank;
            }
            if (targetRank == 0) break;
        }
        if (target == null) return;

        target.Id = id;
        target.SpawnFrame = now;
        target.Facing = facing >= 0 ? 1 : -1;

        const float scale = PlayerConstants.PhysicsScale;
        target.Node.GlobalPosition = new Vector3(simX / scale, simY / scale, GlobalPosition.Z);
        target.Node.SetProgress(0f, target.Facing);
    }

    // Drops every live effect. Called on round reset so last round's effects don't linger.
    public void Reset()
    {
        if (pools == null) return;

        foreach (Slot[] slots in pools)
            foreach (Slot s in slots ?? System.Array.Empty<Slot>())
            {
                if (s?.Node == null) continue;
                s.Id = 0;
                s.Node.SetProgress(-1f);
            }
    }

    public override void _Process(double delta)
    {
        if (pools == null) return;

        int sim = MatchManager.Current?.SimFrame ?? 0;

        foreach (Slot[] slots in pools)
            foreach (Slot s in slots ?? System.Array.Empty<Slot>())
            {
                if (s?.Node == null || s.Id == 0) continue;

                int frame = sim - s.SpawnFrame;
                int duration = s.Node.DurationFrames;

                if (frame < 0)
                {
                    // Rolled back past this effect's start. Keep the slot: if the event really did
                    // happen, the resim re-requests the same id and it picks up where it was.
                    s.Node.SetProgress(-1f);
                }
                else if (frame < duration)
                {
                    s.Node.SetProgress((float)frame / duration, s.Facing);
                }
                else
                {
                    s.Node.SetProgress(-1f);
                    if (frame >= duration + IdKeepFrames) s.Id = 0;  // id no longer needed for dedup
                }
            }
    }
}
