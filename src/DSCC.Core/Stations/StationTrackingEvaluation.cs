namespace DSCC.Core.Stations;

public sealed class StationTrackingEvaluation
{
    public StationState State { get; init; }

    public bool HasSkeleton { get; init; }

    public bool MeetsConfidence { get; init; }

    public bool IsInsideTrackingRoi { get; init; }

    public bool IsInsideFootMarker { get; init; }

    public bool IsValidPlayer { get; init; }

    public bool HasPlayer => State is StationState.Entering or StationState.Active;

    public double Confidence { get; init; }

    public double TrackingLostSeconds { get; init; }

    public DateTimeOffset Timestamp { get; init; }
}
