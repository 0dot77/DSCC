using System.Text.Json;
using DSCC.Protocol;

namespace DSCC.Replay;

public sealed class SkeletonReplaySource
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StationSkeletonFrame>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var frames = new List<StationSkeletonFrame>();

        await foreach (var frame in ReadFramesAsync(filePath, cancellationToken).ConfigureAwait(false))
        {
            frames.Add(frame);
        }

        return frames;
    }

    public async IAsyncEnumerable<StationSkeletonFrame> ReadFramesAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream);

        var lineNumber = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            StationSkeletonFrame? frame;
            try
            {
                frame = JsonSerializer.Deserialize<StationSkeletonFrame>(line, SerializerOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Invalid skeleton replay JSON at line {lineNumber}.", exception);
            }

            if (frame is null)
            {
                throw new InvalidDataException($"Skeleton replay line {lineNumber} did not contain a frame.");
            }

            yield return frame;
        }
    }

    public async IAsyncEnumerable<StationSkeletonFrame> PlayAsync(
        string filePath,
        bool preserveTiming = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StationSkeletonFrame? previous = null;

        await foreach (var frame in ReadFramesAsync(filePath, cancellationToken).ConfigureAwait(false))
        {
            if (preserveTiming && previous is not null)
            {
                var delayUsec = Math.Max(0, frame.TimestampUsec - previous.TimestampUsec);
                if (delayUsec > 0)
                {
                    await Task.Delay(TimeSpan.FromMicroseconds(delayUsec), cancellationToken).ConfigureAwait(false);
                }
            }

            yield return frame;
            previous = frame;
        }
    }
}
