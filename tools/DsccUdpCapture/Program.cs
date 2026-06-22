using System.Net;
using System.Net.Sockets;
using DSCC.Protocol;
using DSCC.Replay;
using DSCC.Transport;
using MessagePack;

if (args.Length == 0 || IsHelp(args[0]))
{
    PrintUsage();
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

try
{
    return args[0].ToLowerInvariant() switch
    {
        "record" => await RecordAsync(args, cts.Token).ConfigureAwait(false),
        "replay" => await ReplayAsync(args, cts.Token).ConfigureAwait(false),
        _ => Fail($"Unknown command '{args[0]}'.")
    };
}
catch (OperationCanceledException)
{
    Console.WriteLine("[capture] canceled");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"[error] {exception.Message}");
    return 1;
}

static async Task<int> RecordAsync(string[] args, CancellationToken cancellationToken)
{
    if (args.Length != 4)
    {
        PrintUsage();
        return 1;
    }

    var port = ParsePort(args[1]);
    var durationSeconds = ParsePositiveDouble(args[2], "durationSeconds");
    var outputPath = args[3];
    var frames = new List<StationSkeletonFrame>();
    var stationCounts = new SortedDictionary<int, int>();
    var decodeErrors = 0;

    using var durationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    durationCts.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
    using var client = new UdpClient(new IPEndPoint(IPAddress.Any, port));

    Console.WriteLine($"[record] listening on udp:{port} for {durationSeconds:0.###}s");

    try
    {
        while (true)
        {
            UdpReceiveResult received;
            try
            {
                received = await client.ReceiveAsync(durationCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var frame = MessagePackSerializer.Deserialize<StationSkeletonFrame>(received.Buffer, cancellationToken: cancellationToken);
                frames.Add(frame);
                stationCounts[frame.StationId] = stationCounts.GetValueOrDefault(frame.StationId) + 1;
            }
            catch (Exception exception) when (exception is MessagePackSerializationException or InvalidOperationException)
            {
                decodeErrors++;
            }
        }
    }
    finally
    {
        await new SkeletonRecorder().RecordAsync(frames, outputPath, CancellationToken.None).ConfigureAwait(false);
    }

    Console.WriteLine(
        $"[record] wrote {frames.Count} frames to {Path.GetFullPath(outputPath)} " +
        $"stations={FormatStationCounts(stationCounts)} decodeErrors={decodeErrors}");
    return decodeErrors == 0 ? 0 : 2;
}

static async Task<int> ReplayAsync(string[] args, CancellationToken cancellationToken)
{
    if (args.Length < 4)
    {
        PrintUsage();
        return 1;
    }

    var filePath = args[1];
    var host = args[2];
    var port = ParsePort(args[3]);
    var loop = false;
    var preserveTiming = true;
    double? sendIntervalMilliseconds = null;

    for (var index = 4; index < args.Length; index++)
    {
        switch (args[index].ToLowerInvariant())
        {
            case "--loop":
                loop = true;
                break;
            case "--no-timing":
                preserveTiming = false;
                break;
            case "--send-interval-ms":
                if (index + 1 >= args.Length)
                {
                    return Fail("--send-interval-ms requires a value.");
                }

                sendIntervalMilliseconds = ParseNonNegativeDouble(args[++index], "sendIntervalMilliseconds");
                preserveTiming = false;
                break;
            default:
                return Fail($"Unknown replay option '{args[index]}'.");
        }
    }

    await using var sender = new UdpMessagePackSender(host, port);
    var source = new SkeletonReplaySource();
    long sent = 0;

    Console.WriteLine(
        $"[replay] sending {Path.GetFullPath(filePath)} to udp://{host}:{port} " +
        $"timing={(preserveTiming ? "preserved" : "off")} " +
        $"sendIntervalMs={(sendIntervalMilliseconds is null ? "capture" : sendIntervalMilliseconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))} " +
        $"loop={loop}");

    do
    {
        var frames = sendIntervalMilliseconds is null
            ? source.PlayAsync(filePath, preserveTiming, cancellationToken)
            : source.ReadFramesAsync(filePath, cancellationToken);

        await foreach (var frame in frames.ConfigureAwait(false))
        {
            await sender.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            sent++;

            if (sendIntervalMilliseconds is > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(sendIntervalMilliseconds.Value), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
    while (loop && !cancellationToken.IsCancellationRequested);

    Console.WriteLine($"[replay] sent {sent} frames");
    return 0;
}

static int ParsePort(string value)
{
    var port = int.Parse(value);
    if (port <= 0 || port > 65535)
    {
        throw new ArgumentOutOfRangeException(nameof(value), "Port must be 1..65535.");
    }

    return port;
}

static double ParsePositiveDouble(string value, string name)
{
    var result = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    if (result <= 0)
    {
        throw new ArgumentOutOfRangeException(name, "Value must be positive.");
    }

    return result;
}

static double ParseNonNegativeDouble(string value, string name)
{
    var result = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    if (result < 0)
    {
        throw new ArgumentOutOfRangeException(name, "Value must be non-negative.");
    }

    return result;
}

static string FormatStationCounts(SortedDictionary<int, int> stationCounts)
{
    return stationCounts.Count == 0
        ? "none"
        : string.Join(",", stationCounts.Select(pair => $"{pair.Key}:{pair.Value}"));
}

static bool IsHelp(string value)
{
    return string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);
}

static int Fail(string message)
{
    Console.Error.WriteLine($"[error] {message}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        DSCC UDP capture/replay

        Record app-facing skeleton UDP to JSONL:
          dotnet run --project tools\DsccUdpCapture -- record <port> <seconds> <file.jsonl>

        Replay a JSONL capture back to an app UDP port:
          dotnet run --project tools\DsccUdpCapture -- replay <file.jsonl> <host> <port> [--loop] [--no-timing] [--send-interval-ms <ms>]

        Examples:
          dotnet run --project tools\DsccUdpCapture -- record 55010 30 artifacts\field-capture.jsonl
          dotnet run --project tools\DsccUdpCapture -- replay artifacts\field-capture.jsonl 127.0.0.1 55010
          dotnet run --project tools\DsccUdpCapture -- replay artifacts\field-capture.jsonl 127.0.0.1 55130 --send-interval-ms 8
        """);
}
