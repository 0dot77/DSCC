using DSCC.Protocol;
using DSCC.Replay;

namespace DSCC.Replay.Tests;

public sealed class SkeletonReplayFileTests
{
    [Fact]
    public async Task RecorderAndReplaySource_PreserveBodySelectionMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dscc-replay-{Guid.NewGuid():N}.jsonl");
        try
        {
            var frames = new[]
            {
                new StationSkeletonFrame
                {
                    StationId = 2,
                    CameraSerial = "MEGA-002",
                    DeviceType = "FemtoMega",
                    TimestampUsec = 1_000_000,
                    HasPlayer = true,
                    State = StationStateDto.Active,
                    Confidence = 0.91f,
                    PelvisLocal = new Vector3Dto(0.1f, 1.0f, 2.2f),
                    BodyRotation = QuaternionDto.Identity,
                    Joints =
                    [
                        new JointFrameDto
                        {
                            Name = "Pelvis",
                            PositionLocal = new Vector3Dto(0.1f, 1.0f, 2.2f),
                            RotationLocal = QuaternionDto.Identity,
                            Confidence = 0.91f
                        }
                    ],
                    BodyCount = 2,
                    SelectedBodyId = 88
                }
            };

            await new SkeletonRecorder().RecordAsync(frames, path);
            var actual = await new SkeletonReplaySource().LoadAsync(path);

            var frame = Assert.Single(actual);
            Assert.Equal(2, frame.BodyCount);
            Assert.Equal(88, frame.SelectedBodyId);
            Assert.Equal("MEGA-002", frame.CameraSerial);
            Assert.Single(frame.Joints);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
