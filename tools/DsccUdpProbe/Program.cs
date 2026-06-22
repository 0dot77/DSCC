using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DSCC.Core.Diagnostics;
using DSCC.Protocol;
using MessagePack;

// Field diagnostic: listens on the DSCC skeleton UDP port and summarizes the
// StationSkeletonFrame stream the app would receive.
//   dotnet run --project tools/DsccUdpProbe -- [port] [durationSeconds]
//   dotnet run --project tools/DsccUdpProbe -- --check-field-config config\wall-a.local.json
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --field-strict --expect-stations all --expect-serials-from-config config\wall-a.local.json
//   dotnet run --project tools/DsccUdpProbe -- 55010 30 --expect-stations all --min-joints 32 --min-fps 10 --require-player --require-active --max-decode-errors 0
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --expect-serials 1=CL2Z...,2=CL2Z... --min-player-ratio 0.8 --min-active-ratio 0.8
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --max-frame-gap-ms 500 --max-player-gap-ms 1000 --max-active-gap-ms 1000
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --fail-extra-stations
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --fail-duplicate-serials
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --min-active-confidence 0.45 --joint-confidence-threshold 0.45 --min-active-joint-confidence-ratio 0.8
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --expect-protocol-version 1
//   dotnet run --project tools/DsccUdpProbe -- 55010 30 --expect-stations all --min-active-joint-motion-m 0.05
//   dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --expect-serials-from-config config\wall-a.local.json
//   dotnet run --project tools/DsccUdpProbe -- 55130 60 --field-strict --expect-stations all --forward-to 127.0.0.1:55010

ProbeOptions options;
try
{
    options = ProbeOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"[fail] {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

if (options.ShowHelp)
{
    Console.WriteLine(ProbeOptions.Usage);
    return;
}

if (!string.IsNullOrWhiteSpace(options.FieldConfigPath))
{
    IReadOnlyList<string> configFailures;
    try
    {
        configFailures = ValidateFieldConfig(options.FieldConfigPath);
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"[fail] {ex.Message}");
        Environment.ExitCode = 2;
        return;
    }

    if (configFailures.Count > 0)
    {
        Console.WriteLine("[fail] field config validation failed");
        foreach (var failure in configFailures)
        {
            Console.WriteLine($"[fail] {failure}");
        }

        Environment.ExitCode = 2;
    }
    else
    {
        Console.WriteLine("[pass] field config validation passed");
    }

    return;
}

using var client = new UdpClient(new IPEndPoint(IPAddress.Any, options.Port));
Console.WriteLine($"[probe] listening on udp:{options.Port} for {options.DurationSeconds}s");
if (options.FieldStrict)
{
    Console.WriteLine("[probe] field strict preset enabled");
}

UdpClient? forwardClient = null;
if (options.ForwardTo is { } forwardTarget)
{
    forwardClient = new UdpClient();
    Console.WriteLine($"[forward] forwarding validated DSCC packets to udp://{forwardTarget.Host}:{forwardTarget.Port}");
}

var stats = new Dictionary<int, StationStat>();
var startedAt = DateTimeOffset.UtcNow;
var deadline = startedAt.AddSeconds(options.DurationSeconds);
long totalPackets = 0;
long decodeErrors = 0;
long forwardedPackets = 0;
long forwardErrors = 0;

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

    if (forwardClient is not null && options.ForwardTo is { } forwardEndpoint)
    {
        try
        {
            await forwardClient.SendAsync(result.Buffer, result.Buffer.Length, forwardEndpoint.Host, forwardEndpoint.Port);
            forwardedPackets++;
        }
        catch (SocketException ex)
        {
            forwardErrors++;
            if (forwardErrors == 1)
            {
                Console.WriteLine($"[forward] first send failure: {ex.Message}");
            }
        }
    }

    var receivedAt = DateTimeOffset.UtcNow;
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
    stat.ObserveFrame(receivedAt);
    stat.Serial = frame.CameraSerial;
    stat.Serials.Add(frame.CameraSerial);
    stat.DeviceType = frame.DeviceType;
    stat.DeviceTypes.Add(frame.DeviceType);
    stat.LastProtocolVersion = frame.ProtocolVersion;
    stat.ProtocolVersions.Add(frame.ProtocolVersion);
    stat.LastState = frame.State.ToString();
    stat.LastJointCount = frame.Joints.Length;
    stat.MaxJointCount = Math.Max(stat.MaxJointCount, frame.Joints.Length);
    stat.LastConfidence = frame.Confidence;
    stat.LastBodyCount = frame.BodyCount;
    stat.MaxBodyCount = Math.Max(stat.MaxBodyCount, frame.BodyCount);
    stat.LastSelectedBodyId = frame.SelectedBodyId;
    stat.LastPelvis = (frame.PelvisLocal.X, frame.PelvisLocal.Y, frame.PelvisLocal.Z);
    stat.LastInsideRoi = frame.IsInsideTrackingRoi;
    stat.LastInsideMarker = frame.IsInsideFootMarker;
    if (frame.HasPlayer)
    {
        stat.ObservePlayerConfidence(frame.Confidence);
    }

    if (frame.State == StationStateDto.Active)
    {
        stat.ActiveFrames++;
        stat.ObserveActive(receivedAt);
        stat.ObserveActiveConfidence(frame.Confidence);
        stat.ObserveActiveJointConfidence(frame.Joints, options.JointConfidenceThreshold);
        stat.ObserveActiveJointMotion(frame.Joints, frame.PelvisLocal, options.MotionJointNames);
        stat.ObserveActiveRoi(frame.IsInsideTrackingRoi, frame.IsInsideFootMarker);
        stat.ObserveActiveBody(frame.BodyCount, frame.SelectedBodyId);
    }

    if (frame.HasPlayer)
    {
        stat.HasPlayerFrames++;
        stat.ObservePlayer(receivedAt);
    }

    if (frame.Joints.Length > 0)
    {
        stat.FramesWithJoints++;
    }
}

forwardClient?.Dispose();

var finishedAt = DateTimeOffset.UtcNow;
var elapsed = (finishedAt - startedAt).TotalSeconds;
Console.WriteLine($"[done] {totalPackets} packets in {elapsed:0.0}s, decode errors {decodeErrors}");
if (options.ForwardTo is { } completedForwardTarget)
{
    Console.WriteLine(
        $"[forward] forwarded {forwardedPackets} packets to udp://{completedForwardTarget.Host}:{completedForwardTarget.Port}, " +
        $"errors {forwardErrors}");
}

if (totalPackets == 0)
{
    Console.WriteLine("[warn] no packets received - is DSCC live and sending to this port?");
}

foreach (var (stationId, stat) in stats.OrderBy(pair => pair.Key))
{
    var playerRatio = stat.Frames > 0 ? stat.HasPlayerFrames / (double)stat.Frames : 0;
    var activeRatio = stat.Frames > 0 ? stat.ActiveFrames / (double)stat.Frames : 0;
    var activeJointConfidenceRatio = stat.ActiveJointSamples > 0
        ? stat.ActiveConfidentJointSamples / (double)stat.ActiveJointSamples
        : 0;
    var activeRoiRatio = stat.ActiveFrames > 0 ? stat.ActiveInsideRoiFrames / (double)stat.ActiveFrames : 0;
    var activeMarkerRatio = stat.ActiveFrames > 0 ? stat.ActiveInsideMarkerFrames / (double)stat.ActiveFrames : 0;
    var serialSummary = stat.Serials.Count <= 1 ? stat.Serial : string.Join("|", stat.Serials.Order());
    var deviceTypeSummary = stat.DeviceTypes.Count <= 1 ? stat.DeviceType : string.Join("|", stat.DeviceTypes.Order());
    var protocolSummary = stat.ProtocolVersions.Count <= 1
        ? stat.LastProtocolVersion.ToString()
        : string.Join("|", stat.ProtocolVersions.Order());

    Console.WriteLine(
        $"[station {stationId}] protocol={protocolSummary} serial={serialSummary} device={deviceTypeSummary} frames={stat.Frames} (~{stat.Frames / elapsed:0.0} fps) " +
        $"withJoints={stat.FramesWithJoints} hasPlayer={stat.HasPlayerFrames} ({playerRatio:P0}) active={stat.ActiveFrames} ({activeRatio:P0}) lastState={stat.LastState} " +
        $"lastConf={stat.LastConfidence:0.00} minPlayerConf={FormatConfidence(stat.MinPlayerConfidence)} " +
        $"minActiveConf={FormatConfidence(stat.MinActiveConfidence)} activeJointConf={activeJointConfidenceRatio:P0} " +
        $"requiredJointConf={stat.FormatRequiredActiveJointConfidence(options.RequiredActiveJointNames)} " +
        $"activeRoi={activeRoiRatio:P0} activeMarker={activeMarkerRatio:P0} " +
        $"lastJoints={stat.LastJointCount} maxJoints={stat.MaxJointCount} bodyCount={stat.LastBodyCount} maxBodyCount={stat.MaxBodyCount} " +
        $"selectedBody={FormatBodyId(stat.LastSelectedBodyId)} bodySwitches={stat.SelectedBodyIdChanges} " +
        $"bodyStability={stat.FormatActiveBodyStability(options.MaxActiveBodyCount)} " +
        $"jointMotion={stat.FormatActiveJointMotion(options.MotionJointNames)} " +
        $"maxFrameGap={FormatGap(stat.MaxFrameGapMs)} maxPlayerGap={FormatGap(stat.GetMaxPlayerGapMs(finishedAt))} " +
        $"maxActiveGap={FormatGap(stat.GetMaxActiveGapMs(finishedAt))} " +
        $"lastPelvis=({stat.LastPelvis.X:0.00},{stat.LastPelvis.Y:0.00},{stat.LastPelvis.Z:0.00}) " +
        $"insideRoi={stat.LastInsideRoi} insideMarker={stat.LastInsideMarker}");
}

WriteAcceptanceSummary(options, stats, elapsed, finishedAt);

var failures = Validate(options, stats, totalPackets, decodeErrors, forwardedPackets, forwardErrors, elapsed, finishedAt);
if (failures.Count > 0)
{
    Console.WriteLine("[fail] validation failed");
    foreach (var failure in failures)
    {
        Console.WriteLine($"[fail] {failure}");
    }

    WriteTroubleshootingHints(failures);
    Environment.ExitCode = 2;
}
else if (options.ValidationEnabled)
{
    Console.WriteLine("[pass] validation passed");
}

static void WriteAcceptanceSummary(
    ProbeOptions options,
    IReadOnlyDictionary<int, StationStat> stats,
    double elapsed,
    DateTimeOffset finishedAt)
{
    if (!options.ValidationEnabled || stats.Count == 0)
    {
        return;
    }

    var stationIds = options.ExpectedStations.Count > 0 || options.ExpectedSerials.Count > 0
        ? options.ExpectedStations.Concat(options.ExpectedSerials.Keys).Distinct().Order().ToArray()
        : stats.Keys.Order().ToArray();

    Console.WriteLine("[acceptance] stream target: four pinned stations, one Active body per station, stable selected body, and head/hand/knee/ankle/foot motion");
    foreach (var stationId in stationIds)
    {
        if (!stats.TryGetValue(stationId, out var stat))
        {
            Console.WriteLine($"[acceptance station {stationId}] stream=fail station was not observed");
            continue;
        }

        var fps = elapsed > 0 ? stat.Frames / elapsed : 0;
        var playerRatio = stat.Frames > 0 ? stat.HasPlayerFrames / (double)stat.Frames : 0;
        var activeRatio = stat.Frames > 0 ? stat.ActiveFrames / (double)stat.Frames : 0;
        var activeJointConfidenceRatio = stat.ActiveJointSamples > 0
            ? stat.ActiveConfidentJointSamples / (double)stat.ActiveJointSamples
            : 0;
        var maxPlayerGapMs = stat.GetMaxPlayerGapMs(finishedAt);
        var maxActiveGapMs = stat.GetMaxActiveGapMs(finishedAt);

        var streamOk =
            stat.Frames > 0 &&
            (options.MinFps <= 0 || fps >= options.MinFps) &&
            (!options.MaxFrameGapMs.HasValue || stat.MaxFrameGapMs <= options.MaxFrameGapMs.Value) &&
            (!options.MaxPlayerGapMs.HasValue || (maxPlayerGapMs.HasValue && maxPlayerGapMs.Value <= options.MaxPlayerGapMs.Value)) &&
            (!options.MaxActiveGapMs.HasValue || (maxActiveGapMs.HasValue && maxActiveGapMs.Value <= options.MaxActiveGapMs.Value));
        var playerOk =
            (!options.RequirePlayer || stat.HasPlayerFrames > 0) &&
            (!options.RequireActive || stat.ActiveFrames > 0) &&
            (options.MinPlayerRatio <= 0 || playerRatio >= options.MinPlayerRatio) &&
            (options.MinActiveRatio <= 0 || activeRatio >= options.MinActiveRatio) &&
            (!options.MinActiveConfidence.HasValue || (stat.MinActiveConfidence.HasValue && stat.MinActiveConfidence.Value >= options.MinActiveConfidence.Value));
        var skeletonOk =
            (options.MinJoints <= 0 || stat.MaxJointCount >= options.MinJoints) &&
            (options.MinActiveJointConfidenceRatio <= 0 ||
                (stat.ActiveJointSamples > 0 && activeJointConfidenceRatio >= options.MinActiveJointConfidenceRatio)) &&
            AreRequiredJointsConfident(stat, options);
        var oneBodyOk =
            (!options.MaxActiveBodyCount.HasValue ||
                !FieldBodyCountPolicy.HasExtraBodies(
                    hasPlayer: stat.ActiveFrames > 0,
                    bodyCount: stat.MaxActiveBodyCount,
                    maxActiveBodyCount: options.MaxActiveBodyCount.Value)) &&
            (!options.MaxSelectedBodyIdChanges.HasValue ||
                (stat.ActiveSelectedBodySamples > 0 && stat.SelectedBodyIdChanges <= options.MaxSelectedBodyIdChanges.Value));
        var motionOk = IsMotionOk(stat, options);

        Console.WriteLine(
            $"[acceptance station {stationId}] " +
            $"stream={FormatOk(streamOk)} fps={fps:0.0} gaps(frame/player/active)=" +
            $"{FormatGap(stat.MaxFrameGapMs)}/{FormatGap(maxPlayerGapMs)}/{FormatGap(maxActiveGapMs)} " +
            $"player={FormatOk(playerOk)} active={activeRatio:P0} minConf={FormatConfidence(stat.MinActiveConfidence)} " +
            $"skeleton={FormatOk(skeletonOk)} joints={stat.MaxJointCount} jointConf={activeJointConfidenceRatio:P0} " +
            $"requiredJointConf={stat.FormatRequiredActiveJointConfidence(options.RequiredActiveJointNames)} " +
            $"oneBody={FormatOk(oneBodyOk)} maxBodies={stat.MaxActiveBodyCount} bodySwitches={stat.SelectedBodyIdChanges} " +
            $"bodyStability={stat.FormatActiveBodyStability(options.MaxActiveBodyCount)} " +
            $"motion={FormatOk(motionOk)} {stat.FormatActiveJointMotion(options.MotionJointNames)}");
    }
}

static bool AreRequiredJointsConfident(StationStat stat, ProbeOptions options)
{
    if (options.RequiredActiveJointNames.Count == 0)
    {
        return true;
    }

    foreach (var jointName in options.RequiredActiveJointNames)
    {
        if (!stat.TryGetActiveJointConfidenceRatio(jointName, out var confidenceRatio) ||
            confidenceRatio < options.MinRequiredActiveJointConfidenceRatio)
        {
            return false;
        }
    }

    return true;
}

static bool IsMotionOk(StationStat stat, ProbeOptions options)
{
    if (options.MinActiveJointMotionMeters <= 0)
    {
        return true;
    }

    foreach (var jointName in options.MotionJointNames)
    {
        if (!stat.TryGetActiveJointMotionMeters(jointName, out var jointMotionMeters) ||
            jointMotionMeters < options.MinActiveJointMotionMeters)
        {
            return false;
        }
    }

    return true;
}

static string FormatOk(bool ok)
{
    return ok ? "ok" : "fail";
}

static void WriteTroubleshootingHints(IReadOnlyList<string> failures)
{
    if (failures.Count == 0)
    {
        return;
    }

    var hints = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var failure in failures)
    {
        if (failure.Contains("was not observed", StringComparison.OrdinalIgnoreCase) ||
            failure.Contains("no packets", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("Check DSCC live/tracker startup, UDP port, and that the Tauri receiver port matches unity.skeletonPort.");
        }

        if (failure.Contains("serial", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("Run .\\tools\\Set-FieldStationSerials.ps1 -PrintTemplate, then pin physical left-to-right Femto Mega serials.");
        }

        if (failure.Contains("body count", StringComparison.OrdinalIgnoreCase) ||
            failure.Contains("selected body id", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("Keep exactly one performer inside each station ROI; reduce camera overlap or tighten trackingRoi if body ids switch.");
        }

        if (failure.Contains("motion joint", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("Ask the performer to move head, hands, knees, ankles, and feet; if motion stays flat, check body tracking skeleton output.");
        }

        if (failure.Contains("fps", StringComparison.OrdinalIgnoreCase) ||
            failure.Contains("gap", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("For low FPS/gaps, keep CUDA mode, NFOV_UNBINNED at 15fps, reduce USB load, and verify GPU runtime libraries.");
        }

        if (failure.Contains("confidence", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("For low confidence, improve full-body visibility, distance, lighting, and remove occlusion inside the ROI.");
        }
    }

    if (hints.Count == 0)
    {
        return;
    }

    Console.WriteLine("[hint] next checks:");
    foreach (var hint in hints)
    {
        Console.WriteLine($"[hint] {hint}");
    }
}

static IReadOnlyList<string> Validate(
    ProbeOptions options,
    IReadOnlyDictionary<int, StationStat> stats,
    long totalPackets,
    long decodeErrors,
    long forwardedPackets,
    long forwardErrors,
    double elapsed,
    DateTimeOffset finishedAt)
{
    var failures = new List<string>();
    if (!options.ValidationEnabled)
    {
        return failures;
    }

    if (totalPackets == 0)
    {
        failures.Add("no packets received");
    }

    if (options.MaxDecodeErrors is { } maxDecodeErrors && decodeErrors > maxDecodeErrors)
    {
        failures.Add($"decode errors {decodeErrors} exceeded max {maxDecodeErrors}");
    }

    if (options.ForwardTo is not null)
    {
        if (forwardErrors > 0)
        {
            failures.Add($"UDP forward errors {forwardErrors} exceeded max 0");
        }

        if (totalPackets > 0 && forwardedPackets == 0)
        {
            failures.Add("UDP forwarding was enabled but no valid packets were forwarded");
        }
    }

    var stationIds = options.ExpectedStations.Count > 0 || options.ExpectedSerials.Count > 0
        ? options.ExpectedStations.Concat(options.ExpectedSerials.Keys).Distinct().Order().ToArray()
        : stats.Keys.Order().ToArray();

    if (options.FailExtraStations && stationIds.Length > 0)
    {
        foreach (var stationId in stats.Keys.Except(stationIds).Order())
        {
            failures.Add($"unexpected station {stationId} was observed");
        }
    }

    if (options.FailDuplicateSerials)
    {
        foreach (var duplicate in FindDuplicateSerials(stats))
        {
            failures.Add(
                $"camera serial {duplicate.Serial} was observed on multiple stations: " +
                string.Join(",", duplicate.StationIds));
        }
    }

    foreach (var stationId in stationIds)
    {
        if (!stats.TryGetValue(stationId, out var stat))
        {
            failures.Add($"station {stationId} was not observed");
            continue;
        }

        var fps = elapsed > 0 ? stat.Frames / elapsed : 0;
        var playerRatio = stat.Frames > 0 ? stat.HasPlayerFrames / (double)stat.Frames : 0;
        var activeRatio = stat.Frames > 0 ? stat.ActiveFrames / (double)stat.Frames : 0;
        var activeJointConfidenceRatio = stat.ActiveJointSamples > 0
            ? stat.ActiveConfidentJointSamples / (double)stat.ActiveJointSamples
            : 0;
        var activeRoiRatio = stat.ActiveFrames > 0 ? stat.ActiveInsideRoiFrames / (double)stat.ActiveFrames : 0;
        var activeMarkerRatio = stat.ActiveFrames > 0 ? stat.ActiveInsideMarkerFrames / (double)stat.ActiveFrames : 0;
        var maxPlayerGapMs = stat.GetMaxPlayerGapMs(finishedAt);
        var maxActiveGapMs = stat.GetMaxActiveGapMs(finishedAt);
        if (!string.IsNullOrWhiteSpace(options.ExpectedDeviceType))
        {
            var observedDeviceTypes = stat.DeviceTypes
                .Where(deviceType => !string.IsNullOrWhiteSpace(deviceType))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (observedDeviceTypes.Length == 0)
            {
                failures.Add($"station {stationId} had no device type");
            }
            else if (observedDeviceTypes.Any(deviceType =>
                         !string.Equals(deviceType, options.ExpectedDeviceType, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add(
                    $"station {stationId} expected device type {options.ExpectedDeviceType} but observed " +
                    string.Join(",", observedDeviceTypes));
            }
        }

        if (options.ExpectedProtocolVersion is { } expectedProtocolVersion)
        {
            if (stat.ProtocolVersions.Count == 0)
            {
                failures.Add($"station {stationId} had no protocol version samples");
            }
            else
            {
                var unexpectedProtocolVersions = stat.ProtocolVersions
                    .Where(version => version != expectedProtocolVersion)
                    .Order()
                    .ToArray();
                if (unexpectedProtocolVersions.Length > 0)
                {
                    failures.Add(
                        $"station {stationId} expected protocol version {expectedProtocolVersion} but observed " +
                        string.Join(",", stat.ProtocolVersions.Order()));
                }
            }
        }

        if (options.MinFps > 0 && fps < options.MinFps)
        {
            failures.Add($"station {stationId} fps {fps:0.0} below min {options.MinFps:0.0}");
        }

        if (options.MinJoints > 0 && stat.MaxJointCount < options.MinJoints)
        {
            failures.Add($"station {stationId} max joints {stat.MaxJointCount} below min {options.MinJoints}");
        }

        if (options.RequirePlayer && stat.HasPlayerFrames == 0)
        {
            failures.Add($"station {stationId} had no hasPlayer frames");
        }

        if (options.RequireActive && stat.ActiveFrames == 0)
        {
            failures.Add($"station {stationId} had no Active frames");
        }

        if (options.MinPlayerRatio > 0 && playerRatio < options.MinPlayerRatio)
        {
            failures.Add($"station {stationId} player ratio {playerRatio:0.00} below min {options.MinPlayerRatio:0.00}");
        }

        if (options.MinActiveRatio > 0 && activeRatio < options.MinActiveRatio)
        {
            failures.Add($"station {stationId} active ratio {activeRatio:0.00} below min {options.MinActiveRatio:0.00}");
        }

        if (options.MinPlayerConfidence is { } minPlayerConfidence)
        {
            if (stat.MinPlayerConfidence is null)
            {
                failures.Add($"station {stationId} had no player frames for confidence validation");
            }
            else if (stat.MinPlayerConfidence < minPlayerConfidence)
            {
                failures.Add(
                    $"station {stationId} min player confidence {stat.MinPlayerConfidence:0.00} below min {minPlayerConfidence:0.00}");
            }
        }

        if (options.MinActiveConfidence is { } minActiveConfidence)
        {
            if (stat.MinActiveConfidence is null)
            {
                failures.Add($"station {stationId} had no Active frames for confidence validation");
            }
            else if (stat.MinActiveConfidence < minActiveConfidence)
            {
                failures.Add(
                    $"station {stationId} min active confidence {stat.MinActiveConfidence:0.00} below min {minActiveConfidence:0.00}");
            }
        }

        if (options.MinActiveJointConfidenceRatio > 0)
        {
            if (stat.ActiveJointSamples == 0)
            {
                failures.Add($"station {stationId} had no Active joints for confidence validation");
            }
            else if (activeJointConfidenceRatio < options.MinActiveJointConfidenceRatio)
            {
                failures.Add(
                    $"station {stationId} active joint confidence ratio {activeJointConfidenceRatio:0.00} below min " +
                    $"{options.MinActiveJointConfidenceRatio:0.00} at threshold {options.JointConfidenceThreshold:0.00}");
            }
        }

        if (options.RequiredActiveJointNames.Count > 0)
        {
            if (stat.ActiveFrames == 0)
            {
                failures.Add($"station {stationId} had no Active frames for required joint confidence validation");
            }

            foreach (var jointName in options.RequiredActiveJointNames)
            {
                if (!stat.TryGetActiveJointConfidenceRatio(jointName, out var requiredJointRatio))
                {
                    failures.Add($"station {stationId} had no Active confidence samples for required joint {jointName}");
                }
                else if (requiredJointRatio < options.MinRequiredActiveJointConfidenceRatio)
                {
                    failures.Add(
                        $"station {stationId} required joint {jointName} confidence ratio {requiredJointRatio:0.00} below min " +
                        $"{options.MinRequiredActiveJointConfidenceRatio:0.00} at threshold {options.JointConfidenceThreshold:0.00}");
                }
            }
        }

        if (options.MinActiveJointMotionMeters > 0)
        {
            if (stat.ActiveFrames == 0)
            {
                failures.Add($"station {stationId} had no Active frames for joint motion validation");
            }

            foreach (var jointName in options.MotionJointNames)
            {
                if (!stat.TryGetActiveJointMotionMeters(jointName, out var jointMotionMeters))
                {
                    failures.Add($"station {stationId} had no Active samples for motion joint {jointName}");
                }
                else if (jointMotionMeters < options.MinActiveJointMotionMeters)
                {
                    failures.Add(
                        $"station {stationId} motion joint {jointName} moved {jointMotionMeters:0.000}m, " +
                        $"below min {options.MinActiveJointMotionMeters:0.000}m");
                }
            }
        }

        if (options.MinActiveRoiRatio > 0)
        {
            if (stat.ActiveFrames == 0)
            {
                failures.Add($"station {stationId} had no Active frames for ROI validation");
            }
            else if (activeRoiRatio < options.MinActiveRoiRatio)
            {
                failures.Add(
                    $"station {stationId} Active ROI ratio {activeRoiRatio:0.00} below min {options.MinActiveRoiRatio:0.00}");
            }
        }

        if (options.MinActiveMarkerRatio > 0)
        {
            if (stat.ActiveFrames == 0)
            {
                failures.Add($"station {stationId} had no Active frames for marker validation");
            }
            else if (activeMarkerRatio < options.MinActiveMarkerRatio)
            {
                failures.Add(
                    $"station {stationId} Active foot marker ratio {activeMarkerRatio:0.00} below min {options.MinActiveMarkerRatio:0.00}");
            }
        }

        if (options.MaxActiveBodyCount is { } maxActiveBodyCount &&
            FieldBodyCountPolicy.HasExtraBodies(
                hasPlayer: stat.ActiveFrames > 0,
                bodyCount: stat.MaxActiveBodyCount,
                maxActiveBodyCount))
        {
            var extraBodyFrames = stat.GetActiveExtraBodyFrames(maxActiveBodyCount);
            failures.Add(
                $"station {stationId} had {extraBodyFrames}/{stat.ActiveBodySamples} Active body samples " +
                $"with body count above {maxActiveBodyCount}; max Active body count was {stat.MaxActiveBodyCount}");
        }

        if (options.MaxSelectedBodyIdChanges is { } maxSelectedBodyIdChanges)
        {
            if (stat.ActiveSelectedBodySamples == 0)
            {
                failures.Add($"station {stationId} had no selected body id samples during Active frames");
            }
            else if (stat.SelectedBodyIdChanges > maxSelectedBodyIdChanges)
            {
                failures.Add(
                    $"station {stationId} selected body id changed {stat.SelectedBodyIdChanges} times, " +
                    $"exceeding max {maxSelectedBodyIdChanges}");
            }
        }

        if (options.MaxFrameGapMs is { } maxFrameGapMs && stat.MaxFrameGapMs > maxFrameGapMs)
        {
            failures.Add($"station {stationId} max frame gap {stat.MaxFrameGapMs:0}ms exceeded max {maxFrameGapMs}ms");
        }

        if (options.MaxPlayerGapMs is { } maxPlayerGapLimitMs)
        {
            if (maxPlayerGapMs is null)
            {
                failures.Add($"station {stationId} had no player frames for gap validation");
            }
            else if (maxPlayerGapMs > maxPlayerGapLimitMs)
            {
                failures.Add($"station {stationId} max player gap {maxPlayerGapMs:0}ms exceeded max {maxPlayerGapLimitMs}ms");
            }
        }

        if (options.MaxActiveGapMs is { } maxActiveGapLimitMs)
        {
            if (maxActiveGapMs is null)
            {
                failures.Add($"station {stationId} had no Active frames for gap validation");
            }
            else if (maxActiveGapMs > maxActiveGapLimitMs)
            {
                failures.Add($"station {stationId} max active gap {maxActiveGapMs:0}ms exceeded max {maxActiveGapLimitMs}ms");
            }
        }

        if (options.ExpectedSerials.TryGetValue(stationId, out var expectedSerial))
        {
            if (!stat.Serials.Contains(expectedSerial))
            {
                var observed = stat.Serials.Count > 0 ? string.Join(",", stat.Serials.Order()) : "<none>";
                failures.Add($"station {stationId} expected serial {expectedSerial} but observed {observed}");
            }

            if (stat.Serials.Count > 1)
            {
                failures.Add($"station {stationId} observed multiple serials: {string.Join(",", stat.Serials.Order())}");
            }
        }
    }

    return failures;
}

static IReadOnlyList<(string Serial, IReadOnlyList<int> StationIds)> FindDuplicateSerials(
    IReadOnlyDictionary<int, StationStat> stats)
{
    return stats
        .SelectMany(pair => pair.Value.Serials
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .Select(serial => new { Serial = serial, StationId = pair.Key }))
        .GroupBy(item => item.Serial, StringComparer.OrdinalIgnoreCase)
        .Select(group => (
            Serial: group.Key,
            StationIds: (IReadOnlyList<int>)group
                .Select(item => item.StationId)
                .Distinct()
                .Order()
                .ToArray()))
        .Where(group => group.StationIds.Count > 1)
        .OrderBy(group => group.Serial, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string FormatGap(double? gapMs)
{
    return gapMs is null ? "-" : $"{gapMs.Value:0}ms";
}

static string FormatConfidence(float? confidence)
{
    return confidence is null ? "-" : confidence.Value.ToString("0.00");
}

static string FormatBodyId(long bodyId)
{
    return bodyId < 0 ? "-" : bodyId.ToString();
}

static IReadOnlyList<string> ValidateFieldConfig(string path)
{
    using var stream = File.OpenRead(path);
    using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    });

    var failures = new List<string>();
    var root = document.RootElement;
    ValidateFieldRoot(root, failures);
    ValidateFieldBodyTracking(root, failures);
    ValidateFieldUnity(root, failures);
    ValidateFieldStations(root, path, failures);
    return failures;
}

static void ValidateFieldRoot(JsonElement root, List<string> failures)
{
    var autoAssignDevicesOnStart = ConfigGetOptionalBool(root, "autoAssignDevicesOnStart") ?? false;
    if (autoAssignDevicesOnStart)
    {
        failures.Add(
            "autoAssignDevicesOnStart must be false for the four-Mega field rig; " +
            "pin each station with device.serial or calibration.cameraSerial instead");
    }
}

static void ValidateFieldBodyTracking(JsonElement root, List<string> failures)
{
    if (!root.TryGetProperty("bodyTracking", out var bodyTracking) ||
        bodyTracking.ValueKind != JsonValueKind.Object)
    {
        failures.Add("bodyTracking object is missing");
        return;
    }

    if (!bodyTracking.TryGetProperty("processingModes", out var processingModes) ||
        processingModes.ValueKind != JsonValueKind.Array)
    {
        failures.Add("bodyTracking.processingModes must be an array containing only Cuda");
    }
    else
    {
        var modes = processingModes.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString() ?? string.Empty)
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .ToArray();
        if (modes.Length == 0)
        {
            failures.Add("bodyTracking.processingModes is empty");
        }
        else if (!modes.Any(mode => string.Equals(mode, "Cuda", StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("bodyTracking.processingModes must include Cuda");
        }

        var nonCudaModes = modes
            .Where(mode => !string.Equals(mode, "Cuda", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nonCudaModes.Length > 0)
        {
            failures.Add($"bodyTracking.processingModes must be CUDA-only for field rig, found: {string.Join(",", nonCudaModes)}");
        }
    }

    var useLiteModel = ConfigGetOptionalBool(bodyTracking, "useLiteModel") ?? false;
    if (!useLiteModel)
    {
        failures.Add("bodyTracking.useLiteModel must be true for four-camera field tracking");
    }

    var maxFps = ConfigGetOptionalInt32(bodyTracking, "maxFps");
    if (maxFps is null or <= 0)
    {
        failures.Add("bodyTracking.maxFps must be greater than zero");
    }
    else if (maxFps > 15)
    {
        failures.Add($"bodyTracking.maxFps must be 15 or lower for four-camera field tracking, found {maxFps}");
    }
}

static void ValidateFieldUnity(JsonElement root, List<string> failures)
{
    if (!root.TryGetProperty("unity", out var unity) ||
        unity.ValueKind != JsonValueKind.Object)
    {
        failures.Add("unity object is missing");
        return;
    }

    var skeletonPort = ConfigGetOptionalInt32(unity, "skeletonPort");
    if (skeletonPort is null or <= 0 or > 65535)
    {
        failures.Add("unity.skeletonPort must be a valid UDP port");
    }
}

static void ValidateFieldStations(JsonElement root, string path, List<string> failures)
{
    if (!root.TryGetProperty("stations", out var stationsElement) ||
        stationsElement.ValueKind != JsonValueKind.Array)
    {
        failures.Add($"Config file does not contain a stations array: {path}");
        return;
    }

    var enabledStations = stationsElement.EnumerateArray()
        .Where(station => (ConfigGetOptionalBool(station, "enabled") ?? true))
        .ToArray();
    var stationIds = new List<int>();
    var serialToStation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var station in enabledStations)
    {
        var stationId = ConfigGetRequiredInt32(station, "stationId");
        stationIds.Add(stationId);
        if (!station.TryGetProperty("device", out var device) ||
            device.ValueKind != JsonValueKind.Object)
        {
            failures.Add($"station {stationId} device object is missing");
            continue;
        }

        var deviceType = ConfigGetOptionalString(device, "deviceType");
        if (!string.Equals(deviceType, "FemtoMega", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"station {stationId} device.deviceType must be FemtoMega, found {DisplayConfigValue(deviceType)}");
        }

        var syncRole = ConfigGetOptionalString(device, "syncRole");
        var expectedSyncRole = stationId == 1 ? "Primary" : "Secondary";
        if (!string.Equals(syncRole, expectedSyncRole, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"station {stationId} device.syncRole must be {expectedSyncRole} " +
                $"for the four-Mega field rig, found {DisplayConfigValue(syncRole)}");
        }

        var serial = ConfigGetOptionalString(device, "serial");
        if (string.IsNullOrWhiteSpace(serial) &&
            station.TryGetProperty("calibration", out var calibration) &&
            calibration.ValueKind == JsonValueKind.Object)
        {
            serial = ConfigGetOptionalString(calibration, "cameraSerial");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            failures.Add($"station {stationId} is missing device.serial/calibration.cameraSerial");
        }
        else if (serialToStation.TryGetValue(serial, out var otherStationId))
        {
            failures.Add($"stations {otherStationId} and {stationId} share camera serial {serial}");
        }
        else
        {
            serialToStation[serial] = stationId;
        }

        var depthMode = ConfigGetOptionalString(device, "depthMode");
        if (!string.Equals(depthMode, "NFOV_UNBINNED", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"station {stationId} device.depthMode must be NFOV_UNBINNED, found {DisplayConfigValue(depthMode)}");
        }

        var fps = ConfigGetOptionalInt32(device, "fps");
        if (fps is null or <= 0)
        {
            failures.Add($"station {stationId} device.fps must be greater than zero");
        }
        else if (fps > 15)
        {
            failures.Add($"station {stationId} device.fps must be 15 or lower for four-camera field tracking, found {fps}");
        }
    }

    var expectedStationIds = new[] { 1, 2, 3, 4 };
    if (!stationIds.Order().SequenceEqual(expectedStationIds))
    {
        failures.Add($"enabled field stations must be exactly 1,2,3,4; found {string.Join(",", stationIds.Order())}");
    }
}

static int ConfigGetRequiredInt32(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.Number ||
        !property.TryGetInt32(out var value))
    {
        throw new ArgumentException($"Missing or invalid integer property: {propertyName}");
    }

    return value;
}

static int? ConfigGetOptionalInt32(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
    {
        return value;
    }

    throw new ArgumentException($"Invalid integer property: {propertyName}");
}

static bool? ConfigGetOptionalBool(JsonElement element, string propertyName)
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

static string ConfigGetOptionalString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return string.Empty;
    }

    if (property.ValueKind != JsonValueKind.String)
    {
        throw new ArgumentException($"Invalid string property: {propertyName}");
    }

    return property.GetString() ?? string.Empty;
}

static string DisplayConfigValue(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
}

internal sealed class StationStat
{
    public long Frames;
    public long FramesWithJoints;
    public long HasPlayerFrames;
    public long ActiveFrames;
    public string Serial = "";
    public HashSet<string> Serials { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeviceTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<int> ProtocolVersions { get; } = [];
    public double MaxFrameGapMs { get; private set; }
    public double MaxPlayerGapMs { get; private set; }
    public double MaxActiveGapMs { get; private set; }
    public float? MinPlayerConfidence { get; private set; }
    public float? MinActiveConfidence { get; private set; }
    public long ActiveJointSamples { get; private set; }
    public long ActiveConfidentJointSamples { get; private set; }
    public long ActiveInsideRoiFrames { get; private set; }
    public long ActiveInsideMarkerFrames { get; private set; }
    private Dictionary<string, JointMotionStat> ActiveJointMotion { get; } = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, JointConfidenceStat> ActiveJointConfidenceByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string DeviceType = "";
    public int LastProtocolVersion;
    public string LastState = "";
    public int LastJointCount;
    public int MaxJointCount;
    public int LastBodyCount;
    public int MaxBodyCount;
    public int MaxActiveBodyCount;
    public long LastSelectedBodyId = -1;
    public long ActiveBodySamples { get; private set; }
    public long ActiveSelectedBodySamples;
    public long ActiveMissingSelectedBodyFrames { get; private set; }
    public int SelectedBodyIdChanges;
    public float LastConfidence;
    public (float X, float Y, float Z) LastPelvis;
    public bool LastInsideRoi;
    public bool LastInsideMarker;
    private DateTimeOffset? LastFrameReceivedAt { get; set; }
    private DateTimeOffset? LastPlayerReceivedAt { get; set; }
    private DateTimeOffset? LastActiveReceivedAt { get; set; }
    private long? ActiveSelectedBodyId { get; set; }
    private SortedDictionary<int, long> ActiveBodyCountHistogram { get; } = [];
    private HashSet<long> ActiveSelectedBodyIds { get; } = [];

    public void ObserveFrame(DateTimeOffset receivedAt)
    {
        MaxFrameGapMs = Math.Max(MaxFrameGapMs, CalculateGapMs(LastFrameReceivedAt, receivedAt));
        LastFrameReceivedAt = receivedAt;
    }

    public void ObservePlayer(DateTimeOffset receivedAt)
    {
        MaxPlayerGapMs = Math.Max(MaxPlayerGapMs, CalculateGapMs(LastPlayerReceivedAt, receivedAt));
        LastPlayerReceivedAt = receivedAt;
    }

    public void ObserveActive(DateTimeOffset receivedAt)
    {
        MaxActiveGapMs = Math.Max(MaxActiveGapMs, CalculateGapMs(LastActiveReceivedAt, receivedAt));
        LastActiveReceivedAt = receivedAt;
    }

    public void ObservePlayerConfidence(float confidence)
    {
        MinPlayerConfidence = MinPlayerConfidence is null ? confidence : Math.Min(MinPlayerConfidence.Value, confidence);
    }

    public void ObserveActiveConfidence(float confidence)
    {
        MinActiveConfidence = MinActiveConfidence is null ? confidence : Math.Min(MinActiveConfidence.Value, confidence);
    }

    public void ObserveActiveJointConfidence(IEnumerable<JointFrameDto> joints, double threshold)
    {
        foreach (var joint in joints)
        {
            ActiveJointSamples++;
            if (joint.Confidence >= threshold)
            {
                ActiveConfidentJointSamples++;
            }

            if (!ActiveJointConfidenceByName.TryGetValue(joint.Name, out var stat))
            {
                stat = new JointConfidenceStat(joint.Name);
                ActiveJointConfidenceByName[joint.Name] = stat;
            }

            stat.Observe(joint.Confidence, threshold);
        }
    }

    public void ObserveActiveJointMotion(
        IEnumerable<JointFrameDto> joints,
        Vector3Dto pelvisLocal,
        IReadOnlyList<string> jointNames)
    {
        if (jointNames.Count == 0)
        {
            return;
        }

        foreach (var joint in joints)
        {
            if (!jointNames.Contains(joint.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ActiveJointMotion.TryGetValue(joint.Name, out var motion))
            {
                motion = new JointMotionStat(joint.Name);
                ActiveJointMotion[joint.Name] = motion;
            }

            motion.Observe(new Vector3Dto(
                joint.PositionLocal.X - pelvisLocal.X,
                joint.PositionLocal.Y - pelvisLocal.Y,
                joint.PositionLocal.Z - pelvisLocal.Z));
        }
    }

    public bool TryGetActiveJointMotionMeters(string jointName, out double motionMeters)
    {
        foreach (var pair in ActiveJointMotion)
        {
            if (!string.Equals(pair.Key, jointName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            motionMeters = pair.Value.RangeMeters;
            return pair.Value.Samples >= 2;
        }

        motionMeters = 0;
        return false;
    }

    public string FormatActiveJointMotion(IReadOnlyList<string> jointNames)
    {
        if (jointNames.Count == 0)
        {
            return "-";
        }

        var parts = new List<string>();
        foreach (var jointName in jointNames)
        {
            if (TryGetActiveJointMotionMeters(jointName, out var motionMeters))
            {
                parts.Add($"{jointName}:{motionMeters:0.000}m");
            }
            else
            {
                parts.Add($"{jointName}:-");
            }
        }

        return string.Join(",", parts);
    }

    public bool TryGetActiveJointConfidenceRatio(string jointName, out double ratio)
    {
        foreach (var pair in ActiveJointConfidenceByName)
        {
            if (!string.Equals(pair.Key, jointName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ratio = pair.Value.ConfidenceRatio;
            return pair.Value.Samples > 0;
        }

        ratio = 0;
        return false;
    }

    public string FormatRequiredActiveJointConfidence(IReadOnlyList<string> jointNames)
    {
        if (jointNames.Count == 0)
        {
            return "-";
        }

        var parts = new List<string>();
        foreach (var jointName in jointNames)
        {
            if (TryGetActiveJointConfidenceRatio(jointName, out var ratio))
            {
                parts.Add($"{jointName}:{ratio:P0}");
            }
            else
            {
                parts.Add($"{jointName}:-");
            }
        }

        return string.Join(",", parts);
    }

    public void ObserveActiveRoi(bool isInsideTrackingRoi, bool isInsideFootMarker)
    {
        if (isInsideTrackingRoi)
        {
            ActiveInsideRoiFrames++;
        }

        if (isInsideFootMarker)
        {
            ActiveInsideMarkerFrames++;
        }
    }

    public void ObserveActiveBody(int bodyCount, long selectedBodyId)
    {
        ActiveBodySamples++;
        MaxActiveBodyCount = Math.Max(MaxActiveBodyCount, bodyCount);
        ActiveBodyCountHistogram[bodyCount] =
            ActiveBodyCountHistogram.TryGetValue(bodyCount, out var count) ? count + 1 : 1;

        if (selectedBodyId < 0)
        {
            ActiveMissingSelectedBodyFrames++;
            return;
        }

        ActiveSelectedBodyIds.Add(selectedBodyId);
        if (ActiveSelectedBodyId is { } activeSelectedBodyId && selectedBodyId != activeSelectedBodyId)
        {
            SelectedBodyIdChanges++;
        }

        ActiveSelectedBodyId = selectedBodyId;
        LastSelectedBodyId = selectedBodyId;
        ActiveSelectedBodySamples++;
    }

    public long GetActiveExtraBodyFrames(int maxActiveBodyCount)
    {
        if (maxActiveBodyCount < 0)
        {
            return 0;
        }

        long extraFrames = 0;
        foreach (var pair in ActiveBodyCountHistogram)
        {
            if (pair.Key > maxActiveBodyCount)
            {
                extraFrames += pair.Value;
            }
        }

        return extraFrames;
    }

    public string FormatActiveBodyStability(int? maxActiveBodyCount)
    {
        if (ActiveBodySamples == 0)
        {
            return "bodySamples=0";
        }

        var maxBodies = maxActiveBodyCount ?? FieldBodyCountPolicy.DefaultMaxActiveBodyCount;
        var extraFrames = GetActiveExtraBodyFrames(maxBodies);
        var extraRatio = extraFrames / (double)ActiveBodySamples;
        var bodyCounts = string.Join("|", ActiveBodyCountHistogram.Select(pair => $"{pair.Key}:{pair.Value}"));
        var selectedIds = ActiveSelectedBodyIds.Count > 0
            ? string.Join("|", ActiveSelectedBodyIds.Order())
            : "-";

        return
            $"bodySamples={ActiveBodySamples} " +
            $"extraBodies>{maxBodies}={extraFrames}({extraRatio:P0}) " +
            $"missingSelected={ActiveMissingSelectedBodyFrames} " +
            $"selectedIds={selectedIds} " +
            $"bodyCounts={bodyCounts}";
    }

    public double? GetMaxPlayerGapMs(DateTimeOffset finishedAt)
    {
        if (LastPlayerReceivedAt is null)
        {
            return null;
        }

        return Math.Max(MaxPlayerGapMs, CalculateGapMs(LastPlayerReceivedAt, finishedAt));
    }

    public double? GetMaxActiveGapMs(DateTimeOffset finishedAt)
    {
        if (LastActiveReceivedAt is null)
        {
            return null;
        }

        return Math.Max(MaxActiveGapMs, CalculateGapMs(LastActiveReceivedAt, finishedAt));
    }

    private static double CalculateGapMs(DateTimeOffset? previous, DateTimeOffset current)
    {
        return previous is null ? 0 : Math.Max(0, (current - previous.Value).TotalMilliseconds);
    }
}

internal sealed class JointMotionStat
{
    private float _minX;
    private float _maxX;
    private float _minY;
    private float _maxY;
    private float _minZ;
    private float _maxZ;

    public JointMotionStat(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public int Samples { get; private set; }

    public double RangeMeters
    {
        get
        {
            if (Samples < 2)
            {
                return 0;
            }

            var dx = _maxX - _minX;
            var dy = _maxY - _minY;
            var dz = _maxZ - _minZ;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public void Observe(Vector3Dto position)
    {
        if (Samples == 0)
        {
            _minX = _maxX = position.X;
            _minY = _maxY = position.Y;
            _minZ = _maxZ = position.Z;
        }
        else
        {
            _minX = Math.Min(_minX, position.X);
            _maxX = Math.Max(_maxX, position.X);
            _minY = Math.Min(_minY, position.Y);
            _maxY = Math.Max(_maxY, position.Y);
            _minZ = Math.Min(_minZ, position.Z);
            _maxZ = Math.Max(_maxZ, position.Z);
        }

        Samples++;
    }
}

internal sealed class JointConfidenceStat
{
    public JointConfidenceStat(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public long Samples { get; private set; }
    public long ConfidentSamples { get; private set; }
    public double ConfidenceRatio => Samples > 0 ? ConfidentSamples / (double)Samples : 0;

    public void Observe(float confidence, double threshold)
    {
        Samples++;
        if (confidence >= threshold)
        {
            ConfidentSamples++;
        }
    }
}

internal sealed record ForwardTarget(string Host, int Port);

internal sealed class ProbeOptions
{
    public const string Usage = """
Usage:
  dotnet run --project tools/DsccUdpProbe -- [port] [durationSeconds]
  dotnet run --project tools/DsccUdpProbe -- --check-field-config config\wall-a.local.json
  dotnet run --project tools/DsccUdpProbe -- 55010 60 --field-strict --expect-stations all --expect-serials-from-config config\wall-a.local.json
  dotnet run --project tools/DsccUdpProbe -- 55010 30 --expect-stations all --min-joints 32 --min-fps 10 --require-player --require-active --max-decode-errors 0
  dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --expect-serials 1=CL2Z...,2=CL2Z... --min-player-ratio 0.8 --min-active-ratio 0.8
  dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --max-frame-gap-ms 500 --max-player-gap-ms 1000 --max-active-gap-ms 1000
  dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --fail-extra-stations
  dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --fail-duplicate-serials
  dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --min-active-confidence 0.45 --joint-confidence-threshold 0.45 --min-active-joint-confidence-ratio 0.8
  dotnet run --project tools/DsccUdpProbe -- 55010 60 --expect-stations all --expect-serials-from-config config\wall-a.local.json
  dotnet run --project tools/DsccUdpProbe -- 55130 60 --field-strict --expect-stations all --forward-to 127.0.0.1:55010

Options:
  --field-strict                    Apply the default live field gate:
                                    fail extra/duplicate stations, require 32 joints,
                                    10 fps, player+Active frames, 80% player/Active
                                    ratio, 0.45 Active confidence, 80% confident
                                    Active joints, 80% confident app-driving
                                    joints, 80% Active ROI frames, zero selected-body id changes,
                                    frame/player/Active gap limits, FemtoMega
                                    device type, and zero decode errors.
                                    Station ids/serials must still be supplied separately.
  --expect-stations <all|1,2,3,4>  Require these station ids to appear.
  --check-field-config <path>       Validate DSCC config for four Femto Mega stations,
                                    CUDA-only tracking, fixed unique serials, and
                                    NFOV_UNBINNED <=15fps. Exits without listening.
  --expect-serials <1=SER,2=SER>   Require station ids to use these camera serials.
  --expect-serials-from-config <path>
                                    Read expected serials from DSCC station config.
  --expect-device-type <type>       Require every checked station to publish this device type.
  --expect-protocol-version <ver>   Require every checked station to publish this DSCC protocol version.
  --min-joints <count>             Require each checked station to reach this joint count.
  --min-fps <fps>                  Require each checked station to reach this packet rate.
  --require-player                 Require each checked station to receive at least one hasPlayer frame.
  --require-active                 Require each checked station to receive at least one Active frame.
  --min-player-ratio <0..1>        Require this fraction of frames to have hasPlayer=true.
  --min-active-ratio <0..1>        Require this fraction of frames to be Active.
  --min-player-confidence <0..1>   Require player frames to stay at or above this confidence.
  --min-active-confidence <0..1>   Require Active frames to stay at or above this confidence.
  --joint-confidence-threshold <0..1>
                                    Confidence threshold used for Active joint ratio.
  --min-active-joint-confidence-ratio <0..1>
                                    Require this fraction of Active joints to meet the joint threshold.
  --required-active-joints <name,name>
                                    Require each named Active joint to meet the joint threshold.
  --min-required-active-joint-confidence-ratio <0..1>
                                    Required Active joint confidence ratio per named joint.
                                    Default: 0.8 when --required-active-joints is supplied.
  --min-active-joint-motion-m <m>   Require selected Active joints to move at least this many meters.
  --motion-joints <name,name>       Joints used by Active motion validation, measured relative to Pelvis.
                                    Default: Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight.
  --min-active-roi-ratio <0..1>    Require this fraction of Active frames to be inside tracking ROI.
  --require-active-inside-roi      Require every Active frame to be inside tracking ROI.
  --min-active-marker-ratio <0..1> Require this fraction of Active frames to be inside foot marker.
  --require-active-inside-marker   Require every Active frame to be inside foot marker.
  --max-active-body-count <count>  Fail if Active frames report more bodies than this.
  --max-selected-body-id-changes <count>
                                    Fail if selected K4ABT body id changes more than this.
  --max-frame-gap-ms <ms>          Fail if station packets stop longer than this.
  --max-player-gap-ms <ms>         Fail if hasPlayer frames stop longer than this.
  --max-active-gap-ms <ms>         Fail if Active frames stop longer than this.
  --fail-extra-stations            Fail if any station outside the expected set appears.
  --fail-duplicate-serials         Fail if a camera serial appears on multiple stations.
  --forward-to <host:port>         Forward every successfully decoded DSCC packet to this UDP target.
                                    Use this as a tee when the live probe must validate while the
                                    Tauri app also receives the same stream.
  --max-decode-errors <count>      Fail if MessagePack decode errors exceed this count.
  -h, --help                       Print this help.
""";

    public int Port { get; private set; } = ProtocolConstants.DefaultSkeletonPort;
    public int DurationSeconds { get; private set; } = 30;
    public IReadOnlyList<int> ExpectedStations { get; private set; } = [];
    public IReadOnlyDictionary<int, string> ExpectedSerials { get; private set; } = new Dictionary<int, string>();
    public string FieldConfigPath { get; private set; } = string.Empty;
    public string ExpectedDeviceType { get; private set; } = string.Empty;
    public int? ExpectedProtocolVersion { get; private set; }
    public int MinJoints { get; private set; }
    public double MinFps { get; private set; }
    public double MinPlayerRatio { get; private set; }
    public double MinActiveRatio { get; private set; }
    public double? MinPlayerConfidence { get; private set; }
    public double? MinActiveConfidence { get; private set; }
    public double JointConfidenceThreshold { get; private set; } = 0.45;
    public double MinActiveJointConfidenceRatio { get; private set; }
    public IReadOnlyList<string> RequiredActiveJointNames { get; private set; } = [];
    public double MinRequiredActiveJointConfidenceRatio { get; private set; }
    public double MinActiveJointMotionMeters { get; private set; }
    public IReadOnlyList<string> MotionJointNames { get; private set; } =
        FieldSkeletonAcceptancePolicy.AppDrivingJointNames;
    public double MinActiveRoiRatio { get; private set; }
    public double MinActiveMarkerRatio { get; private set; }
    public int? MaxActiveBodyCount { get; private set; }
    public int? MaxSelectedBodyIdChanges { get; private set; }
    public int? MaxFrameGapMs { get; private set; }
    public int? MaxPlayerGapMs { get; private set; }
    public int? MaxActiveGapMs { get; private set; }
    public bool FailExtraStations { get; private set; }
    public bool FailDuplicateSerials { get; private set; }
    public bool RequirePlayer { get; private set; }
    public bool RequireActive { get; private set; }
    public long? MaxDecodeErrors { get; private set; }
    public ForwardTarget? ForwardTo { get; private set; }
    public bool FieldStrict { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool ValidationEnabled =>
        FieldStrict ||
        !string.IsNullOrWhiteSpace(FieldConfigPath) ||
        ExpectedStations.Count > 0 ||
        MinJoints > 0 ||
        MinFps > 0 ||
        ExpectedSerials.Count > 0 ||
        !string.IsNullOrWhiteSpace(ExpectedDeviceType) ||
        ExpectedProtocolVersion.HasValue ||
        MinPlayerRatio > 0 ||
        MinActiveRatio > 0 ||
        MinPlayerConfidence.HasValue ||
        MinActiveConfidence.HasValue ||
        MinActiveJointConfidenceRatio > 0 ||
        RequiredActiveJointNames.Count > 0 ||
        MinActiveJointMotionMeters > 0 ||
        MinActiveRoiRatio > 0 ||
        MinActiveMarkerRatio > 0 ||
        MaxActiveBodyCount.HasValue ||
        MaxSelectedBodyIdChanges.HasValue ||
        MaxFrameGapMs.HasValue ||
        MaxPlayerGapMs.HasValue ||
        MaxActiveGapMs.HasValue ||
        FailExtraStations ||
        FailDuplicateSerials ||
        RequirePlayer ||
        RequireActive ||
        MaxDecodeErrors.HasValue;

    public static ProbeOptions Parse(string[] args)
    {
        var options = new ProbeOptions();
        var index = 0;

        if (index < args.Length && IsHelp(args[index]))
        {
            options.ShowHelp = true;
            return options;
        }

        if (index < args.Length && !IsOption(args[index]))
        {
            options.Port = int.Parse(args[index]);
            index++;
        }

        if (index < args.Length && !IsOption(args[index]))
        {
            options.DurationSeconds = int.Parse(args[index]);
            index++;
        }

        while (index < args.Length)
        {
            var token = args[index++];
            if (IsHelp(token))
            {
                options.ShowHelp = true;
                return options;
            }

            var (name, inlineValue) = SplitOption(token);
            switch (name)
            {
                case "--field-strict":
                    options.FieldStrict = true;
                    break;
                case "--expect-stations":
                    options.ExpectedStations = ParseStations(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--check-field-config":
                    options.FieldConfigPath = ReadOptionValue(args, ref index, inlineValue, name).Trim();
                    if (string.IsNullOrWhiteSpace(options.FieldConfigPath))
                    {
                        throw new ArgumentException($"{name} requires a config path.");
                    }

                    break;
                case "--expect-serials":
                    options.ExpectedSerials = MergeSerials(
                        options.ExpectedSerials,
                        ParseSerials(ReadOptionValue(args, ref index, inlineValue, name)),
                        name);
                    break;
                case "--expect-serials-from-config":
                    options.ExpectedSerials = MergeSerials(
                        options.ExpectedSerials,
                        ParseSerialsFromConfig(ReadOptionValue(args, ref index, inlineValue, name)),
                        name);
                    break;
                case "--expect-device-type":
                    options.ExpectedDeviceType = ReadOptionValue(args, ref index, inlineValue, name).Trim();
                    if (string.IsNullOrWhiteSpace(options.ExpectedDeviceType))
                    {
                        throw new ArgumentException($"{name} requires a non-empty device type.");
                    }

                    break;
                case "--expect-protocol-version":
                    options.ExpectedProtocolVersion = ParsePositiveInt(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--min-joints":
                    options.MinJoints = int.Parse(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--min-fps":
                    options.MinFps = double.Parse(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--min-player-ratio":
                    options.MinPlayerRatio = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--min-active-ratio":
                    options.MinActiveRatio = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--min-player-confidence":
                    options.MinPlayerConfidence = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--min-active-confidence":
                    options.MinActiveConfidence = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--joint-confidence-threshold":
                    options.JointConfidenceThreshold = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--min-active-joint-confidence-ratio":
                    options.MinActiveJointConfidenceRatio = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--required-active-joints":
                    options.RequiredActiveJointNames = ParseJointNames(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--min-required-active-joint-confidence-ratio":
                    options.MinRequiredActiveJointConfidenceRatio = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--min-active-joint-motion-m":
                    options.MinActiveJointMotionMeters = ParsePositiveDouble(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--motion-joints":
                    options.MotionJointNames = ParseJointNames(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--min-active-roi-ratio":
                    options.MinActiveRoiRatio = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--require-active-inside-roi":
                    options.MinActiveRoiRatio = 1;
                    break;
                case "--min-active-marker-ratio":
                    options.MinActiveMarkerRatio = ParseRatio(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--require-active-inside-marker":
                    options.MinActiveMarkerRatio = 1;
                    break;
                case "--max-active-body-count":
                    options.MaxActiveBodyCount = ParseNonNegativeInt(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--max-selected-body-id-changes":
                    options.MaxSelectedBodyIdChanges = ParseNonNegativeInt(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--max-frame-gap-ms":
                    options.MaxFrameGapMs = ParsePositiveMilliseconds(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--max-player-gap-ms":
                    options.MaxPlayerGapMs = ParsePositiveMilliseconds(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--max-active-gap-ms":
                    options.MaxActiveGapMs = ParsePositiveMilliseconds(ReadOptionValue(args, ref index, inlineValue, name), name);
                    break;
                case "--fail-extra-stations":
                    options.FailExtraStations = true;
                    break;
                case "--fail-duplicate-serials":
                    options.FailDuplicateSerials = true;
                    break;
                case "--forward-to":
                    options.ForwardTo = ParseForwardTarget(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                case "--require-player":
                    options.RequirePlayer = true;
                    break;
                case "--require-active":
                    options.RequireActive = true;
                    break;
                case "--max-decode-errors":
                    options.MaxDecodeErrors = long.Parse(ReadOptionValue(args, ref index, inlineValue, name));
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {token}");
            }
        }

        if (options.ForwardTo is { } forwardTo && forwardTo.Port == options.Port)
        {
            throw new ArgumentException("--forward-to port must differ from the probe listen port to avoid UDP forwarding loops.");
        }

        options.ApplyFieldStrictDefaults();
        if (options.RequiredActiveJointNames.Count > 0 && options.MinRequiredActiveJointConfidenceRatio <= 0)
        {
            options.MinRequiredActiveJointConfidenceRatio = 0.8;
        }

        return options;
    }

    private void ApplyFieldStrictDefaults()
    {
        if (!FieldStrict)
        {
            return;
        }

        FailExtraStations = true;
        FailDuplicateSerials = true;
        RequirePlayer = true;
        RequireActive = true;
        ExpectedDeviceType = string.IsNullOrWhiteSpace(ExpectedDeviceType) ? "FemtoMega" : ExpectedDeviceType;
        ExpectedProtocolVersion ??= ProtocolConstants.CurrentProtocolVersion;
        MinJoints = MinJoints > 0 ? MinJoints : 32;
        MinFps = MinFps > 0 ? MinFps : 10;
        MinPlayerRatio = MinPlayerRatio > 0 ? MinPlayerRatio : 0.8;
        MinActiveRatio = MinActiveRatio > 0 ? MinActiveRatio : 0.8;
        MinActiveConfidence ??= 0.45;
        MinActiveJointConfidenceRatio = MinActiveJointConfidenceRatio > 0 ? MinActiveJointConfidenceRatio : 0.8;
        RequiredActiveJointNames = RequiredActiveJointNames.Count > 0
            ? RequiredActiveJointNames
            : FieldSkeletonAcceptancePolicy.AppDrivingJointNames;
        MinActiveRoiRatio = MinActiveRoiRatio > 0 ? MinActiveRoiRatio : 0.8;
        MaxSelectedBodyIdChanges ??= 0;
        MaxFrameGapMs ??= 500;
        MaxPlayerGapMs ??= 1000;
        MaxActiveGapMs ??= 1000;
        MaxDecodeErrors ??= 0;
    }

    private static bool IsHelp(string value)
    {
        return string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOption(string value)
    {
        return value.StartsWith("--", StringComparison.Ordinal);
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

        if (index >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}");
        }

        return args[index++];
    }

    private static IReadOnlyList<int> ParseStations(string value)
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

    private static IReadOnlyDictionary<int, string> ParseSerials(string value)
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

            serials[stationId] = pair[(separator + 1)..].Trim();
        }

        if (serials.Count == 0)
        {
            throw new ArgumentException("At least one station serial mapping is required.", nameof(value));
        }

        return serials;
    }

    private static IReadOnlyList<string> ParseJointNames(string value)
    {
        var jointNames = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (jointNames.Length == 0)
        {
            throw new ArgumentException("At least one joint name is required.", nameof(value));
        }

        return jointNames;
    }

    private static ForwardTarget ParseForwardTarget(string value)
    {
        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            throw new ArgumentException($"Invalid --forward-to target: {value}. Use host:port.");
        }

        var host = value[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("--forward-to host cannot be empty.");
        }

        var port = ParsePositiveInt(value[(separatorIndex + 1)..], "--forward-to");
        if (port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "--forward-to port must be <= 65535.");
        }

        return new ForwardTarget(host, port);
    }

    private static IReadOnlyDictionary<int, string> ParseSerialsFromConfig(string path)
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
            if (stationId <= 0)
            {
                throw new ArgumentException($"Invalid stationId in config: {stationId}", nameof(path));
            }

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

        if (serials.Count == 0)
        {
            throw new ArgumentException($"No enabled station serials found in config file: {path}", nameof(path));
        }

        return serials;
    }

    private static IReadOnlyDictionary<int, string> MergeSerials(
        IReadOnlyDictionary<int, string> existing,
        IReadOnlyDictionary<int, string> incoming,
        string source)
    {
        var merged = new Dictionary<int, string>(existing);
        foreach (var (stationId, serial) in incoming)
        {
            if (merged.TryGetValue(stationId, out var existingSerial) &&
                !string.Equals(existingSerial, serial, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Conflicting expected serial for station {stationId}: {existingSerial} vs {serial} from {source}");
            }

            merged[stationId] = serial;
        }

        return merged;
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            throw new ArgumentException($"Missing or invalid integer property: {propertyName}");
        }

        return value;
    }

    private static bool? GetOptionalBool(JsonElement element, string propertyName)
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

    private static string GetNestedString(JsonElement element, string objectName, string propertyName)
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

    private static double ParseRatio(string value, string optionName)
    {
        var ratio = double.Parse(value);
        if (ratio < 0 || ratio > 1)
        {
            throw new ArgumentOutOfRangeException(optionName, "Ratio must be between 0 and 1.");
        }

        return ratio;
    }

    private static int ParsePositiveMilliseconds(string value, string optionName)
    {
        var milliseconds = int.Parse(value);
        if (milliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(optionName, "Milliseconds must be greater than zero.");
        }

        return milliseconds;
    }

    private static int ParsePositiveInt(string value, string optionName)
    {
        var number = int.Parse(value);
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(optionName, "Value must be greater than zero.");
        }

        return number;
    }

    private static double ParsePositiveDouble(string value, string optionName)
    {
        var number = double.Parse(value);
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(optionName, "Value must be greater than zero.");
        }

        return number;
    }

    private static int ParseNonNegativeInt(string value, string optionName)
    {
        var number = int.Parse(value);
        if (number < 0)
        {
            throw new ArgumentOutOfRangeException(optionName, "Value cannot be negative.");
        }

        return number;
    }
}
