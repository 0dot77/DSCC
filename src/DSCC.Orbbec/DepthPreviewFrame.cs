namespace DSCC.Orbbec;

public sealed class DepthPreviewFrame
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required byte[] Bgra32 { get; init; }

    public int TotalDepthPixels { get; init; }

    public int ValidDepthPixels { get; init; }

    public int MinDepthMillimeters { get; init; }

    public int MaxDepthMillimeters { get; init; }

    public int Stride => Width * 4;
}
