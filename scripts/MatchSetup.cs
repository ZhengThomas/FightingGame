// Everything the fighting scene needs to know about the match it's about to run, handed over by
// whatever screen came before it
public struct MatchSetup
{
    // Null means both players share this keyboard.
    public IInputTransport Transport;

    public GameMode Mode;

    public int InputDelay;
    public int RollbackFrames;
}
