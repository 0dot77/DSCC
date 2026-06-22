using DSCC.Protocol;
using DSCC.Replay;
using DSCC.Transport;
using System.Globalization;

// Avatar test driver: streams FakeSkeletonFrameGenerator output to the Unity
// skeleton port using the same wire conventions as the live app (K4A camera
// space, then MirrorPerformerFacingCamera like mirrorSkeletonX=true).
//   dotnet run --project tools/DsccFakeSender -- [host] [port] [stationId] [activeSeconds] [serialTemplate]
//   dotnet run --project tools/DsccFakeSender -- 127.0.0.1 55010 all 60
//   dotnet run --project tools/DsccFakeSender -- 127.0.0.1 55010 1,2 60 DUPLICATE-SERIAL

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 ? int.Parse(args[1]) : ProtocolConstants.DefaultSkeletonPort;
var stationIds = args.Length > 2 ? ParseStationIds(args[2]) : [1];
var activeSeconds = args.Length > 3 ? double.Parse(args[3]) : 600;
var serialTemplate = args.Length > 4 ? args[4] : "FAKE-SENDER-{0:000}";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await using var sender = new UdpMessagePackSender(host, port);
Console.WriteLine(
    $"[fake-sender] streaming stations {string.Join(",", stationIds)} to udp://{host}:{port} " +
    $"(active {activeSeconds}s, 30fps, looped)");

long sent = 0;
var generator = new FakeSkeletonFrameGenerator();

try
{
    while (!cts.IsCancellationRequested)
    {
        var sequences = stationIds
            .Select(stationId => generator.CreateSequence(CreateOptions(stationId, activeSeconds, serialTemplate)))
            .ToArray();
        var maxFrames = sequences.Max(sequence => sequence.Count);
        var frameDelay = TimeSpan.FromSeconds(1d / 30);

        for (var frameIndex = 0; frameIndex < maxFrames && !cts.IsCancellationRequested; frameIndex++)
        {
            StationSkeletonFrame? sampleFrame = null;
            foreach (var sequence in sequences)
            {
                if (frameIndex >= sequence.Count)
                {
                    continue;
                }

                var frame = sequence[frameIndex];
                sampleFrame ??= frame;
                var wireFrame = SkeletonFrameTransforms.MirrorPerformerFacingCamera(
                    ReplayFrameConventions.ToK4aCameraConvention(frame));
                await sender.SendAsync(wireFrame, cts.Token);
                sent++;
            }

            if (frameIndex % 60 == 0 && sampleFrame is not null)
            {
                Console.WriteLine(
                    $"[fake-sender] sent={sent} stations={stationIds.Count} state={sampleFrame.State} " +
                    $"hasPlayer={sampleFrame.HasPlayer} pelvis=({sampleFrame.PelvisLocal.X:0.00}," +
                    $"{sampleFrame.PelvisLocal.Y:0.00},{sampleFrame.PelvisLocal.Z:0.00})");
            }

            await Task.Delay(frameDelay, cts.Token);
        }

        Console.WriteLine("[fake-sender] sequence finished, looping");
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C
}

Console.WriteLine($"[fake-sender] stopped after {sent} frames");

static FakeSkeletonFrameOptions CreateOptions(int stationId, double activeSeconds, string serialTemplate)
{
    return new FakeSkeletonFrameOptions
    {
        StationId = stationId,
        CameraSerial = FormatSerial(serialTemplate, stationId),
        DeviceType = "FakeSender",
        Fps = 30,
        WarmupOutsideDuration = TimeSpan.FromSeconds(0.5),
        EnterDuration = TimeSpan.FromSeconds(0.5),
        ActiveDuration = TimeSpan.FromSeconds(activeSeconds),
        LostDuration = TimeSpan.FromSeconds(1.5),
        ExitedDuration = TimeSpan.FromSeconds(1),
        EmptyTailDuration = TimeSpan.FromSeconds(0.5),
        ActiveSwayMeters = 0.18f
    };
}

static string FormatSerial(string serialTemplate, int stationId)
{
    return string.Format(CultureInfo.InvariantCulture, serialTemplate, stationId);
}

static IReadOnlyList<int> ParseStationIds(string value)
{
    if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
    {
        return [1, 2, 3, 4];
    }

    var stations = value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(int.Parse)
        .Distinct()
        .Order()
        .ToArray();

    if (stations.Length == 0)
    {
        throw new ArgumentException("At least one station id is required.", nameof(value));
    }

    if (stations.Any(stationId => stationId <= 0))
    {
        throw new ArgumentOutOfRangeException(nameof(value), "Station ids must be greater than zero.");
    }

    return stations;
}
