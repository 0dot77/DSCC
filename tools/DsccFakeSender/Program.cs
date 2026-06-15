using DSCC.Protocol;
using DSCC.Replay;
using DSCC.Transport;

// Avatar test driver: streams FakeSkeletonFrameGenerator output to the Unity
// skeleton port using the same wire conventions as the live app (K4A camera
// space, then MirrorPerformerFacingCamera like mirrorSkeletonX=true).
//   dotnet run --project tools/DsccFakeSender -- [host] [port] [stationId] [activeSeconds]

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 ? int.Parse(args[1]) : ProtocolConstants.DefaultSkeletonPort;
var stationId = args.Length > 2 ? int.Parse(args[2]) : 1;
var activeSeconds = args.Length > 3 ? double.Parse(args[3]) : 600;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var options = new FakeSkeletonFrameOptions
{
    StationId = stationId,
    CameraSerial = "FAKE-SENDER-001",
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

await using var sender = new UdpMessagePackSender(host, port);
Console.WriteLine($"[fake-sender] streaming station {stationId} to udp://{host}:{port} (active {activeSeconds}s, 30fps, looped)");

long sent = 0;
var generator = new FakeSkeletonFrameGenerator();

try
{
    while (!cts.IsCancellationRequested)
    {
        await foreach (var frame in generator.PlaySequenceAsync(options, cts.Token))
        {
            var wireFrame = SkeletonFrameTransforms.MirrorPerformerFacingCamera(
                ReplayFrameConventions.ToK4aCameraConvention(frame));
            await sender.SendAsync(wireFrame, cts.Token);
            sent++;

            if (sent % 60 == 0)
            {
                Console.WriteLine(
                    $"[fake-sender] sent={sent} state={frame.State} hasPlayer={frame.HasPlayer} " +
                    $"pelvis=({frame.PelvisLocal.X:0.00},{frame.PelvisLocal.Y:0.00},{frame.PelvisLocal.Z:0.00})");
            }
        }

        Console.WriteLine("[fake-sender] sequence finished, looping");
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C
}

Console.WriteLine($"[fake-sender] stopped after {sent} frames");
