namespace DSCC.Orbbec;

public static class OrbbecSkeletonTrackingStatus
{
    public const string PreviewOnly = "preview only";
    public const string TrackerQueueBusy = "tracker queue busy";
    public const string TrackerQueueBusyDroppingFrame = "tracker queue busy; dropping camera frame";
    public const string WaitingForSkeletonResult = "waiting for skeleton result";

    public static string InitializingTrackerPreviewOnly(string processingMode)
    {
        return $"initializing {processingMode} body tracker; {PreviewOnly}";
    }

    public static bool IsDepthOnlyTransient(string? trackingStatus)
    {
        return !string.IsNullOrWhiteSpace(trackingStatus) &&
               (trackingStatus.Contains(PreviewOnly, StringComparison.OrdinalIgnoreCase) ||
                trackingStatus.Contains(WaitingForSkeletonResult, StringComparison.OrdinalIgnoreCase) ||
                trackingStatus.Contains(TrackerQueueBusy, StringComparison.OrdinalIgnoreCase));
    }
}
