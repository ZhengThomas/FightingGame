// What kind of match this is. Separate from whether there's a connection: versus can be played on
// one keyboard or over Steam, while training is always local.
//
// Fixed for the whole match, so it isn't snapshotted — same as InputDelay.
public enum GameMode
{
    Versus,
    Training,
}
