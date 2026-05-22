namespace DSCC.Orbbec;

public static class DepthPreviewFactory
{
    private const int MinDepthMillimeters = 250;
    private const int MaxDepthMillimeters = 6_000;
    private const byte MinVisibleDepthShade = 48;

    public static DepthPreviewFrame? FromDepth16(
        ReadOnlySpan<ushort> depthMillimeters,
        int sourceWidth,
        int sourceHeight,
        int maxPreviewWidth = 160)
    {
        if (sourceWidth <= 0 ||
            sourceHeight <= 0 ||
            depthMillimeters.Length < sourceWidth * sourceHeight)
        {
            return null;
        }

        var previewWidth = Math.Min(maxPreviewWidth, sourceWidth);
        var previewHeight = Math.Max(1, (int)Math.Round(sourceHeight * (previewWidth / (double)sourceWidth)));
        var pixels = new byte[previewWidth * previewHeight * 4];
        var stats = CalculateDepthStats(depthMillimeters[..(sourceWidth * sourceHeight)]);

        for (var y = 0; y < previewHeight; y++)
        {
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / previewHeight);
            for (var x = 0; x < previewWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / previewWidth);
                var depth = depthMillimeters[sourceY * sourceWidth + sourceX];
                var offset = (y * previewWidth + x) * 4;
                var shade = DepthToShade(depth);
                pixels[offset] = shade;
                pixels[offset + 1] = shade;
                pixels[offset + 2] = shade;
                pixels[offset + 3] = 255;
            }
        }

        return new DepthPreviewFrame
        {
            Width = previewWidth,
            Height = previewHeight,
            Bgra32 = pixels,
            TotalDepthPixels = stats.TotalPixels,
            ValidDepthPixels = stats.ValidPixels,
            MinDepthMillimeters = stats.MinMillimeters,
            MaxDepthMillimeters = stats.MaxMillimeters
        };
    }

    public static DepthPreviewFrame? FromLuma8(
        ReadOnlySpan<byte> luminance,
        int sourceWidth,
        int sourceHeight,
        int maxPreviewWidth = 160)
    {
        if (sourceWidth <= 0 ||
            sourceHeight <= 0 ||
            luminance.Length < sourceWidth * sourceHeight)
        {
            return null;
        }

        var previewWidth = Math.Min(maxPreviewWidth, sourceWidth);
        var previewHeight = Math.Max(1, (int)Math.Round(sourceHeight * (previewWidth / (double)sourceWidth)));
        var pixels = new byte[previewWidth * previewHeight * 4];

        for (var y = 0; y < previewHeight; y++)
        {
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / previewHeight);
            for (var x = 0; x < previewWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / previewWidth);
                var shade = luminance[sourceY * sourceWidth + sourceX];
                var offset = (y * previewWidth + x) * 4;
                pixels[offset] = shade;
                pixels[offset + 1] = shade;
                pixels[offset + 2] = shade;
                pixels[offset + 3] = 255;
            }
        }

        return new DepthPreviewFrame
        {
            Width = previewWidth,
            Height = previewHeight,
            Bgra32 = pixels
        };
    }

    public static DepthPreviewFrame? FromLuma16(
        ReadOnlySpan<ushort> luminance,
        int sourceWidth,
        int sourceHeight,
        int maxPreviewWidth = 160)
    {
        if (sourceWidth <= 0 ||
            sourceHeight <= 0 ||
            luminance.Length < sourceWidth * sourceHeight)
        {
            return null;
        }

        var max = 0;
        for (var index = 0; index < luminance.Length; index++)
        {
            max = Math.Max(max, luminance[index]);
        }

        if (max <= 0)
        {
            return FromLuma8(new byte[sourceWidth * sourceHeight], sourceWidth, sourceHeight, maxPreviewWidth);
        }

        var previewWidth = Math.Min(maxPreviewWidth, sourceWidth);
        var previewHeight = Math.Max(1, (int)Math.Round(sourceHeight * (previewWidth / (double)sourceWidth)));
        var pixels = new byte[previewWidth * previewHeight * 4];

        for (var y = 0; y < previewHeight; y++)
        {
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / previewHeight);
            for (var x = 0; x < previewWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / previewWidth);
                var shade = (byte)Math.Clamp((int)Math.Round(luminance[sourceY * sourceWidth + sourceX] * 255.0 / max), 0, 255);
                var offset = (y * previewWidth + x) * 4;
                pixels[offset] = shade;
                pixels[offset + 1] = shade;
                pixels[offset + 2] = shade;
                pixels[offset + 3] = 255;
            }
        }

        return new DepthPreviewFrame
        {
            Width = previewWidth,
            Height = previewHeight,
            Bgra32 = pixels
        };
    }

    public static DepthPreviewFrame? FromColor(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        ColorPreviewFormat format,
        int maxPreviewWidth = 160)
    {
        var bytesPerPixel = format switch
        {
            ColorPreviewFormat.Rgb24 or ColorPreviewFormat.Bgr24 => 3,
            ColorPreviewFormat.Rgba32 or ColorPreviewFormat.Bgra32 => 4,
            _ => 0
        };
        if (sourceWidth <= 0 ||
            sourceHeight <= 0 ||
            bytesPerPixel == 0 ||
            source.Length < sourceWidth * sourceHeight * bytesPerPixel)
        {
            return null;
        }

        var previewWidth = Math.Min(maxPreviewWidth, sourceWidth);
        var previewHeight = Math.Max(1, (int)Math.Round(sourceHeight * (previewWidth / (double)sourceWidth)));
        var pixels = new byte[previewWidth * previewHeight * 4];

        for (var y = 0; y < previewHeight; y++)
        {
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / previewHeight);
            for (var x = 0; x < previewWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / previewWidth);
                var sourceOffset = (sourceY * sourceWidth + sourceX) * bytesPerPixel;
                var offset = (y * previewWidth + x) * 4;
                WriteColorPixel(source, sourceOffset, format, pixels, offset);
            }
        }

        return new DepthPreviewFrame
        {
            Width = previewWidth,
            Height = previewHeight,
            Bgra32 = pixels
        };
    }

    private static byte DepthToShade(ushort depthMillimeters)
    {
        if (depthMillimeters == 0)
        {
            return 0;
        }

        var clamped = Math.Clamp((int)depthMillimeters, MinDepthMillimeters, MaxDepthMillimeters);
        var normalized = (clamped - MinDepthMillimeters) / (double)(MaxDepthMillimeters - MinDepthMillimeters);
        return (byte)Math.Round(MinVisibleDepthShade + (255.0 - MinVisibleDepthShade) * (1.0 - normalized));
    }

    private static DepthPreviewStats CalculateDepthStats(ReadOnlySpan<ushort> depthMillimeters)
    {
        var validPixels = 0;
        var min = int.MaxValue;
        var max = 0;

        for (var index = 0; index < depthMillimeters.Length; index++)
        {
            var depth = depthMillimeters[index];
            if (depth == 0)
            {
                continue;
            }

            validPixels++;
            min = Math.Min(min, depth);
            max = Math.Max(max, depth);
        }

        return new DepthPreviewStats(
            depthMillimeters.Length,
            validPixels,
            validPixels == 0 ? 0 : min,
            validPixels == 0 ? 0 : max);
    }

    private static void WriteColorPixel(
        ReadOnlySpan<byte> source,
        int sourceOffset,
        ColorPreviewFormat format,
        byte[] destination,
        int destinationOffset)
    {
        switch (format)
        {
            case ColorPreviewFormat.Rgb24:
                destination[destinationOffset] = source[sourceOffset + 2];
                destination[destinationOffset + 1] = source[sourceOffset + 1];
                destination[destinationOffset + 2] = source[sourceOffset];
                break;
            case ColorPreviewFormat.Bgr24:
                destination[destinationOffset] = source[sourceOffset];
                destination[destinationOffset + 1] = source[sourceOffset + 1];
                destination[destinationOffset + 2] = source[sourceOffset + 2];
                break;
            case ColorPreviewFormat.Rgba32:
                destination[destinationOffset] = source[sourceOffset + 2];
                destination[destinationOffset + 1] = source[sourceOffset + 1];
                destination[destinationOffset + 2] = source[sourceOffset];
                break;
            case ColorPreviewFormat.Bgra32:
                destination[destinationOffset] = source[sourceOffset];
                destination[destinationOffset + 1] = source[sourceOffset + 1];
                destination[destinationOffset + 2] = source[sourceOffset + 2];
                break;
        }

        destination[destinationOffset + 3] = 255;
    }
}

internal readonly record struct DepthPreviewStats(
    int TotalPixels,
    int ValidPixels,
    int MinMillimeters,
    int MaxMillimeters);

public enum ColorPreviewFormat
{
    Rgb24,
    Bgr24,
    Rgba32,
    Bgra32
}
