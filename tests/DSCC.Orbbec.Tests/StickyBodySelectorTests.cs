using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class StickyBodySelectorTests
{
    private static readonly BodySelectionRoi Roi = new(-0.7, 0.7, 0.0, 2.4, 1.5, 2.8);

    [Fact]
    public void Select_WithNoCandidates_ReturnsMinusOne()
    {
        var selector = new StickyBodySelector();

        Assert.Equal(-1, selector.Select(Array.Empty<BodyCandidate>(), Roi));
        Assert.Null(selector.TrackedBodyId);
    }

    [Fact]
    public void Select_WithSingleBody_SelectsAndSticks()
    {
        var selector = new StickyBodySelector();
        var candidates = new[] { new BodyCandidate(7u, 0.66, 0.0, 1.0, 2.0) };

        Assert.Equal(0, selector.Select(candidates, Roi));
        Assert.Equal(7u, selector.TrackedBodyId);
    }

    [Fact]
    public void Select_KeepsTrackedBody_WhenAnotherBodyHasHigherConfidence()
    {
        var selector = new StickyBodySelector();
        selector.Select(new[] { new BodyCandidate(1u, 0.66, 0.0, 1.0, 2.0) }, Roi);

        // Both inside ROI; the new body reports better confidence but must not steal the station.
        var candidates = new[]
        {
            new BodyCandidate(2u, 0.9, 0.3, 1.0, 2.0),
            new BodyCandidate(1u, 0.5, -0.1, 1.0, 2.1)
        };

        Assert.Equal(1, selector.Select(candidates, Roi));
        Assert.Equal(1u, selector.TrackedBodyId);
    }

    [Fact]
    public void Select_KeepsTrackedBodyBriefly_WhenReplacementIsInsideRoi()
    {
        var selector = new StickyBodySelector();
        selector.Select(new[] { new BodyCandidate(1u, 0.66, 0.0, 1.0, 2.0) }, Roi);

        var candidates = new[]
        {
            new BodyCandidate(1u, 0.66, 1.8, 1.0, 2.0),  // tracked body walked out of the ROI
            new BodyCandidate(2u, 0.66, 0.0, 1.0, 2.1)   // someone is standing in the station
        };

        Assert.Equal(0, selector.Select(candidates, Roi));
        Assert.Equal(1u, selector.TrackedBodyId);
        Assert.Equal(1, selector.TrackedBodyOutsideRoiFrames);
    }

    [Fact]
    public void Select_PrefersBodyInsideRoi_AfterTrackedBodyStaysOutsideRoi()
    {
        var selector = new StickyBodySelector(outsideRoiGraceFrames: 2);
        selector.Select(new[] { new BodyCandidate(1u, 0.66, 0.0, 1.0, 2.0) }, Roi);

        var candidates = new[]
        {
            new BodyCandidate(1u, 0.66, 1.8, 1.0, 2.0),
            new BodyCandidate(2u, 0.66, 0.0, 1.0, 2.1)
        };

        Assert.Equal(0, selector.Select(candidates, Roi));
        Assert.Equal(0, selector.Select(candidates, Roi));
        Assert.Equal(1, selector.Select(candidates, Roi));
        Assert.Equal(2u, selector.TrackedBodyId);
        Assert.Equal(0, selector.TrackedBodyOutsideRoiFrames);
    }

    [Fact]
    public void Select_KeepsTrackedBodyOutsideRoi_WhenNobodyIsInsideRoi()
    {
        var selector = new StickyBodySelector();
        selector.Select(new[] { new BodyCandidate(1u, 0.66, 0.0, 1.0, 2.0) }, Roi);

        var candidates = new[]
        {
            new BodyCandidate(2u, 0.9, 2.5, 1.0, 2.0),
            new BodyCandidate(1u, 0.5, 1.8, 1.0, 2.0)
        };

        Assert.Equal(1, selector.Select(candidates, Roi));
        Assert.Equal(1u, selector.TrackedBodyId);
    }

    [Fact]
    public void Select_WithEqualConfidence_PrefersBodyNearestRoiCenter()
    {
        var selector = new StickyBodySelector();
        var candidates = new[]
        {
            new BodyCandidate(1u, 0.66, 0.6, 1.0, 2.7),
            new BodyCandidate(2u, 0.66, 0.05, 1.0, 2.15)
        };

        Assert.Equal(1, selector.Select(candidates, Roi));
        Assert.Equal(2u, selector.TrackedBodyId);
    }

    [Fact]
    public void Select_WithoutRoi_UsesStickinessThenConfidence()
    {
        var selector = new StickyBodySelector();
        Assert.Equal(1, selector.Select(
            new[]
            {
                new BodyCandidate(1u, 0.5, 0.4, 1.0, 2.0),
                new BodyCandidate(2u, 0.8, -0.3, 1.0, 2.2)
            },
            roi: null));
        Assert.Equal(2u, selector.TrackedBodyId);

        Assert.Equal(0, selector.Select(
            new[]
            {
                new BodyCandidate(2u, 0.4, -0.3, 1.0, 2.2),
                new BodyCandidate(3u, 0.9, 0.0, 1.0, 2.0)
            },
            roi: null));
        Assert.Equal(2u, selector.TrackedBodyId);
    }

    [Fact]
    public void Select_AfterTrackedBodyDisappears_AdoptsReplacementAndSticksToIt()
    {
        var selector = new StickyBodySelector();
        selector.Select(new[] { new BodyCandidate(1u, 0.66, 0.0, 1.0, 2.0) }, Roi);

        var replacement = new[] { new BodyCandidate(5u, 0.66, 0.1, 1.0, 2.0) };
        Assert.Equal(0, selector.Select(replacement, Roi));
        Assert.Equal(5u, selector.TrackedBodyId);
    }

    [Fact]
    public void Reset_ClearsTrackedBody()
    {
        var selector = new StickyBodySelector();
        selector.Select(new[] { new BodyCandidate(1u, 0.66, 0.0, 1.0, 2.0) }, Roi);
        selector.Select(new[] { new BodyCandidate(1u, 0.66, 1.8, 1.0, 2.0) }, Roi);

        selector.Reset();

        Assert.Null(selector.TrackedBodyId);
        Assert.Equal(0, selector.TrackedBodyOutsideRoiFrames);
    }
}
