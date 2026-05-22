namespace DSCC.Orbbec;

public sealed class K4aBodyTrackingRuntimeInfo
{
    public string OfficialK4aWrapperVersion { get; init; } = K4aBodyTrackingRuntimeProbe.OfficialK4aWrapperVersion;

    public string OfficialBodyTrackingPackageVersion { get; init; } = K4aBodyTrackingRuntimeProbe.OfficialBodyTrackingPackageVersion;

    public string OfficialSensorPackageVersion { get; init; } = K4aBodyTrackingRuntimeProbe.OfficialSensorPackageVersion;

    public bool IsBodyTrackingBuildEnabled { get; init; }

    public bool IsManagedBodyTrackingAssemblyAvailable { get; init; }

    public bool IsManagedSensorAssemblyAvailable { get; init; }

    public bool IsNativeBodyTrackingLibraryAvailable { get; init; }

    public bool IsOnnxRuntimeAvailable { get; init; }

    public bool IsBodyTrackingModelAvailable { get; init; }

    public bool IsOrbbecK4aWrapperAvailable { get; init; }

    public bool IsOrbbecK4aWrapperDependencyAvailable { get; init; }

    public string? ManagedBodyTrackingAssemblyPath { get; init; }

    public string? ManagedSensorAssemblyPath { get; init; }

    public string? NativeBodyTrackingLibraryPath { get; init; }

    public string? OnnxRuntimePath { get; init; }

    public string? BodyTrackingModelPath { get; init; }

    public string? OrbbecK4aWrapperPath { get; init; }

    public string? OrbbecK4aWrapperDirectory { get; init; }

    public string? OrbbecK4aWrapperDependencyPath { get; init; }

    public IReadOnlyList<string> MissingRequirements { get; init; } = Array.Empty<string>();

    public bool IsAvailable =>
        IsBodyTrackingBuildEnabled &&
        IsManagedBodyTrackingAssemblyAvailable &&
        IsManagedSensorAssemblyAvailable &&
        IsNativeBodyTrackingLibraryAvailable &&
        IsOnnxRuntimeAvailable &&
        IsBodyTrackingModelAvailable &&
        IsOrbbecK4aWrapperAvailable &&
        IsOrbbecK4aWrapperDependencyAvailable;

    public string Status =>
        IsAvailable
            ? $"K4A body tracking ready: wrapper {OfficialK4aWrapperVersion}, body package {OfficialBodyTrackingPackageVersion}"
            : "K4A body tracking unavailable: " + string.Join("; ", MissingRequirements);
}
