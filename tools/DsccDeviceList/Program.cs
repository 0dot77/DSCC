using System.Text.Json;
using System.Text.Json.Nodes;
using DSCC.Orbbec;

DeviceListOptions options;
try
{
    options = DeviceListOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"[fail] {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

if (options.ShowHelp)
{
    Console.WriteLine(DeviceListOptions.Usage);
    return;
}

if (options.CheckRuntime)
{
    var runtimeFailures = ValidateRuntime(options);
    if (runtimeFailures.Count > 0)
    {
        Console.WriteLine("[fail] runtime validation failed");
        foreach (var failure in runtimeFailures)
        {
            Console.WriteLine($"[fail] {failure}");
        }

        Environment.ExitCode = 2;
        return;
    }

    Console.WriteLine("[pass] runtime validation passed");
    if (!options.RequiresDeviceDiscovery)
    {
        return;
    }
}

var discovery = new OrbbecDeviceDiscovery(options: new OrbbecDeviceDiscoveryOptions
{
    BackendMode = options.BackendMode,
    AllowPlaceholderFallback = options.AllowPlaceholderFallback,
    EnableNetworkDeviceEnumeration = options.EnableNetworkDeviceEnumeration
});

IReadOnlyList<OrbbecDeviceInfo> devices;
try
{
    devices = await discovery.DiscoverAsync();
}
catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or OrbbecSdkUnavailableException)
{
    Console.Error.WriteLine($"[fail] {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

if (options.OutputJson)
{
    Console.WriteLine(JsonSerializer.Serialize(
        devices.Select(DeviceReport.FromDevice),
        new JsonSerializerOptions { WriteIndented = true }));
}
else
{
    Console.WriteLine($"[devices] backend={options.BackendMode} count={devices.Count}");
    foreach (var (device, index) in devices.Select((device, index) => (device, index + 1)))
    {
        Console.WriteLine(
            $"[{index}] type={device.DeviceType} serial={Display(device.Serial)} " +
            $"connection={device.Connection} placeholder={device.IsPlaceholder} " +
            $"name={Display(device.DisplayName)} native={Display(device.NativeName)} " +
            $"ip={Display(device.IpAddress)} firmware={Display(device.FirmwareVersion)}");
    }
}

if (!string.IsNullOrWhiteSpace(options.PinCommandConfigPath))
{
    PrintPinCommandTemplate(options.PinCommandConfigPath, devices);
}

List<string> failures;
try
{
    failures = Validate(options, devices).ToList();
    failures.AddRange(ValidateSerialPinning(options, devices));
}
catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"[fail] {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

if (failures.Count > 0)
{
    Console.WriteLine("[fail] device validation failed");
    foreach (var failure in failures)
    {
        Console.WriteLine($"[fail] {failure}");
    }

    Environment.ExitCode = 2;
    return;
}

if (options.HasSerialPinning)
{
    var result = PinSerialsToConfig(options.PinConfigPath, options.SerialPins);
    Console.WriteLine($"[pin] wrote {result.ConfigPath}");
    Console.WriteLine($"[pin] backup {result.BackupPath}");
}

if (options.ValidationEnabled)
{
    Console.WriteLine("[pass] device validation passed");
}

static IReadOnlyList<string> ValidateRuntime(DeviceListOptions options)
{
    var failures = new List<string>();
    var runtime = K4aBodyTrackingRuntimeProbe.Probe();
    Console.WriteLine($"[runtime] {runtime.Status}");
    if (!runtime.IsAvailable)
    {
        failures.Add(runtime.Status);
    }

    if (options.RequireCudaRuntime)
    {
        var cudaStatus = CudaRuntimeProbe.Describe();
        Console.WriteLine($"[runtime] {cudaStatus}");
        if (!CudaRuntimeProbe.IsLikelyAvailable())
        {
            failures.Add(cudaStatus);
        }
    }

    return failures;
}

static IReadOnlyList<string> Validate(DeviceListOptions options, IReadOnlyList<OrbbecDeviceInfo> devices)
{
    var failures = new List<string>();
    if (options.RequiredCount is { } requiredCount && devices.Count != requiredCount)
    {
        failures.Add($"expected {requiredCount} devices, found {devices.Count}");
    }

    if (options.RequiredDeviceType is { } requiredType)
    {
        foreach (var device in devices.Where(device => device.DeviceType != requiredType))
        {
            failures.Add($"serial {Display(device.Serial)} expected type {requiredType}, found {device.DeviceType}");
        }
    }

    if (options.FailPlaceholders)
    {
        foreach (var device in devices.Where(device => device.IsPlaceholder))
        {
            failures.Add($"placeholder device was returned: {Display(device.Serial)}");
        }
    }

    if (!string.IsNullOrWhiteSpace(options.ConfigPath))
    {
        var expectedSerials = ReadSerialsFromConfig(options.ConfigPath);
        var observedSerials = devices
            .Select(device => device.Serial)
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (stationId, serial) in expectedSerials.OrderBy(pair => pair.Key))
        {
            if (!observedSerials.Contains(serial))
            {
                failures.Add($"station {stationId} configured serial {serial} was not found in connected devices");
            }
        }
    }

    return failures;
}

static IReadOnlyList<string> ValidateSerialPinning(DeviceListOptions options, IReadOnlyList<OrbbecDeviceInfo> devices)
{
    var failures = new List<string>();
    if (!options.HasSerialPinning)
    {
        return failures;
    }

    var enabledStationIds = ReadEnabledStationIdsFromConfig(options.PinConfigPath);
    foreach (var stationId in enabledStationIds)
    {
        if (!options.SerialPins.ContainsKey(stationId))
        {
            failures.Add($"station {stationId} is missing from --pin-serials");
        }
    }

    foreach (var stationId in options.SerialPins.Keys.Except(enabledStationIds).Order())
    {
        failures.Add($"--pin-serials contains station {stationId}, but it is not enabled in {options.PinConfigPath}");
    }

    foreach (var duplicate in options.SerialPins
                 .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                 .Where(group => group.Count() > 1)
                 .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
    {
        failures.Add(
            $"--pin-serials uses serial {duplicate.Key} for multiple stations: " +
            string.Join(",", duplicate.Select(pair => pair.Key).Order()));
    }

    if (options.AllowUnconnectedPin)
    {
        return failures;
    }

    var connectedBySerial = new Dictionary<string, OrbbecDeviceInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (var device in devices.Where(device => !string.IsNullOrWhiteSpace(device.Serial)))
    {
        if (connectedBySerial.ContainsKey(device.Serial))
        {
            failures.Add($"connected device serial {device.Serial} appears more than once");
            continue;
        }

        connectedBySerial[device.Serial] = device;
    }

    foreach (var (stationId, serial) in options.SerialPins.OrderBy(pair => pair.Key))
    {
        if (!connectedBySerial.TryGetValue(serial, out var device))
        {
            failures.Add($"station {stationId} pin serial {serial} is not connected");
            continue;
        }

        if (options.RequiredDeviceType is { } requiredType && device.DeviceType != requiredType)
        {
            failures.Add($"station {stationId} pin serial {serial} expected type {requiredType}, found {device.DeviceType}");
        }

        if (options.FailPlaceholders && device.IsPlaceholder)
        {
            failures.Add($"station {stationId} pin serial {serial} is a placeholder device");
        }
    }

    return failures;
}

static void PrintPinCommandTemplate(string configPath, IReadOnlyList<OrbbecDeviceInfo> devices)
{
    var stationIds = ReadEnabledStationIdsFromConfig(configPath);
    var candidateDevices = devices
        .Where(device => !string.IsNullOrWhiteSpace(device.Serial))
        .OrderBy(device => device.Serial, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Console.WriteLine("[pin-template] verify physical left-to-right camera order before running any pin command");
    Console.WriteLine($"[pin-template] enabled stations from config: {string.Join(",", stationIds)}");
    foreach (var (device, index) in candidateDevices.Select((device, index) => (device, index + 1)))
    {
        Console.WriteLine(
            $"[pin-template] candidate {index}: serial={device.Serial} type={device.DeviceType} " +
            $"placeholder={device.IsPlaceholder} connection={device.Connection}");
    }

    if (candidateDevices.Length < stationIds.Count)
    {
        Console.WriteLine(
            $"[pin-template] not enough connected devices to fill all stations: " +
            $"{candidateDevices.Length}/{stationIds.Count}");
        return;
    }

    if (candidateDevices.Any(device => device.IsPlaceholder))
    {
        Console.WriteLine("[pin-template] placeholders were discovered; do not pin these for field use");
    }

    if (candidateDevices.Any(device => device.DeviceType != OrbbecDeviceType.FemtoMega))
    {
        Console.WriteLine("[pin-template] at least one discovered device is not FemtoMega; field rig expects only FemtoMega");
    }

    var placeholderMapping = CreatePhysicalOrderPlaceholderMapping(stationIds);
    Console.WriteLine("[pin-template] copy this command, then replace placeholders with the physical station serials:");
    Console.WriteLine(
        "dotnet run --project tools\\DsccDeviceList -- --field --pin-config " +
        $"{QuotePowerShellArgument(configPath)} --pin-serials {QuotePowerShellArgument(placeholderMapping)}");

    var sortedSerialMapping = string.Join(
        ",",
        stationIds.Select((stationId, index) => $"{stationId}={candidateDevices[index].Serial.Trim()}"));
    Console.WriteLine("[pin-template] serial-sorted candidate only; do not run unless it matches physical left-to-right order:");
    Console.WriteLine($"[pin-template] {sortedSerialMapping}");
}

static string CreatePhysicalOrderPlaceholderMapping(IReadOnlyList<int> stationIds)
{
    string[] labels =
    [
        "LEFT_SERIAL",
        "MID_LEFT_SERIAL",
        "MID_RIGHT_SERIAL",
        "RIGHT_SERIAL"
    ];

    return string.Join(
        ",",
        stationIds.Select((stationId, index) =>
            $"{stationId}={(index < labels.Length ? labels[index] : $"STATION_{stationId}_SERIAL")}"));
}

static IReadOnlyDictionary<int, string> ReadSerialsFromConfig(string path)
{
    using var stream = File.OpenRead(path);
    using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    });

    if (!document.RootElement.TryGetProperty("stations", out var stationsElement) ||
        stationsElement.ValueKind != JsonValueKind.Array)
    {
        throw new ArgumentException($"Config file does not contain a stations array: {path}", nameof(path));
    }

    var serials = new Dictionary<int, string>();
    var missingSerialStations = new List<int>();
    foreach (var stationElement in stationsElement.EnumerateArray())
    {
        var stationId = GetInt32(stationElement, "stationId");
        var enabled = GetOptionalBool(stationElement, "enabled") ?? true;
        if (!enabled)
        {
            continue;
        }

        var serial = GetNestedString(stationElement, "device", "serial");
        if (string.IsNullOrWhiteSpace(serial))
        {
            serial = GetNestedString(stationElement, "calibration", "cameraSerial");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            missingSerialStations.Add(stationId);
            continue;
        }

        serials[stationId] = serial.Trim();
    }

    if (missingSerialStations.Count > 0)
    {
        throw new ArgumentException(
            $"Enabled stations missing device.serial/calibration.cameraSerial in {path}: " +
            string.Join(",", missingSerialStations),
            nameof(path));
    }

    return serials;
}

static IReadOnlyList<int> ReadEnabledStationIdsFromConfig(string path)
{
    using var stream = File.OpenRead(path);
    using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    });

    if (!document.RootElement.TryGetProperty("stations", out var stationsElement) ||
        stationsElement.ValueKind != JsonValueKind.Array)
    {
        throw new ArgumentException($"Config file does not contain a stations array: {path}", nameof(path));
    }

    return stationsElement.EnumerateArray()
        .Where(stationElement => GetOptionalBool(stationElement, "enabled") ?? true)
        .Select(stationElement => GetInt32(stationElement, "stationId"))
        .Order()
        .ToArray();
}

static PinConfigResult PinSerialsToConfig(string path, IReadOnlyDictionary<int, string> serialPins)
{
    var json = File.ReadAllText(path);
    var root = JsonNode.Parse(
        json,
        documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        }) as JsonObject ?? throw new ArgumentException($"Config file root must be an object: {path}", nameof(path));

    if (root["stations"] is not JsonArray stations)
    {
        throw new ArgumentException($"Config file does not contain a stations array: {path}", nameof(path));
    }

    root["autoAssignDevicesOnStart"] = false;
    foreach (var stationNode in stations)
    {
        if (stationNode is not JsonObject station)
        {
            continue;
        }

        var stationId = station["stationId"]?.GetValue<int>()
            ?? throw new ArgumentException("station is missing stationId", nameof(path));
        if (!serialPins.TryGetValue(stationId, out var serial))
        {
            continue;
        }

        if (station["device"] is not JsonObject device)
        {
            device = new JsonObject();
            station["device"] = device;
        }

        if (station["calibration"] is not JsonObject calibration)
        {
            calibration = new JsonObject();
            station["calibration"] = calibration;
        }

        device["serial"] = serial;
        calibration["cameraSerial"] = serial;
    }

    var backupPath = CreateBackupPath(path);
    File.Copy(path, backupPath);
    File.WriteAllText(
        path,
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

    return new PinConfigResult(path, backupPath);
}

static string CreateBackupPath(string path)
{
    var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var candidate = $"{path}.bak-{timestamp}";
    if (!File.Exists(candidate))
    {
        return candidate;
    }

    for (var index = 1; index < 1000; index++)
    {
        candidate = $"{path}.bak-{timestamp}-{index}";
        if (!File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new IOException($"Could not create a unique backup path for {path}");
}

static int GetInt32(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.Number ||
        !property.TryGetInt32(out var value))
    {
        throw new ArgumentException($"Missing or invalid integer property: {propertyName}");
    }

    return value;
}

static bool? GetOptionalBool(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    return property.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ArgumentException($"Invalid boolean property: {propertyName}")
    };
}

static string GetNestedString(JsonElement element, string objectName, string propertyName)
{
    if (!element.TryGetProperty(objectName, out var nested) ||
        nested.ValueKind != JsonValueKind.Object ||
        !nested.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String)
    {
        return string.Empty;
    }

    return property.GetString() ?? string.Empty;
}

static string Display(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
}

static string QuotePowerShellArgument(string value)
{
    if (value.All(character =>
            char.IsLetterOrDigit(character) ||
            character is '_' or '-' or '.' or '/' or '\\' or ':' or '=' or ','))
    {
        return value;
    }

    return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

internal sealed record DeviceReport(
    string DeviceType,
    string Serial,
    string Connection,
    string DisplayName,
    string? FirmwareVersion,
    string? HardwareVersion,
    string? IpAddress,
    string? NativeName,
    bool IsPlaceholder)
{
    public static DeviceReport FromDevice(OrbbecDeviceInfo device)
    {
        return new DeviceReport(
            device.DeviceType.ToString(),
            device.Serial,
            device.Connection.ToString(),
            device.DisplayName,
            device.FirmwareVersion,
            device.HardwareVersion,
            device.IpAddress,
            device.NativeName,
            device.IsPlaceholder);
    }
}

internal sealed record PinConfigResult(string ConfigPath, string BackupPath);

internal sealed class DeviceListOptions
{
    public const string Usage = """
Usage:
  dotnet run --project tools/DsccDeviceList -- [options]

Options:
  --field                         Require the field device shape: K4A wrapper,
                                  four FemtoMega devices, no placeholders.
  --backend <auto|k4a-wrapper|native-sdk-v2|placeholder>
                                  Discovery backend. Default: k4a-wrapper.
  --config <path>                 Verify configured station serials are connected.
  --runtime                       Check bundled K4A body tracking runtime files.
  --require-cuda                  Require CUDA/cuDNN runtime DLLs for Cuda mode.
                                  Implies --runtime.
  --pin-config <path>             Write station serial pins to this config.
  --pin-serials <1=SER,2=SER,...> Explicit station-to-serial pins to write.
  --print-pin-command <path>      Print a candidate --pin-config command from
                                  discovered serials without writing config.
  --allow-unconnected-pin         Allow writing pins without connected serials.
                                  Use only before cameras are available.
  --require-count <count>         Require exactly this many devices.
  --require-type <FemtoMega|FemtoBolt|Unknown>
                                  Require every discovered device to have this type.
  --allow-placeholder-fallback    Allow placeholder fallback for auto/native backends.
  --fail-placeholders             Fail if any placeholder device is returned.
  --network                       Enable native SDK network device enumeration.
  --json                          Print device list as JSON.
  -h, --help                      Print this help.
""";

    public OrbbecBackendMode BackendMode { get; private set; } = OrbbecBackendMode.K4AWrapper;
    public bool AllowPlaceholderFallback { get; private set; }
    public bool EnableNetworkDeviceEnumeration { get; private set; }
    public bool OutputJson { get; private set; }
    public bool ShowHelp { get; private set; }
    public int? RequiredCount { get; private set; }
    public OrbbecDeviceType? RequiredDeviceType { get; private set; }
    public bool FailPlaceholders { get; private set; }
    public bool CheckRuntime { get; private set; }
    public bool RequireCudaRuntime { get; private set; }
    public string ConfigPath { get; private set; } = string.Empty;
    public string PinConfigPath { get; private set; } = string.Empty;
    public string PinCommandConfigPath { get; private set; } = string.Empty;
    public IReadOnlyDictionary<int, string> SerialPins { get; private set; } = new Dictionary<int, string>();
    public bool AllowUnconnectedPin { get; private set; }
    public bool HasSerialPinning =>
        !string.IsNullOrWhiteSpace(PinConfigPath) || SerialPins.Count > 0;

    public bool RequiresDeviceDiscovery =>
        OutputJson ||
        RequiredCount.HasValue ||
        RequiredDeviceType.HasValue ||
        FailPlaceholders ||
        !string.IsNullOrWhiteSpace(ConfigPath) ||
        !string.IsNullOrWhiteSpace(PinCommandConfigPath) ||
        HasSerialPinning;

    public bool ValidationEnabled =>
        RequiredCount.HasValue ||
        RequiredDeviceType.HasValue ||
        FailPlaceholders ||
        CheckRuntime ||
        RequireCudaRuntime ||
        !string.IsNullOrWhiteSpace(ConfigPath) ||
        HasSerialPinning;

    public static DeviceListOptions Parse(string[] args)
    {
        var options = new DeviceListOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (IsHelp(token))
            {
                options.ShowHelp = true;
                return options;
            }

            var (name, inlineValue) = SplitOption(token);
            switch (name)
            {
                case "--field":
                    options.BackendMode = OrbbecBackendMode.K4AWrapper;
                    options.RequiredCount = 4;
                    options.RequiredDeviceType = OrbbecDeviceType.FemtoMega;
                    options.FailPlaceholders = true;
                    break;
                case "--backend":
                    options.BackendMode = ParseBackend(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--config":
                    options.ConfigPath = ReadOptionValue(args, ref index, inlineValue, name);
                    break;
                case "--runtime":
                    options.CheckRuntime = true;
                    break;
                case "--require-cuda":
                    options.CheckRuntime = true;
                    options.RequireCudaRuntime = true;
                    break;
                case "--pin-config":
                    options.PinConfigPath = ReadOptionValue(args, ref index, inlineValue, name);
                    break;
                case "--pin-serials":
                    options.SerialPins = ParseSerialPins(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--print-pin-command":
                    options.PinCommandConfigPath = ReadOptionValue(args, ref index, inlineValue, name);
                    break;
                case "--allow-unconnected-pin":
                    options.AllowUnconnectedPin = true;
                    break;
                case "--require-count":
                    options.RequiredCount = int.Parse(ReadOptionValue(args, ref index, inlineValue, name));
                    if (options.RequiredCount < 0)
                    {
                        throw new ArgumentOutOfRangeException(name, "Device count cannot be negative.");
                    }

                    break;
                case "--require-type":
                    options.RequiredDeviceType = ParseDeviceType(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--allow-placeholder-fallback":
                    options.AllowPlaceholderFallback = true;
                    break;
                case "--fail-placeholders":
                    options.FailPlaceholders = true;
                    break;
                case "--network":
                    options.EnableNetworkDeviceEnumeration = true;
                    break;
                case "--json":
                    options.OutputJson = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {token}");
            }
        }

        if (options.HasSerialPinning)
        {
            if (string.IsNullOrWhiteSpace(options.PinConfigPath))
            {
                throw new ArgumentException("--pin-config is required when writing station serial pins.");
            }

            if (options.SerialPins.Count == 0)
            {
                throw new ArgumentException("--pin-serials is required when writing station serial pins.");
            }
        }

        return options;
    }

    private static OrbbecBackendMode ParseBackend(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => OrbbecBackendMode.Auto,
            "k4a-wrapper" or "k4a" => OrbbecBackendMode.K4AWrapper,
            "native-sdk-v2" or "native" or "sdk-v2" => OrbbecBackendMode.NativeSdkV2,
            "placeholder" => OrbbecBackendMode.Placeholder,
            _ => throw new ArgumentException($"Unknown backend: {value}")
        };
    }

    private static OrbbecDeviceType ParseDeviceType(string value)
    {
        return Enum.TryParse<OrbbecDeviceType>(value, ignoreCase: true, out var deviceType)
            ? deviceType
            : throw new ArgumentException($"Unknown device type: {value}");
    }

    private static IReadOnlyDictionary<int, string> ParseSerialPins(string value)
    {
        var serials = new Dictionary<int, string>();
        foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == pair.Length - 1)
            {
                throw new ArgumentException($"Invalid serial mapping: {pair}", nameof(value));
            }

            var stationId = int.Parse(pair[..separator]);
            if (stationId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Station ids must be greater than zero.");
            }

            var serial = pair[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(serial))
            {
                throw new ArgumentException($"Station {stationId} has an empty serial.", nameof(value));
            }

            if (serials.TryGetValue(stationId, out var existingSerial) &&
                !string.Equals(existingSerial, serial, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Conflicting serial pin for station {stationId}: {existingSerial} vs {serial}",
                    nameof(value));
            }

            serials[stationId] = serial;
        }

        if (serials.Count == 0)
        {
            throw new ArgumentException("At least one station serial pin is required.", nameof(value));
        }

        return serials;
    }

    private static bool IsHelp(string value)
    {
        return string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Name, string? Value) SplitOption(string token)
    {
        var equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            return (token, null);
        }

        return (token[..equalsIndex], token[(equalsIndex + 1)..]);
    }

    private static string ReadOptionValue(string[] args, ref int index, string? inlineValue, string optionName)
    {
        if (!string.IsNullOrWhiteSpace(inlineValue))
        {
            return inlineValue;
        }

        index++;
        if (index >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}");
        }

        return args[index];
    }
}
