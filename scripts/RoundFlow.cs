// Where a round is up to.
public enum RoundPhase
{
    Countdown,  // pre-round; nobody can act yet
    Fighting,
    KO,         // someone's health hit 0; the loser falls and stays down
    MatchOver,  // someone took two rounds; the rematch decision happens here
    Ending,     // somebody quit; the scene goes back to the menu shortly
}

// An option on the match-over menu. None is "hasn't confirmed yet" for a choice; a cursor is always
// on one of the other two.
public enum MatchOverChoice
{
    None,
    Rematch,
    Quit,
}

// What each player pressed on the match-over menu this tick. Read off the input log by
// MatchManager, which owns the players and the frame number.
public struct MenuPresses
{
    public bool P1Up;
    public bool P1Down;
    public bool P1Confirm;
    public bool P2Up;
    public bool P2Down;
    public bool P2Confirm;
}

// What the match has to do about a tick, beyond carrying on.
public enum RoundStep
{
    Continue,
    NextRound,  // reset to the start state, keeping the scores
    Rematch,    // reset to the start state, scores back to nil
}

// The countdown, the fight, the KO freeze, the score, and the rematch decision — all of it, and
// nothing else. Lifted out of MatchManager, which had accumulated too many jobs.
//
// A value type, so it copies into the snapshot with everything else. It has to be snapshotted: a
// rematch rewrites both players' state, so both machines must decide on it on the same frame or
// they're desynced by the difference. The rest follows from that — a rollback across a KO has to
// un-kill, so the phase and score have to rewind too.
public struct RoundFlow
{
    // Three seconds, one per number on screen.
    public const int CountdownDuration = 180;
    // How long the loser lies there before the round resets.
    public const int KODuration = 120;
    // Rounds needed to take the match.
    public const int WinsNeeded = 2;
    // How long Ending holds before the scene leaves — past the rollback window, so a mispredicted
    // press can't kick both players out.
    public const int LeaveDelay = 20;

    public RoundPhase Phase;
    public int PhaseFrame;
    public int P1Wins;
    public int P2Wins;
    // Where each cursor sits, and what each player has locked in. Both are in here because the
    // choice is derived from the cursor, so the two machines have to agree on both to agree on the
    // outcome.
    public MatchOverChoice P1Hover;
    public MatchOverChoice P2Hover;
    public MatchOverChoice P1Choice;
    public MatchOverChoice P2Choice;

    public static RoundFlow NewRound(int p1Wins, int p2Wins) => new RoundFlow
    {
        Phase = RoundPhase.Countdown,
        P1Wins = p1Wins,
        P2Wins = p2Wins,
        P1Hover = MatchOverChoice.Rematch,
        P2Hover = MatchOverChoice.Rematch,
    };

    // Whether the players are driving their own characters.
    public readonly bool InputsLive => Phase == RoundPhase.Fighting;
    public readonly bool Decided => Phase == RoundPhase.MatchOver || Phase == RoundPhase.Ending;
    public readonly bool ReadyToLeave => Phase == RoundPhase.Ending && PhaseFrame >= LeaveDelay;

    public readonly int RoundNumber => P1Wins + P2Wins + 1;
    // 0 while the match is still going, otherwise who took it.
    public readonly int Winner => P1Wins >= WinsNeeded ? 1 : P2Wins >= WinsNeeded ? 2 : 0;
    // 3, 2, 1 while counting in, 0 once the round is live. CountdownDuration - PhaseFrame is the
    // frames left, for something smoother than a number swap.
    public readonly int CountdownNumber => Phase == RoundPhase.Countdown
        ? 3 - PhaseFrame * 3 / CountdownDuration
        : 0;

    public readonly MatchOverChoice HoverFor(int playerNumber)
        => playerNumber == 1 ? P1Hover : P2Hover;
    public readonly MatchOverChoice ChoiceFor(int playerNumber)
        => playerNumber == 1 ? P1Choice : P2Choice;

    // One tick's worth. Called at the end of a tick, before the state is recorded, so a rollback to
    // this frame lands on the same side of any transition the first pass did.
    public RoundStep Advance(bool p1Defeated, bool p2Defeated, MenuPresses presses)
    {
        PhaseFrame++;

        switch (Phase)
        {
            case RoundPhase.Countdown:
                if (PhaseFrame >= CountdownDuration) Enter(RoundPhase.Fighting);
                return RoundStep.Continue;

            case RoundPhase.Fighting:
                if (p1Defeated || p2Defeated) Enter(RoundPhase.KO);
                return RoundStep.Continue;

            case RoundPhase.KO:
                return PhaseFrame >= KODuration ? Score(p1Defeated, p2Defeated) : RoundStep.Continue;

            case RoundPhase.MatchOver:
                return Decide(presses);

            default:
                return RoundStep.Continue;
        }
    }

    RoundStep Score(bool p1Defeated, bool p2Defeated)
    {
        // Both down is a draw: nobody scores and the round runs again.
        if (p2Defeated && !p1Defeated) P1Wins++;
        if (p1Defeated && !p2Defeated) P2Wins++;

        // The deciding round doesn't reset. The loser stays down and the winner stays put, which is
        // what a results screen wants behind it.
        if (Winner == 0) return RoundStep.NextRound;

        Enter(RoundPhase.MatchOver);
        return RoundStep.Continue;
    }

    RoundStep Decide(MenuPresses presses)
    {
        Move(presses.P1Up, presses.P1Down, presses.P1Confirm, ref P1Hover, ref P1Choice);
        Move(presses.P2Up, presses.P2Down, presses.P2Confirm, ref P2Hover, ref P2Choice);

        // One quit is enough to end it; a rematch needs both.
        if (P1Choice == MatchOverChoice.Quit || P2Choice == MatchOverChoice.Quit)
        {
            Enter(RoundPhase.Ending);
            return RoundStep.Continue;
        }

        return P1Choice == MatchOverChoice.Rematch && P2Choice == MatchOverChoice.Rematch
            ? RoundStep.Rematch
            : RoundStep.Continue;
    }

    // Rematch on top, Quit below. Once confirmed there's no changing your mind.
    static void Move(bool up, bool down, bool confirm,
                     ref MatchOverChoice hover, ref MatchOverChoice choice)
    {
        if (choice != MatchOverChoice.None) return;

        if (up) hover = MatchOverChoice.Rematch;
        if (down) hover = MatchOverChoice.Quit;
        if (confirm) choice = hover;
    }

    void Enter(RoundPhase next)
    {
        Phase = next;
        PhaseFrame = 0;
    }
}
