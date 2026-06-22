namespace DSCC.Core.Diagnostics;

public static class FieldBodyCountPolicy
{
    public const int DefaultMaxActiveBodyCount = 1;

    public static bool HasExtraBodies(bool hasPlayer, int bodyCount, int maxActiveBodyCount = DefaultMaxActiveBodyCount)
    {
        return hasPlayer &&
               maxActiveBodyCount >= 0 &&
               bodyCount > maxActiveBodyCount;
    }
}
