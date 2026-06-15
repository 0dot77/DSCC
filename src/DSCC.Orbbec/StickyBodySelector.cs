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
    private uint? trackedBodyId;

    public uint? TrackedBodyId => trackedBodyId;

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

            if (trackedBodyId is { } trackedId && candidate.BodyId == trackedId)
            {
                return index;
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
        }

        return selectedIndex;
    }

    public void Reset()
    {
        trackedBodyId = null;
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
