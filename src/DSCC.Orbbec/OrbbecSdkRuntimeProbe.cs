using System.Reflection;
using System.Runtime.InteropServices;

namespace DSCC.Orbbec;

public static class OrbbecSdkRuntimeProbe
{
    public const string OfficialNativeSdkVersion = "2.8.6";
    public const string OfficialCSharpWrapperVersion = "2.4.0";
    public const string FemtoBoltRecommendedFirmware = "1.1.3";
    public const string FemtoMegaRecommendedFirmware = "1.3.1";

    private static readonly string[] ManagedWrapperFileNames =
    [
        "OrbbecSDK_CSharp.dll",
        "Orbbec.dll"
    ];

    private static readonly string[] NativeLibraryFileNames =
    [
        "OrbbecSDK.dll",
        "OrbbecSDK_d.dll"
    ];

    public static OrbbecSdkRuntimeInfo Probe(OrbbecDeviceDiscoveryOptions? options = null)
    {
        options ??= new OrbbecDeviceDiscoveryOptions();

        Assembly? managedAssembly = null;
        string? managedPath = null;
        string? nativePath = null;
        string? errorMessage = null;

        try
        {
            (managedAssembly, managedPath) = ResolveManagedWrapper(options);
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
        }

        try
        {
            nativePath = ResolveNativeLibrary(options);
        }
        catch (Exception exception)
        {
            errorMessage = Join(errorMessage, exception.Message);
        }

        return new OrbbecSdkRuntimeInfo
        {
            IsManagedWrapperAvailable = managedAssembly is not null,
            IsNativeLibraryAvailable = nativePath is not null,
            ManagedWrapperPath = managedPath,
            NativeLibraryPath = nativePath,
            DetectedManagedWrapperVersion = managedAssembly?.GetName().Version?.ToString(),
            DetectedNativeSdkVersion = TryGetNativeSdkVersion(managedAssembly),
            ErrorMessage = errorMessage,
            ManagedWrapperAssembly = managedAssembly
        };
    }

    private static string? TryGetNativeSdkVersion(Assembly? managedAssembly)
    {
        if (managedAssembly?.GetType("Orbbec.Version", throwOnError: false) is not { } versionType)
        {
            return null;
        }

        try
        {
            var major = versionType.GetMethod("GetMajorVersion")?.Invoke(null, null);
            var minor = versionType.GetMethod("GetMinorVersion")?.Invoke(null, null);
            var patch = versionType.GetMethod("GetPatchVersion")?.Invoke(null, null);
            return major is null || minor is null || patch is null
                ? null
                : $"{major}.{minor}.{patch}";
        }
        catch
        {
            return null;
        }
    }

    private static (Assembly? Assembly, string? Path) ResolveManagedWrapper(OrbbecDeviceDiscoveryOptions options)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType("Orbbec.Context", throwOnError: false) is not null)
            {
                return (assembly, assembly.Location);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.ManagedWrapperAssemblyPath))
        {
            if (!File.Exists(options.ManagedWrapperAssemblyPath))
            {
                return (null, null);
            }

            var fullPath = Path.GetFullPath(options.ManagedWrapperAssemblyPath);
            return (Assembly.LoadFrom(fullPath), fullPath);
        }

        foreach (var directory in CandidateDirectories(options))
        {
            foreach (var fileName in ManagedWrapperFileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return (Assembly.LoadFrom(candidate), candidate);
                }
            }
        }

        foreach (var assemblyName in ManagedWrapperFileNames
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>())
        {
            try
            {
                var assembly = Assembly.Load(new AssemblyName(assemblyName));
                if (assembly.GetType("Orbbec.Context", throwOnError: false) is not null)
                {
                    return (assembly, assembly.Location);
                }
            }
            catch
            {
                // Assembly probing is best effort; absence is reported in the runtime info.
            }
        }

        return (null, null);
    }

    private static string? ResolveNativeLibrary(OrbbecDeviceDiscoveryOptions options)
    {
        foreach (var directory in CandidateDirectories(options))
        {
            foreach (var fileName in NativeLibraryFileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate) && TryLoadNative(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (var libraryName in NativeLibraryFileNames
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>())
        {
            if (TryLoadNative(libraryName))
            {
                return libraryName;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories(OrbbecDeviceDiscoveryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.NativeLibraryDirectory))
        {
            yield return Path.GetFullPath(options.NativeLibraryDirectory);
        }

        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }

    private static bool TryLoadNative(string libraryNameOrPath)
    {
        if (!NativeLibrary.TryLoad(libraryNameOrPath, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }

    private static string? Join(string? first, string second)
    {
        return string.IsNullOrWhiteSpace(first) ? second : $"{first}; {second}";
    }
}
