using System.Reflection;

namespace DSCC.Orbbec;

public sealed class OrbbecSdkRuntimeInfo
{
    public string OfficialNativeSdkVersion { get; init; } = OrbbecSdkRuntimeProbe.OfficialNativeSdkVersion;

    public string OfficialCSharpWrapperVersion { get; init; } = OrbbecSdkRuntimeProbe.OfficialCSharpWrapperVersion;

    public string? DetectedNativeSdkVersion { get; init; }

    public string? DetectedManagedWrapperVersion { get; init; }

    public bool IsManagedWrapperAvailable { get; init; }

    public bool IsNativeLibraryAvailable { get; init; }

    public string? ManagedWrapperPath { get; init; }

    public string? NativeLibraryPath { get; init; }

    public string? ErrorMessage { get; init; }

    internal Assembly? ManagedWrapperAssembly { get; init; }

    public bool IsAvailable => IsManagedWrapperAvailable && IsNativeLibraryAvailable && ManagedWrapperAssembly is not null;

    public bool IsNativeSdkCurrent => DetectedNativeSdkVersion == OfficialNativeSdkVersion;
}
