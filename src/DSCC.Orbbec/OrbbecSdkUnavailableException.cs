namespace DSCC.Orbbec;

public sealed class OrbbecSdkUnavailableException : InvalidOperationException
{
    public OrbbecSdkUnavailableException(OrbbecSdkRuntimeInfo runtimeInfo)
        : base(BuildMessage(runtimeInfo))
    {
        RuntimeInfo = runtimeInfo;
    }

    public OrbbecSdkRuntimeInfo RuntimeInfo { get; }

    private static string BuildMessage(OrbbecSdkRuntimeInfo runtimeInfo)
    {
        return "Orbbec SDK v2 runtime is not available. " +
            $"Expected official native SDK {runtimeInfo.OfficialNativeSdkVersion} and C# wrapper {runtimeInfo.OfficialCSharpWrapperVersion}. " +
            $"Managed wrapper: {(runtimeInfo.IsManagedWrapperAvailable ? "found" : "missing")}; " +
            $"native OrbbecSDK.dll: {(runtimeInfo.IsNativeLibraryAvailable ? "found" : "missing")}. " +
            $"Detected native SDK: {runtimeInfo.DetectedNativeSdkVersion ?? "unknown"}. " +
            runtimeInfo.ErrorMessage;
    }
}
