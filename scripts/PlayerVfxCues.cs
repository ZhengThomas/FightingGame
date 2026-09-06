// Effects with no single moment to hang off — ongoing or animation-driven ones, like dust while
// running. An effect that has a natural trigger point instead calls Player.RequestVfx from there
// (see Jump), which needs no condition and can't drift out of sync with the code it describes.
//
// Runs once per sim tick, right after the animator steps, so it can key off AnimState. Per-tick
// matters: a rollback resim advances many ticks inside one rendered frame, so anything checking on
// render frames would miss the ones in between.
//
// Nothing here touches the simulation. Re-running a tick re-requests the same effect, and
// VfxManager throws away the duplicate.
public static class PlayerVfxCues
{
    // Ticks between dust puffs while running.
    const int DustEveryFrames = 15;

    public static void Step(Player p)
    {
        AnimatorState a = p.Anim;

        // First frame of the run, then on a cadence once the run proper is under way. Run frame 0
        // is skipped so it doesn't puff twice in the six ticks RunStart takes.
        if (a.Current == AnimState.RunStart && a.Frame == 0)
            p.RequestVfx(VfxKind.DashCloud);
    }
}
