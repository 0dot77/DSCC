using DSCC.Core.Diagnostics;

namespace DSCC.Core.Tests;

public sealed class FieldBodyCountPolicyTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void HasExtraBodies_ReturnsTrueWhenActivePlayerHasMoreThanOneBody(int bodyCount)
    {
        Assert.True(FieldBodyCountPolicy.HasExtraBodies(hasPlayer: true, bodyCount));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void HasExtraBodies_ReturnsFalseWhenFieldOnePersonContractIsNotViolated(bool hasPlayer, int bodyCount)
    {
        Assert.False(FieldBodyCountPolicy.HasExtraBodies(hasPlayer, bodyCount));
    }

    [Fact]
    public void HasExtraBodies_UsesConfiguredMaximum()
    {
        Assert.False(FieldBodyCountPolicy.HasExtraBodies(hasPlayer: true, bodyCount: 2, maxActiveBodyCount: 2));
        Assert.True(FieldBodyCountPolicy.HasExtraBodies(hasPlayer: true, bodyCount: 3, maxActiveBodyCount: 2));
    }
}
