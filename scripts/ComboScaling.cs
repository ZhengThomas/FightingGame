// Combo damage / hitstun / gravity scaling.
//
// All table values are thousandths: 1000 = 100%, 100 = 10%.
// Sampling and applying a scale uses integer math only:
//   scaled = (base * table[index]) / ScaleUnit
// so the sim stays deterministic across machines (unlike float * 0.9f).
//
// Fill in the tables below. Index 0 is the first hit of a combo; after each hit,
// DamageScaleIndex / HitstunScaleIndex advance by the move's DamageProrationSteps /
// HitstunProrationSteps (default 1 each). Past the last entry, the last value is held.
public static class ComboScaling
{
    public const int ScaleUnit = 10000;

    // >>> Edit these tables <<<
    public static readonly int[] DamageScale =
    {
        10000, 9000, 8000, 7000, 6000, 5000, 4000, 3000, 2000, 1000,
    };

    public static readonly int[] HitstunScale =
    {
        10000, 10000, 10000, 10000, 10000, 9000, 8000, 7000, 6000, 5000, 4000, 3000, 2000, 1000,
    };

    // Hurt gravity scale = 2000 - hitstun scale (hitstun drops → gravity rises).
    public static int GravityFromHitstun(int hitstunScaleThousandths)
        => (2 * ScaleUnit) - hitstunScaleThousandths;

    public static int Sample(int[] table, int index)
    {
        if (table == null || table.Length == 0)
            return ScaleUnit;
        if (index < 0)
            index = 0;
        if (index >= table.Length)
            index = table.Length - 1;
        return table[index];
    }

    // Integer scale apply. Minimum 1 so hitstun/damage never round to 0.
    public static int Apply(int value, int scaleThousandths)
    {
        return System.Math.Max(1, (value * scaleThousandths) / ScaleUnit);
    }
}
