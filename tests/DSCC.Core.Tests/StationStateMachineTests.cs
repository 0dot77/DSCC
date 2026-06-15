using DSCC.Core.Calibration;
using DSCC.Core.Stations;

namespace DSCC.Core.Tests;

public sealed class StationStateMachineTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 5, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Update_TransitionsFromEmptyToEnteringThenActiveAfterStableWindow()
    {
        var station = CreateStation();
        var machine = new StationStateMachine(station);

        Assert.Equal(StationState.Entering, machine.Update(ValidSample(StartTime), StartTime).State);
        Assert.Equal(StationState.Entering, machine.Update(ValidSample(StartTime.AddMilliseconds(390)), StartTime.AddMilliseconds(390)).State);
        Assert.Equal(StationState.Active, machine.Update(ValidSample(StartTime.AddMilliseconds(400)), StartTime.AddMilliseconds(400)).State);
        Assert.Equal(StationState.Active, station.State);
    }

    [Fact]
    public void Update_RemainsEmptyWhenFootMarkerIsOutsideRadius()
    {
        var station = CreateStation();
        var machine = new StationStateMachine(station);
        var outsideFootMarker = SkeletonTrackingSample.Detected(
            new Vector3Meters(0.0, 1.0, 2.1),
            new Vector3Meters(0.8, 0.0, 2.1),
            confidence: 0.9,
            timestamp: StartTime);

        var evaluation = machine.Update(outsideFootMarker, StartTime);

        Assert.Equal(StationState.Empty, evaluation.State);
        Assert.True(evaluation.IsInsideTrackingRoi);
        Assert.False(evaluation.IsInsideFootMarker);
        Assert.False(evaluation.IsValidPlayer);
    }

    [Fact]
    public void Update_HonorsLostGraceBeforeLostAndExitConfirmBeforeExited()
    {
        var station = CreateStation();
        var machine = new StationStateMachine(station);
        var activeAt = StartTime.AddMilliseconds(400);

        machine.Update(ValidSample(StartTime), StartTime);
        machine.Update(ValidSample(activeAt), activeAt);

        Assert.Equal(StationState.Active, machine.Update(null, activeAt.AddMilliseconds(200)).State);
        Assert.Equal(StationState.Active, station.State);

        var lost = machine.Update(null, activeAt.AddMilliseconds(1500));
        Assert.Equal(StationState.Lost, lost.State);
        Assert.Equal(1.5, lost.TrackingLostSeconds, precision: 3);

        Assert.Equal(StationState.Exited, machine.Update(null, activeAt.AddMilliseconds(3000)).State);
        Assert.Equal(StationState.Empty, machine.Update(null, activeAt.AddMilliseconds(3100)).State);
    }

    [Fact]
    public void Update_KeepsActiveWhenPlayerLeavesFootMarkerButStaysInsideRoi()
    {
        var station = CreateStation();
        var machine = new StationStateMachine(station);
        var activeAt = StartTime.AddMilliseconds(400);

        machine.Update(ValidSample(StartTime), StartTime);
        Assert.Equal(StationState.Active, machine.Update(ValidSample(activeAt), activeAt).State);

        // Dance away from the marker for far longer than the exit window.
        var offMarkerAt = activeAt.AddSeconds(10.0);
        var evaluation = machine.Update(OffMarkerSample(offMarkerAt), offMarkerAt);

        Assert.Equal(StationState.Active, evaluation.State);
        Assert.True(evaluation.IsInsideTrackingRoi);
        Assert.False(evaluation.IsInsideFootMarker);
        Assert.True(evaluation.IsValidPlayer);
    }

    [Fact]
    public void Update_RequiresFootMarkerToEnter_EvenWithDefaultPolicy()
    {
        var station = CreateStation();
        var machine = new StationStateMachine(station);

        Assert.Equal(StationState.Empty, machine.Update(OffMarkerSample(StartTime), StartTime).State);
        Assert.Equal(StationState.Empty, machine.Update(OffMarkerSample(StartTime.AddSeconds(2)), StartTime.AddSeconds(2)).State);
    }

    [Fact]
    public void Update_WithRequireFootMarkerWhileActive_LosesPlayerWhoLeavesMarker()
    {
        var station = CreateStation();
        station.Thresholds.RequireFootMarkerWhileActive = true;
        var machine = new StationStateMachine(station);
        var activeAt = StartTime.AddMilliseconds(400);

        machine.Update(ValidSample(StartTime), StartTime);
        Assert.Equal(StationState.Active, machine.Update(ValidSample(activeAt), activeAt).State);

        Assert.Equal(StationState.Active, machine.Update(OffMarkerSample(activeAt.AddMilliseconds(200)), activeAt.AddMilliseconds(200)).State);
        Assert.Equal(StationState.Lost, machine.Update(OffMarkerSample(activeAt.AddMilliseconds(1700)), activeAt.AddMilliseconds(1700)).State);
        Assert.Equal(StationState.Exited, machine.Update(OffMarkerSample(activeAt.AddMilliseconds(3200)), activeAt.AddMilliseconds(3200)).State);
    }

    [Fact]
    public void Update_RecoversFromLostStraightToActive_WithoutEntryGate()
    {
        var station = CreateStation();
        var machine = new StationStateMachine(station);
        var activeAt = StartTime.AddMilliseconds(400);

        machine.Update(ValidSample(StartTime), StartTime);
        machine.Update(ValidSample(activeAt), activeAt);

        var lostAt = activeAt.AddMilliseconds(1600);
        Assert.Equal(StationState.Lost, machine.Update(null, lostAt).State);

        // Player re-appears off the marker but inside the ROI: same person, keep them Active.
        var recoveredAt = activeAt.AddMilliseconds(1800);
        var evaluation = machine.Update(OffMarkerSample(recoveredAt), recoveredAt);

        Assert.Equal(StationState.Active, evaluation.State);
        Assert.True(evaluation.IsValidPlayer);
    }

    [Fact]
    public void Update_LeavingRoi_StillExitsThroughLostAndExited()
    {
        var station = CreateStation();
        var machine = new StationStateMachine(station);
        var activeAt = StartTime.AddMilliseconds(400);

        machine.Update(ValidSample(StartTime), StartTime);
        Assert.Equal(StationState.Active, machine.Update(ValidSample(activeAt), activeAt).State);

        var outsideRoi = SkeletonTrackingSample.Detected(
            new Vector3Meters(1.6, 1.0, 2.1),
            new Vector3Meters(1.6, 0.0, 2.1),
            confidence: 0.9,
            timestamp: activeAt.AddMilliseconds(200));

        Assert.Equal(StationState.Active, machine.Update(outsideRoi, activeAt.AddMilliseconds(200)).State);
        Assert.Equal(StationState.Lost, machine.Update(outsideRoi, activeAt.AddMilliseconds(1700)).State);
        Assert.Equal(StationState.Exited, machine.Update(outsideRoi, activeAt.AddMilliseconds(3200)).State);
    }

    private static Station CreateStation()
    {
        return new Station
        {
            StationId = 1,
            DisplayName = "Station 1",
            Calibration = new StationCalibration
            {
                FootMarkerCenter = new Vector3Meters(0.0, 0.0, 2.1),
                TrackingRoi = new TrackingRoi
                {
                    MinX = -0.7,
                    MaxX = 0.7,
                    MinY = 0.0,
                    MaxY = 2.4,
                    MinZ = 1.5,
                    MaxZ = 2.8
                }
            },
            Thresholds = new TrackingThresholds
            {
                EnterStableSeconds = 0.4,
                LostGraceSeconds = 1.5,
                ExitConfirmSeconds = 3.0,
                MinSkeletonConfidence = 0.45,
                FootMarkerRadiusMeters = 0.45
            }
        };
    }

    private static SkeletonTrackingSample ValidSample(DateTimeOffset timestamp)
    {
        return SkeletonTrackingSample.Detected(
            new Vector3Meters(0.0, 1.0, 2.1),
            new Vector3Meters(0.05, 0.0, 2.1),
            confidence: 0.9,
            timestamp);
    }

    private static SkeletonTrackingSample OffMarkerSample(DateTimeOffset timestamp)
    {
        // Pelvis inside the ROI, feet ~0.78m from the marker center (> 0.45m radius).
        return SkeletonTrackingSample.Detected(
            new Vector3Meters(0.3, 1.0, 2.5),
            new Vector3Meters(0.6, 0.0, 2.6),
            confidence: 0.9,
            timestamp);
    }
}
