using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class DepthPreviewFactoryTests
{
    [Fact]
    public void FromDepth16_CreatesScaledBgraPreview()
    {
        ushort[] depth =
        [
            0, 500, 1_500, 4_500,
            0, 500, 1_500, 4_500
        ];

        var preview = DepthPreviewFactory.FromDepth16(depth, sourceWidth: 4, sourceHeight: 2, maxPreviewWidth: 2);

        Assert.NotNull(preview);
        Assert.Equal(2, preview.Width);
        Assert.Equal(1, preview.Height);
        Assert.Equal(2 * 1 * 4, preview.Bgra32.Length);
        Assert.Equal(0, preview.Bgra32[0]);
        Assert.Equal(210, preview.Bgra32[4]);
        Assert.Equal(8, preview.TotalDepthPixels);
        Assert.Equal(6, preview.ValidDepthPixels);
        Assert.Equal(500, preview.MinDepthMillimeters);
        Assert.Equal(4_500, preview.MaxDepthMillimeters);
        Assert.All(Enumerable.Range(0, preview.Width * preview.Height), index =>
        {
            Assert.Equal(255, preview.Bgra32[index * 4 + 3]);
        });
    }

    [Fact]
    public void FromDepth16_KeepsFarValidDepthVisible()
    {
        ushort[] depth = [6_000];

        var preview = DepthPreviewFactory.FromDepth16(depth, sourceWidth: 1, sourceHeight: 1);

        Assert.NotNull(preview);
        Assert.Equal(48, preview.Bgra32[0]);
        Assert.Equal(1, preview.TotalDepthPixels);
        Assert.Equal(1, preview.ValidDepthPixels);
        Assert.Equal(6_000, preview.MinDepthMillimeters);
        Assert.Equal(6_000, preview.MaxDepthMillimeters);
    }

    [Fact]
    public void FromDepth16_ReturnsNullWhenBufferIsIncomplete()
    {
        var preview = DepthPreviewFactory.FromDepth16([1_000, 2_000], sourceWidth: 2, sourceHeight: 2);

        Assert.Null(preview);
    }

    [Fact]
    public void FromLuma16_CreatesInfraredStylePreview()
    {
        ushort[] infrared = [0, 100, 200, 400];

        var preview = DepthPreviewFactory.FromLuma16(infrared, sourceWidth: 2, sourceHeight: 2);

        Assert.NotNull(preview);
        Assert.Equal(2, preview.Width);
        Assert.Equal(2, preview.Height);
        Assert.Equal(0, preview.Bgra32[0]);
        Assert.Equal(255, preview.Bgra32[12]);
    }

    [Fact]
    public void FromColor_ConvertsRgbToBgra()
    {
        byte[] rgb = [255, 0, 0];

        var preview = DepthPreviewFactory.FromColor(rgb, sourceWidth: 1, sourceHeight: 1, ColorPreviewFormat.Rgb24);

        Assert.NotNull(preview);
        Assert.Equal(0, preview.Bgra32[0]);
        Assert.Equal(0, preview.Bgra32[1]);
        Assert.Equal(255, preview.Bgra32[2]);
        Assert.Equal(255, preview.Bgra32[3]);
    }
}
