namespace DSCC.Core.Stations;

public sealed class StationStateMachine
{
    private DateTimeOffset? _validSince;
    private DateTimeOffset? _invalidSince;
    private DateTimeOffset? _lastValidTimestamp;

    public StationStateMachine(Station station, TrackingThresholds? thresholds = null)
    {
        Station = station ?? throw new ArgumentNullException(nameof(station));
        Thresholds = thresholds ?? station.Thresholds;
        Thresholds.Validate();
        CurrentState = station.State;
        LastEvaluation = new StationTrackingEvaluation
        {
            State = CurrentState,
            Timestamp = DateTimeOffset.MinValue
        };
    }

    public Station Station { get; }

    public TrackingThresholds Thresholds { get; }

    public StationState CurrentState { get; private set; }

    public StationTrackingEvaluation LastEvaluation { get; private set; }

    public StationTrackingEvaluation Update(SkeletonTrackingSample? sample, DateTimeOffset timestamp)
    {
        if (!Station.Enabled)
        {
            TransitionTo(StationState.Disabled, timestamp);
            LastEvaluation = BuildEvaluation(sample, timestamp, isValidPlayer: false, trackingLostSeconds: 0.0);
            return LastEvaluation;
        }

        var candidateEvaluation = EvaluateCandidate(sample, timestamp);
        var previousState = CurrentState;

        if (candidateEvaluation.IsValidPlayer)
        {
            _invalidSince = null;
            _validSince ??= timestamp;
            _lastValidTimestamp = timestamp;

            if (CurrentState is StationState.Empty or StationState.Lost or StationState.Exited)
            {
                CurrentState = StationState.Entering;
            }

            if (CurrentState == StationState.Entering
                && ElapsedSeconds(_validSince.Value, timestamp) >= Thresholds.EnterStableSeconds)
            {
                CurrentState = StationState.Active;
            }
        }
        else
        {
            _validSince = null;
            ApplyInvalidTransition(timestamp);
        }

        if (previousState != CurrentState)
        {
            Station.Diagnostics.LastStateChangedTimestamp = timestamp;
        }

        Station.State = CurrentState;
        Station.LastSkeletonFrame = sample is { HasSkeleton: true } ? sample : Station.LastSkeletonFrame;

        LastEvaluation = BuildEvaluation(
            sample,
            timestamp,
            candidateEvaluation.IsValidPlayer,
            _invalidSince is { } invalidSince ? ElapsedSeconds(invalidSince, timestamp) : 0.0);

        UpdateDiagnostics(sample, LastEvaluation, timestamp);
        return LastEvaluation;
    }

    public StationTrackingEvaluation Update(SkeletonTrackingSample? sample)
    {
        var timestamp = sample is null || sample.Timestamp == default
            ? DateTimeOffset.UtcNow
            : sample.Timestamp;

        return Update(sample, timestamp);
    }

    public void Reset(DateTimeOffset timestamp)
    {
        _validSince = null;
        _invalidSince = null;
        _lastValidTimestamp = null;
        TransitionTo(StationState.Empty, timestamp);
        LastEvaluation = new StationTrackingEvaluation
        {
            State = CurrentState,
            Timestamp = timestamp
        };
    }

    private CandidateEvaluation EvaluateCandidate(SkeletonTrackingSample? sample, DateTimeOffset timestamp)
    {
        if (sample is null || !sample.HasSkeleton)
        {
            return new CandidateEvaluation(false, false, false, false, false, 0.0);
        }

        var meetsConfidence = sample.Confidence >= Thresholds.MinSkeletonConfidence;
        var insideRoi = Station.Calibration.TrackingRoi.Contains(sample.PelvisPosition);
        var insideFootMarker = sample.FootCandidates()
            .Any(foot => Station.Calibration.IsInsideFootMarker(foot, Thresholds.FootMarkerRadiusMeters));
        var isValidPlayer = meetsConfidence && insideRoi && insideFootMarker;

        return new CandidateEvaluation(
            true,
            meetsConfidence,
            insideRoi,
            insideFootMarker,
            isValidPlayer,
            sample.Confidence);
    }

    private void ApplyInvalidTransition(DateTimeOffset timestamp)
    {
        switch (CurrentState)
        {
            case StationState.Entering:
                _invalidSince = null;
                CurrentState = StationState.Empty;
                break;

            case StationState.Active:
            case StationState.Lost:
                _invalidSince ??= _lastValidTimestamp ?? timestamp;
                var lostSeconds = ElapsedSeconds(_invalidSince.Value, timestamp);
                CurrentState = lostSeconds >= Thresholds.ExitConfirmSeconds
                    ? StationState.Exited
                    : lostSeconds >= Thresholds.LostGraceSeconds
                        ? StationState.Lost
                        : StationState.Active;
                break;

            case StationState.Exited:
                _invalidSince = null;
                CurrentState = StationState.Empty;
                break;

            case StationState.Disabled:
            case StationState.Error:
            case StationState.Empty:
            default:
                _invalidSince = null;
                CurrentState = StationState.Empty;
                break;
        }
    }

    private StationTrackingEvaluation BuildEvaluation(
        SkeletonTrackingSample? sample,
        DateTimeOffset timestamp,
        bool isValidPlayer,
        double trackingLostSeconds)
    {
        if (sample is null || !sample.HasSkeleton)
        {
            return new StationTrackingEvaluation
            {
                State = CurrentState,
                HasSkeleton = false,
                MeetsConfidence = false,
                IsInsideTrackingRoi = false,
                IsInsideFootMarker = false,
                IsValidPlayer = false,
                Confidence = 0.0,
                TrackingLostSeconds = trackingLostSeconds,
                Timestamp = timestamp
            };
        }

        return new StationTrackingEvaluation
        {
            State = CurrentState,
            HasSkeleton = true,
            MeetsConfidence = sample.Confidence >= Thresholds.MinSkeletonConfidence,
            IsInsideTrackingRoi = Station.Calibration.TrackingRoi.Contains(sample.PelvisPosition),
            IsInsideFootMarker = sample.FootCandidates()
                .Any(foot => Station.Calibration.IsInsideFootMarker(foot, Thresholds.FootMarkerRadiusMeters)),
            IsValidPlayer = isValidPlayer,
            Confidence = sample.Confidence,
            TrackingLostSeconds = trackingLostSeconds,
            Timestamp = timestamp
        };
    }

    private void UpdateDiagnostics(
        SkeletonTrackingSample? sample,
        StationTrackingEvaluation evaluation,
        DateTimeOffset timestamp)
    {
        Station.Diagnostics.TotalFrames++;
        Station.Diagnostics.LastFrameTimestamp = timestamp;
        Station.Diagnostics.LastState = CurrentState;
        Station.Diagnostics.LastConfidence = evaluation.Confidence;
        Station.Diagnostics.LastTrackingLostSeconds = evaluation.TrackingLostSeconds;

        if (evaluation.IsValidPlayer)
        {
            Station.Diagnostics.ValidCandidateFrames++;
        }

        if (sample is null || !sample.HasSkeleton || !evaluation.IsValidPlayer)
        {
            Station.Diagnostics.LostFrames++;
        }
    }

    private void TransitionTo(StationState state, DateTimeOffset timestamp)
    {
        if (CurrentState == state)
        {
            return;
        }

        CurrentState = state;
        Station.State = state;
        Station.Diagnostics.LastState = state;
        Station.Diagnostics.LastStateChangedTimestamp = timestamp;
    }

    private static double ElapsedSeconds(DateTimeOffset start, DateTimeOffset end)
    {
        return Math.Max(0.0, (end - start).TotalSeconds);
    }

    private readonly record struct CandidateEvaluation(
        bool HasSkeleton,
        bool MeetsConfidence,
        bool IsInsideTrackingRoi,
        bool IsInsideFootMarker,
        bool IsValidPlayer,
        double Confidence);
}
