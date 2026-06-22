namespace DSCC.Orbbec;

/// <summary>
/// Camera-space bounds used to prefer the body standing inside the station ROI
/// when more than one person is visible to the tracker.
/// </summary>
public readonly record struct BodySelectionRoi(
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    double MinZ,
    double MaxZ)
{
    public bool Contains(double x, double y, double z)
    {
        return x >= MinX && x <= MaxX
            && y >= MinY && y <= MaxY
            && z >= MinZ && z <= MaxZ;
    }

    public double CenterX => (MinX + MaxX) / 2.0;

    public double CenterZ => (MinZ + MaxZ) / 2.0;
}

/// <summary>
/// One tracked body as seen in a single body-tracking frame.
/// Pelvis coordinates are meters in the depth-camera space.
/// </summary>
public readonly record struct BodyCandidate(
    uint BodyId,
    double AverageConfidence,
    double PelvisX,
    double PelvisY,
    double PelvisZ);

/// <summary>
/// Picks which body a station should follow. K4ABT joint confidence rarely
/// distinguishes two well-tracked people, so selection is anchored on the
/// persistent body id from the tracker and on whether the pelvis is inside
/// the station ROI, instead of re-ranking by confidence every frame.
/// </summary>
public sealed class StickyBodySelector
{
    public const int DefaultOutsideRoiGraceFrames = 3;

    private readonly int outsideRoiGraceFrames;
    private uint? trackedBodyId;
    private int trackedBodyOutsideRoiFrames;

    public uint? TrackedBodyId => trackedBodyId;

    public int TrackedBodyOutsideRoiFrames => trackedBodyOutsideRoiFrames;

    public StickyBodySelector(int outsideRoiGraceFrames = DefaultOutsideRoiGraceFrames)
    {
        if (outsideRoiGraceFrames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outsideRoiGraceFrames), outsideRoiGraceFrames, "Grace frames must be non-negative.");
        }

        this.outsideRoiGraceFrames = outsideRoiGraceFrames;
    }

    /// <summary>
    /// Returns the index into <paramref name="candidates"/> of the body the
    /// station should follow, or -1 when there is no candidate.
    /// Selection order: bodies inside the ROI shadow everyone else; within
    /// that group the previously tracked body id wins; otherwise the highest
    /// average confidence, tie-broken by horizontal distance to the ROI center
    /// (or to the camera axis when no ROI is configured).
    /// </summary>
    public int Select(IReadOnlyList<BodyCandidate> candidates, BodySelectionRoi? roi)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return -1;
        }

        var trackedIndex = FindTrackedBodyIndex(candidates);
        if (trackedIndex >= 0 && ShouldKeepTrackedBody(candidates[trackedIndex], roi, candidates))
        {
            return trackedIndex;
        }

        var anyInsideRoi = false;
        if (roi is { } bounds)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                if (bounds.Contains(candidates[index].PelvisX, candidates[index].PelvisY, candidates[index].PelvisZ))
                {
                    anyInsideRoi = true;
                    break;
                }
            }
        }

        var selectedIndex = -1;
        var selectedConfidence = double.MinValue;
        var selectedDistance = double.MaxValue;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (anyInsideRoi && roi is { } roiBounds &&
                !roiBounds.Contains(candidate.PelvisX, candidate.PelvisY, candidate.PelvisZ))
            {
                continue;
            }

            var distance = HorizontalDistance(candidate, roi);
            if (candidate.AverageConfidence > selectedConfidence + 0.000001 ||
                (Math.Abs(candidate.AverageConfidence - selectedConfidence) <= 0.000001 && distance < selectedDistance))
            {
                selectedIndex = index;
                selectedConfidence = candidate.AverageConfidence;
                selectedDistance = distance;
            }
        }

        if (selectedIndex >= 0)
        {
            trackedBodyId = candidates[selectedIndex].BodyId;
            trackedBodyOutsideRoiFrames = 0;
        }

        return selectedIndex;
    }

    public void Reset()
    {
        trackedBodyId = null;
        trackedBodyOutsideRoiFrames = 0;
    }

    private int FindTrackedBodyIndex(IReadOnlyList<BodyCandidate> candidates)
    {
        if (trackedBodyId is not { } trackedId)
        {
            return -1;
        }

        for (var index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].BodyId == trackedId)
            {
                return index;
            }
        }

        return -1;
    }

    private bool ShouldKeepTrackedBody(
        BodyCandidate trackedCandidate,
        BodySelectionRoi? roi,
        IReadOnlyList<BodyCandidate> candidates)
    {
        if (roi is not { } bounds)
        {
            trackedBodyOutsideRoiFrames = 0;
            return true;
        }

        if (bounds.Contains(trackedCandidate.PelvisX, trackedCandidate.PelvisY, trackedCandidate.PelvisZ))
        {
            trackedBodyOutsideRoiFrames = 0;
            return true;
        }

        trackedBodyOutsideRoiFrames++;
        if (!HasOtherBodyInsideRoi(trackedCandidate.BodyId, bounds, candidates))
        {
            return true;
        }

        return trackedBodyOutsideRoiFrames <= outsideRoiGraceFrames;
    }

    private static bool HasOtherBodyInsideRoi(
        uint trackedId,
        BodySelectionRoi roi,
        IReadOnlyList<BodyCandidate> candidates)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.BodyId == trackedId)
            {
                continue;
            }

            if (roi.Contains(candidate.PelvisX, candidate.PelvisY, candidate.PelvisZ))
            {
                return true;
            }
        }

        return false;
    }

    private static double HorizontalDistance(BodyCandidate candidate, BodySelectionRoi? roi)
    {
        if (roi is { } bounds)
        {
            var dx = candidate.PelvisX - bounds.CenterX;
            var dz = candidate.PelvisZ - bounds.CenterZ;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        return Math.Abs(candidate.PelvisX);
    }
}
