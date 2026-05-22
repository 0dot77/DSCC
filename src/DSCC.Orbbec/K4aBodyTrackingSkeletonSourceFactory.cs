namespace DSCC.Orbbec;

public static class K4aBodyTrackingSkeletonSourceFactory
{
    public static bool IsBuildEnabled
    {
        get
        {
#if DSCC_K4A_BODY_TRACKING
            return true;
#else
            return false;
#endif
        }
    }

    public static IOrbbecSkeletonFrameSource Create(K4aBodyTrackingOptions options)
    {
#if DSCC_K4A_BODY_TRACKING
        return new K4aBodyTrackingSkeletonSource(options);
#else
        throw new PlatformNotSupportedException("K4A body tracking is only available in x64 builds.");
#endif
    }
}
