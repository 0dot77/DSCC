#if DSCC_K4A_BODY_TRACKING
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Azure.Kinect.Sensor;

namespace DSCC.Orbbec;

internal static class K4aWrapperNativeLoader
{
    private static int configured;
    private static string? wrapperDirectory;
    private static IntPtr wrapperOrbbecSdkHandle;
    private static IntPtr wrapperDepthEngineHandle;
    private static IntPtr k4aHandle;
    private static IntPtr k4aRecordHandle;

    public static void Configure(K4aBodyTrackingRuntimeInfo runtime)
    {
        if (Interlocked.Exchange(ref configured, 1) == 1)
        {
            return;
        }

        wrapperDirectory = runtime.OrbbecK4aWrapperDirectory;
        if (!string.IsNullOrWhiteSpace(wrapperDirectory))
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(wrapperDirectory, StringComparer.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", wrapperDirectory + Path.PathSeparator + path);
            }

            // The K4A wrapper is built against its own OrbbecSDK.dll. Load it
            // before k4a.dll so the process does not bind the wrapper to the
            // native SDK v2 DLL that DSCC also ships for depth-only preview.
            wrapperOrbbecSdkHandle = Preload(Path.Combine(wrapperDirectory, "OrbbecSDK.dll"));
            wrapperDepthEngineHandle = Preload(Path.Combine(wrapperDirectory, "depthengine_2_0.dll"));
            k4aHandle = Preload(Path.Combine(wrapperDirectory, "k4a.dll"));
            k4aRecordHandle = Preload(Path.Combine(wrapperDirectory, "k4arecord.dll"));
        }

        TrySetResolver(typeof(Device).Assembly, ResolveSensorNativeLibrary);
    }

    private static void TrySetResolver(Assembly assembly, DllImportResolver resolver)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, resolver);
        }
        catch (InvalidOperationException)
        {
            // A resolver can be registered only once per assembly.
        }
    }

    private static IntPtr ResolveSensorNativeLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (string.IsNullOrWhiteSpace(wrapperDirectory))
        {
            return IntPtr.Zero;
        }

        var normalized = Path.GetFileNameWithoutExtension(libraryName);
        var fileName = normalized switch
        {
            "k4a" => "k4a.dll",
            "k4arecord" => "k4arecord.dll",
            _ => null
        };
        if (fileName is null)
        {
            return IntPtr.Zero;
        }

        var path = Path.Combine(wrapperDirectory, fileName);
        return File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)
            ? handle
            : IntPtr.Zero;
    }

    private static IntPtr Preload(string path)
    {
        return File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)
            ? handle
            : IntPtr.Zero;
    }
}
#endif
