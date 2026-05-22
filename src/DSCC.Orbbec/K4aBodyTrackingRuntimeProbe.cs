namespace DSCC.Orbbec;

public static class K4aBodyTrackingRuntimeProbe
{
    public const string OfficialK4aWrapperVersion = "2.0.12";
    public const string OfficialBodyTrackingPackageVersion = "1.1.2";
    public const string OfficialSensorPackageVersion = "1.4.1";

    public static K4aBodyTrackingRuntimeInfo Probe(string? baseDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);
        var wrapperDirectory = Path.Combine(root, "k4a-wrapper");

        var bodyTrackingAssembly = FindFirstExisting(root, "Microsoft.Azure.Kinect.BodyTracking.dll");
        var sensorAssembly = FindFirstExisting(root, "Microsoft.Azure.Kinect.Sensor.dll");
        var bodyTrackingLibrary = FindFirstExisting(root, "k4abt.dll");
        var onnxRuntime = FindFirstExisting(root, "onnxruntime.dll");
        var model = FindFirstExisting(root, "dnn_model_2_0_op11.onnx")
            ?? FindFirstExisting(root, "dnn_model_2_0_lite_op11.onnx");
        var wrapper = FindFirstExisting(wrapperDirectory, "k4a.dll");
        var wrapperDependency = FindFirstExisting(wrapperDirectory, "OrbbecSDK.dll");

        var missing = new List<string>();
        if (!IsBodyTrackingBuildEnabled())
        {
            missing.Add("x64 body tracking build is not enabled");
        }

        if (bodyTrackingAssembly is null)
        {
            missing.Add("Microsoft.Azure.Kinect.BodyTracking.dll is missing");
        }

        if (sensorAssembly is null)
        {
            missing.Add("Microsoft.Azure.Kinect.Sensor.dll is missing");
        }

        if (bodyTrackingLibrary is null)
        {
            missing.Add("k4abt.dll is missing");
        }

        if (onnxRuntime is null)
        {
            missing.Add("onnxruntime.dll is missing");
        }

        if (model is null)
        {
            missing.Add("Azure Kinect body tracking ONNX model is missing");
        }

        if (wrapper is null)
        {
            missing.Add("Orbbec K4A wrapper k4a.dll is missing");
        }

        if (wrapperDependency is null)
        {
            missing.Add("Orbbec K4A wrapper OrbbecSDK.dll is missing");
        }

        return new K4aBodyTrackingRuntimeInfo
        {
            IsBodyTrackingBuildEnabled = IsBodyTrackingBuildEnabled(),
            IsManagedBodyTrackingAssemblyAvailable = bodyTrackingAssembly is not null,
            IsManagedSensorAssemblyAvailable = sensorAssembly is not null,
            IsNativeBodyTrackingLibraryAvailable = bodyTrackingLibrary is not null,
            IsOnnxRuntimeAvailable = onnxRuntime is not null,
            IsBodyTrackingModelAvailable = model is not null,
            IsOrbbecK4aWrapperAvailable = wrapper is not null,
            IsOrbbecK4aWrapperDependencyAvailable = wrapperDependency is not null,
            ManagedBodyTrackingAssemblyPath = bodyTrackingAssembly,
            ManagedSensorAssemblyPath = sensorAssembly,
            NativeBodyTrackingLibraryPath = bodyTrackingLibrary,
            OnnxRuntimePath = onnxRuntime,
            BodyTrackingModelPath = model,
            OrbbecK4aWrapperPath = wrapper,
            OrbbecK4aWrapperDirectory = Directory.Exists(wrapperDirectory) ? wrapperDirectory : null,
            OrbbecK4aWrapperDependencyPath = wrapperDependency,
            MissingRequirements = missing
        };
    }

    private static bool IsBodyTrackingBuildEnabled()
    {
#if DSCC_K4A_BODY_TRACKING
        return true;
#else
        return false;
#endif
    }

    private static string? FindFirstExisting(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

}
