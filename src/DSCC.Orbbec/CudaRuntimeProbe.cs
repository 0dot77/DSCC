namespace DSCC.Orbbec;

/// <summary>
/// Checks whether the CUDA/cuDNN runtime DLLs required by k4abt 1.1.x
/// (onnxruntime 1.10 CUDA execution provider: CUDA 11.4.x + cuDNN 8.2.x)
/// are reachable. They are not redistributed via NuGet; they come from the
/// Azure Kinect Body Tracking SDK 1.1.2 installer, a CUDA/cuDNN install, or
/// DLLs dropped into third_party/cuda-runtime-win-x64/bin.
/// </summary>
public static class CudaRuntimeProbe
{
    private static readonly string[] RequiredLibraries =
    [
        "cudart64_110.dll",
        "cublas64_11.dll",
        "cudnn64_8.dll"
    ];

    public static bool IsLikelyAvailable(string? baseDirectory = null)
    {
        return FindMissingLibraries(baseDirectory).Count == 0;
    }

    public static IReadOnlyList<string> FindMissingLibraries(string? baseDirectory = null)
    {
        var roots = new List<string>
        {
            string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : Path.GetFullPath(baseDirectory)
        };

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        roots.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var missing = new List<string>();
        foreach (var library in RequiredLibraries)
        {
            if (!roots.Any(root => SafeExists(root, library)))
            {
                missing.Add(library);
            }
        }

        return missing;
    }

    public static string Describe(string? baseDirectory = null)
    {
        var missing = FindMissingLibraries(baseDirectory);
        return missing.Count == 0
            ? "CUDA runtime libraries found"
            : $"CUDA runtime libraries missing: {string.Join(", ", missing)} " +
              "(install Azure Kinect Body Tracking SDK 1.1.2 or copy CUDA 11.4/cuDNN 8.2 DLLs into the app folder)";
    }

    private static bool SafeExists(string directory, string fileName)
    {
        try
        {
            return File.Exists(Path.Combine(directory, fileName));
        }
        catch
        {
            return false;
        }
    }
}
