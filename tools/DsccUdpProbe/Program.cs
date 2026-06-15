using System.Net;
using System.Net.Sockets;
using DSCC.Protocol;
using MessagePack;

// Field diagnostic: listens on the DSCC skeleton UDP port and summarizes the
// StationSkeletonFrame stream Unity would receive.
//   dotnet run --project tools/DsccUdpProbe -- [port] [durationSeconds]

var port = args.Length > 0 ? int.Parse(args[0]) : ProtocolConstants.DefaultSkeletonPort;
var durationSeconds = args.Length > 1 ? int.Parse(args[1]) : 30;

using var client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
Console.WriteLine($"[probe] listening on udp:{port} for {durationSeconds}s");

var stats = new Dictionary<int, StationStat>();
var startedAt = DateTimeOffset.UtcNow;
var deadline = startedAt.AddSeconds(durationSeconds);
long totalPackets = 0;
long decodeErrors = 0;

while (DateTimeOffset.UtcNow < deadline)
{
    var remaining = deadline - DateTimeOffset.UtcNow;
    var window = remaining > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : remaining;
    using var cts = new CancellationTokenSource(window);
    UdpReceiveResult result;
    try
    {
        result = await client.ReceiveAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        continue;
    }

    totalPackets++;
    StationSkeletonFrame frame;
    try
    {
        frame = MessagePackSerializer.Deserialize<StationSkeletonFrame>(result.Buffer);
    }
    catch
    {
        decodeErrors++;
        continue;
    }

    if (!stats.TryGetValue(frame.StationId, out var stat))
    {
        stat = new StationStat();
        stats[frame.StationId] = stat;
        Console.WriteLine(
            $"[first] station {frame.StationId} serial={frame.CameraSerial} device={frame.DeviceType} " +
            $"state={frame.State} hasPlayer={frame.HasPlayer} conf={frame.Confidence:0.00} joints={frame.Joints.Length} " +
            $"insideRoi={frame.IsInsideTrackingRoi} insideMarker={frame.IsInsideFootMarker} " +
            $"anchor=({frame.AnchorPosition.X:0.##},{frame.AnchorPosition.Y:0.##},{frame.AnchorPosition.Z:0.##}) " +
            $"bytes={result.Buffer.Length}");
    }

    stat.Frames++;
    stat.Serial = frame.CameraSerial;
    stat.DeviceType = frame.DeviceType;
    stat.LastState = frame.State.ToString();
    stat.LastJointCount = frame.Joints.Length;
    stat.LastConfidence = frame.Confidence;
    stat.LastPelvis = (frame.PelvisLocal.X, frame.PelvisLocal.Y, frame.PelvisLocal.Z);
    stat.LastInsideRoi = frame.IsInsideTrackingRoi;
    stat.LastInsideMarker = frame.IsInsideFootMarker;
    if (frame.HasPlayer)
    {
        stat.HasPlayerFrames++;
    }

    if (frame.Joints.Length > 0)
    {
        stat.FramesWithJoints++;
    }
}

var elapsed = (DateTimeOffset.UtcNow - startedAt).TotalSeconds;
Console.WriteLine($"[done] {totalPackets} packets in {elapsed:0.0}s, decode errors {decodeErrors}");
if (totalPackets == 0)
{
    Console.WriteLine("[warn] no packets received - is DSCC live and sending to this port?");
}

foreach (var (stationId, stat) in stats.OrderBy(pair => pair.Key))
{
    Console.WriteLine(
        $"[station {stationId}] serial={stat.Serial} device={stat.DeviceType} frames={stat.Frames} (~{stat.Frames / elapsed:0.0} fps) " +
        $"withJoints={stat.FramesWithJoints} hasPlayer={stat.HasPlayerFrames} lastState={stat.LastState} " +
        $"lastConf={stat.LastConfidence:0.00} lastJoints={stat.LastJointCount} " +
        $"lastPelvis=({stat.LastPelvis.X:0.00},{stat.LastPelvis.Y:0.00},{stat.LastPelvis.Z:0.00}) " +
        $"insideRoi={stat.LastInsideRoi} insideMarker={stat.LastInsideMarker}");
}

internal sealed class StationStat
{
    public long Frames;
    public long FramesWithJoints;
    public long HasPlayerFrames;
    public string Serial = "";
    public string DeviceType = "";
    public string LastState = "";
    public int LastJointCount;
    public float LastConfidence;
    public (float X, float Y, float Z) LastPelvis;
    public bool LastInsideRoi;
    public bool LastInsideMarker;
}
