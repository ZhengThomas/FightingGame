// A rule set layered over an ordinary match. Versus has none — it's the plain game — so a mode is
// only what a mode changes, and there's no empty implementation standing in for "normal".
//
// One hook, deliberately. A mode reads the keyboard, writes to players and calls MatchManager, all
// of which are frame-ordered work that belongs before the tick. Anything a mode wants that isn't
// once-a-frame is a sign it should be asking MatchManager for it instead.
public interface IGameMode
{
    // Once per real frame, before the sim ticks. Not inside Tick: a resim runs that many times
    // over, so live input read there would fire repeatedly during a replay.
    void Step();
}
