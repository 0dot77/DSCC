namespace DSCC.App.Wpf.ViewModels;

public sealed class JointOverlayPointViewModel
{
    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required double CanvasLeft { get; init; }

    public required double CanvasTop { get; init; }

    public required double Size { get; init; }

    public required double Opacity { get; init; }

    public required string Tooltip { get; init; }
}
