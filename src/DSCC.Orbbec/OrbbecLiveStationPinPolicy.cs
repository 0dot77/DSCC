namespace DSCC.Orbbec;

public static class OrbbecLiveStationPinPolicy
{
    public static IReadOnlyList<string> ValidateRequiredPins(
        IEnumerable<OrbbecStationCameraPin> stations,
        IEnumerable<OrbbecDeviceInfo> connectedDevices)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(connectedDevices);

        var failures = new List<string>();
        var connectedBySerial = BuildConnectedDeviceMap(connectedDevices, failures);
        var serialToStation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var station in stations
                     .Where(station => station.Enabled)
                     .OrderBy(station => station.StationId))
        {
            var serial = station.CameraSerial;
            if (string.IsNullOrWhiteSpace(serial))
            {
                failures.Add($"station {station.StationId} has no pinned camera serial");
                continue;
            }

            serial = serial.Trim();
            if (serialToStation.TryGetValue(serial, out var otherStationId))
            {
                failures.Add($"stations {otherStationId} and {station.StationId} share camera serial {serial}");
                continue;
            }

            serialToStation[serial] = station.StationId;
            if (!connectedBySerial.TryGetValue(serial, out var deviceInfo))
            {
                failures.Add($"station {station.StationId} pinned camera serial {serial} is not connected");
                continue;
            }

            if (!OrbbecDeviceAssignmentPolicy.IsCompatibleWithStationType(deviceInfo, station.DeviceType))
            {
                failures.Add($"station {station.StationId} expects {station.DeviceType}, but {serial} is {deviceInfo.DeviceType}");
            }
        }

        return failures;
    }

    private static Dictionary<string, OrbbecDeviceInfo> BuildConnectedDeviceMap(
        IEnumerable<OrbbecDeviceInfo> connectedDevices,
        List<string> failures)
    {
        var connectedBySerial = new Dictionary<string, OrbbecDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in connectedDevices)
        {
            if (string.IsNullOrWhiteSpace(device.Serial))
            {
                continue;
            }

            var serial = device.Serial.Trim();
            if (connectedBySerial.ContainsKey(serial))
            {
                failures.Add($"connected camera serial {serial} appears more than once");
                continue;
            }

            connectedBySerial[serial] = device;
        }

        return connectedBySerial;
    }
}
