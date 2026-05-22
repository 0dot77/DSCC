namespace DSCC.Protocol;

public sealed class HeadRotationStabilizerOptions
{
    public bool Enabled { get; init; } = true;

    public float SmoothingHalfLifeSeconds { get; init; } = 0.08f;

    public float MaxDegreesPerSecond { get; init; } = 240.0f;

    public float MinConfidence { get; init; } = 0.45f;

    public float DeadZoneDegrees { get; init; } = 0.75f;
}
