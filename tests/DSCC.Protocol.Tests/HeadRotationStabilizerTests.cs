using DSCC.Protocol;

namespace DSCC.Protocol.Tests;

public sealed class HeadRotationStabilizerTests
{
    [Fact]
    public void Apply_ClampsLargeHeadRotationStep()
    {
        var stabilizer = new HeadRotationStabilizer();
        var options = new HeadRotationStabilizerOptions
        {
            SmoothingHalfLifeSeconds = 0.0f,
            MaxDegreesPerSecond = 30.0f,
            MinConfidence = 0.45f,
            DeadZoneDegrees = 0.0f
        };
        var first = CreateFrame(1_000_000, RotationY(0.0f), 0.9f);
        var second = CreateFrame(1_066_667, RotationY(180.0f), 0.9f);

        _ = stabilizer.Apply(first, options);
        var actual = stabilizer.Apply(second, options);

        var head = Assert.Single(actual.Joints, joint => joint.Name == "Head");
        var angle = AngleDegrees(RotationY(0.0f), head.RotationLocal);
        Assert.InRange(angle, 0.1f, 2.1f);
    }

    [Fact]
    public void Apply_HoldsPreviousHeadRotation_WhenConfidenceIsLow()
    {
        var stabilizer = new HeadRotationStabilizer();
        var options = new HeadRotationStabilizerOptions
        {
            SmoothingHalfLifeSeconds = 0.0f,
            MaxDegreesPerSecond = 0.0f,
            MinConfidence = 0.45f,
            DeadZoneDegrees = 0.0f
        };
        var stableRotation = RotationY(20.0f);
        var first = CreateFrame(1_000_000, stableRotation, 0.9f);
        var second = CreateFrame(1_066_667, RotationY(90.0f), 0.1f);

        _ = stabilizer.Apply(first, options);
        var actual = stabilizer.Apply(second, options);

        var head = Assert.Single(actual.Joints, joint => joint.Name == "Head");
        Assert.True(AngleDegrees(stableRotation, head.RotationLocal) < 0.01f);
    }

    [Fact]
    public void Apply_KeepsQuaternionSignContinuity_ForEquivalentRotations()
    {
        var stabilizer = new HeadRotationStabilizer();
        var options = new HeadRotationStabilizerOptions
        {
            SmoothingHalfLifeSeconds = 0.0f,
            MaxDegreesPerSecond = 0.0f,
            MinConfidence = 0.45f,
            DeadZoneDegrees = 0.0f
        };
        var stableRotation = RotationY(35.0f);
        var first = CreateFrame(1_000_000, stableRotation, 0.9f);
        var second = CreateFrame(1_066_667, Negate(stableRotation), 0.9f);

        _ = stabilizer.Apply(first, options);
        var actual = stabilizer.Apply(second, options);

        var head = Assert.Single(actual.Joints, joint => joint.Name == "Head");
        Assert.True(AngleDegrees(stableRotation, head.RotationLocal) < 0.01f);
    }

    [Fact]
    public void Apply_LeavesNonHeadJointsUnchanged()
    {
        var stabilizer = new HeadRotationStabilizer();
        var options = new HeadRotationStabilizerOptions
        {
            SmoothingHalfLifeSeconds = 0.0f,
            MaxDegreesPerSecond = 30.0f,
            MinConfidence = 0.45f,
            DeadZoneDegrees = 0.0f
        };
        var wristRotation = RotationY(90.0f);
        var frame = CreateFrame(1_000_000, RotationY(0.0f), 0.9f, wristRotation);

        var actual = stabilizer.Apply(frame, options);

        var wrist = Assert.Single(actual.Joints, joint => joint.Name == "WristLeft");
        Assert.Equal(wristRotation.X, wrist.RotationLocal.X);
        Assert.Equal(wristRotation.Y, wrist.RotationLocal.Y);
        Assert.Equal(wristRotation.Z, wrist.RotationLocal.Z);
        Assert.Equal(wristRotation.W, wrist.RotationLocal.W);
    }

    [Fact]
    public void Apply_PreservesBodySelectionMetadata_WhenFrameIsCopied()
    {
        var stabilizer = new HeadRotationStabilizer();
        var options = new HeadRotationStabilizerOptions
        {
            SmoothingHalfLifeSeconds = 0.0f,
            MaxDegreesPerSecond = 30.0f,
            MinConfidence = 0.45f,
            DeadZoneDegrees = 0.0f
        };
        var first = CreateFrame(1_000_000, RotationY(0.0f), 0.9f);
        var second = CreateFrame(1_066_667, RotationY(180.0f), 0.9f);

        _ = stabilizer.Apply(first, options);
        var actual = stabilizer.Apply(second, options);

        Assert.Equal(second.BodyCount, actual.BodyCount);
        Assert.Equal(second.SelectedBodyId, actual.SelectedBodyId);
    }

    private static StationSkeletonFrame CreateFrame(
        long timestampUsec,
        QuaternionDto headRotation,
        float headConfidence,
        QuaternionDto? wristRotation = null)
    {
        return new StationSkeletonFrame
        {
            StationId = 1,
            TimestampUsec = timestampUsec,
            HasPlayer = true,
            State = StationStateDto.Active,
            Confidence = 0.9f,
            BodyCount = 2,
            SelectedBodyId = 42,
            Joints =
            [
                new JointFrameDto
                {
                    Name = "Head",
                    PositionLocal = new Vector3Dto(0.0f, 1.7f, 2.0f),
                    RotationLocal = headRotation,
                    Confidence = headConfidence
                },
                new JointFrameDto
                {
                    Name = "Neck",
                    PositionLocal = new Vector3Dto(0.0f, 1.5f, 2.0f),
                    RotationLocal = headRotation,
                    Confidence = headConfidence
                },
                new JointFrameDto
                {
                    Name = "WristLeft",
                    PositionLocal = new Vector3Dto(-0.5f, 1.2f, 2.0f),
                    RotationLocal = wristRotation ?? QuaternionDto.Identity,
                    Confidence = 0.9f
                }
            ]
        };
    }

    private static QuaternionDto RotationY(float degrees)
    {
        var radians = degrees * MathF.PI / 180.0f;
        var half = radians / 2.0f;
        return new QuaternionDto(0.0f, MathF.Sin(half), 0.0f, MathF.Cos(half));
    }

    private static QuaternionDto Negate(QuaternionDto value)
    {
        return new QuaternionDto(-value.X, -value.Y, -value.Z, -value.W);
    }

    private static float AngleDegrees(QuaternionDto first, QuaternionDto second)
    {
        var dot = Math.Abs(first.X * second.X + first.Y * second.Y + first.Z * second.Z + first.W * second.W);
        dot = Math.Clamp(dot, -1.0f, 1.0f);
        return 2.0f * MathF.Acos(dot) * 180.0f / MathF.PI;
    }
}
