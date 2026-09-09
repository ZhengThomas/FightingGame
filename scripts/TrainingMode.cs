using Godot;

// Everything that makes training training: nobody dies, and a key restarts the round in whichever
// situation you're drilling.
//
// A plain class rather than a subclass of MatchManager. MatchManager is a script on a node in the
// fighting scene, so swapping in a subclass would mean a second scene or runtime script juggling —
// and overriding its Tick would put the snapshot ordering in play for no benefit. All this needs is
// a hook once a frame and one rule, both of which MatchManager exposes plainly.
public class TrainingMode : IGameMode
{
    // Situations worth practising from. The plain corners put player 1 against the wall; holding up
    // as well corners the opponent there instead.
    enum Start { Default, LeftCorner, RightCorner, TheirLeftCorner, TheirRightCorner, Swapped }

    // Sim units between the cornered player and the other one.
    const int CornerGap = 8000;

    // How long after they're back in control before health starts coming back, and how fast.
    const int RegenDelay = 15;
    const int RegenSpeed = 16;

    readonly MatchManager match;

    // What a pause menu edits. Read fresh every frame, so changing one takes effect immediately.
    public readonly TrainingSettings Settings = new TrainingSettings();

    // What the last reset used. Holding nothing repeats it, so drilling the same setup doesn't mean
    // holding a direction every single time.
    Start start = Start.Default;

    // Frames each player has been back in control for.
    int p1Free;
    int p2Free;

    public TrainingMode(MatchManager match)
    {
        this.match = match;
        // Nothing is ever Defeated, so the KO phase never fires and the round never ends.
        match.HealthFloor = 1;
        // Resets already skip the count-in; the first round shouldn't be the odd one out.
        match.SkipCountdown();
    }

    // Once per real frame, before the sim ticks. Deliberately not inside Tick: a resim runs that
    // many times over, and reading the keyboard there would let the rollback self-test trip a reset
    // halfway through a replay.
    public void Step()
    {
        Regenerate(match.Player1, ref p1Free);
        Regenerate(match.Player2, ref p2Free);

        if (!Input.IsActionJustPressed("training_reset")) return;

        // Up combines, so it's checked before the plain directions.
        InputFrame held = match.Player1 != null
            ? match.Player1.InputLog.InputAt(match.FrameCount)
            : default;

        if (held.Up && held.Left) start = Start.TheirLeftCorner;
        else if (held.Up && held.Right) start = Start.TheirRightCorner;
        else if (held.Left) start = Start.LeftCorner;
        else if (held.Right) start = Start.RightCorner;
        else if (held.Down) start = Start.Swapped;
        else if (held.Up) start = Start.Default;

        match.RestartRound();
        Place();
    }

    // Damage sticks around long enough to read before it fills back in. Anything that took them out
    // of their own hands — hit, blocking, knocked down, grabbed — restarts the wait, so a combo's
    // damage doesn't heal away underneath it.
    void Regenerate(Player player, ref int free)
    {
        if (player == null) return;

        if (!player.InControl)
        {
            free = 0;
            return;
        }

        free++;
        if (!Settings.HealthRegen || free < RegenDelay) return;

        if (player.Health < Player.MaxHealth)
            player.Health = System.Math.Min(Player.MaxHealth,
                                            player.Health + RegenSpeed);
    }

    void Place()
    {
        int wall = PlayerConstants.MaxDistanceFromCenter;
        (int p1x, int p2x) = start switch
        {
            Start.LeftCorner => (-wall, -wall + CornerGap),
            Start.RightCorner => (wall, wall - CornerGap),
            Start.TheirLeftCorner => (-wall + CornerGap, -wall),
            Start.TheirRightCorner => (wall - CornerGap, wall),
            Start.Swapped => (match.StartX(2), match.StartX(1)),
            _ => (match.StartX(1), match.StartX(2)),
        };

        match.PlacePlayers(p1x, p2x);
    }
}
