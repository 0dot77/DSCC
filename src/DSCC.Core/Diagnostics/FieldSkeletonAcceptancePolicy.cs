namespace DSCC.Core.Diagnostics;

public static class FieldSkeletonAcceptancePolicy
{
    private static readonly string[] AppDrivingJointNameValues =
    [
        "Head",
        "Nose",
        "HandLeft",
        "HandRight",
        "KneeLeft",
        "KneeRight",
        "AnkleLeft",
        "AnkleRight",
        "FootLeft",
        "FootRight"
    ];

    public const double DefaultMotionThresholdMeters = 0.05;

    public static IReadOnlyList<string> AppDrivingJointNames => AppDrivingJointNameValues;

    public static string AppDrivingJointNamesCsv => string.Join(",", AppDrivingJointNameValues);
}
