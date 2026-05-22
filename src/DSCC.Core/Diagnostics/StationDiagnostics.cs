using DSCC.Core.Stations;

namespace DSCC.Core.Diagnostics;

public sealed class StationDiagnostics
{
    public long TotalFrames { get; set; }

    public long ValidCandidateFrames { get; set; }

    public long LostFrames { get; set; }

    public long DroppedFrames { get; set; }

    public DateTimeOffset? LastFrameTimestamp { get; set; }

    public DateTimeOffset? LastStateChangedTimestamp { get; set; }

    public StationState LastState { get; set; } = StationState.Empty;

    public double LastConfidence { get; set; }

    public double LastTrackingLostSeconds { get; set; }

    public string LastError { get; set; } = string.Empty;
}
