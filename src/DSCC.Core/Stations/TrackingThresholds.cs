using System.Text.Json.Serialization;

namespace DSCC.Core.Stations;

public sealed class TrackingThresholds
{
    public double EnterStableSeconds { get; set; } = 0.4;

    public double LostGraceSeconds { get; set; } = 1.5;

    public double ExitConfirmSeconds { get; set; } = 3.0;

    public double MinSkeletonConfidence { get; set; } = 0.45;

    public double FootMarkerRadiusMeters { get; set; } = 0.45;

    /// <summary>
    /// When false (default), the foot marker only gates entry: a player must
    /// stand on the marker to become Active, but may dance off it afterwards
    /// as long as they stay inside the tracking ROI. When true, leaving the
    /// marker also counts as tracking loss while Active (legacy behavior).
    /// </summary>
    public bool RequireFootMarkerWhileActive { get; set; }

    public static TrackingThresholds Default => new();

    [JsonIgnore]
    public double MinConfidence
    {
        get => MinSkeletonConfidence;
        set => MinSkeletonConfidence = value;
    }

    [JsonIgnore]
    public double FootMarkerRadius
    {
        get => FootMarkerRadiusMeters;
        set => FootMarkerRadiusMeters = value;
    }

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(EnterStableSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(LostGraceSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(ExitConfirmSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(MinSkeletonConfidence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MinSkeletonConfidence, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegative(FootMarkerRadiusMeters);

        if (ExitConfirmSeconds < LostGraceSeconds)
        {
            throw new ArgumentException("ExitConfirmSeconds must be greater than or equal to LostGraceSeconds.");
        }
    }
}
