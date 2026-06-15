using DSCC.Protocol;
using MessagePack;

namespace DSCC.Protocol.Tests;

public sealed class MessagePackRoundTripTests
{
    [Fact]
    public void StationSkeletonFrame_RoundTrips_WithMessagePack()
    {
        StationSkeletonFrame source = new()
        {
            StationId = 1,
            CameraSerial = "BOLT_SERIAL",
            DeviceType = "FemtoBolt",
            TimestampUsec = 123456789L,
            HasPlayer = true,
            State = StationStateDto.Active,
            Confidence = 0.87f,
            IsInsideFootMarker = true,
            IsInsideTrackingRoi = true,
            TrackingLostSeconds = 0.0f,
            PelvisLocal = new Vector3Dto(0.1f, 1.2f, 2.3f),
            BodyRotation = new QuaternionDto(0.0f, 0.2f, 0.0f, 0.98f),
            AnchorPosition = new Vector3Dto(-3.0f, 0.0f, 0.5f),
            AnchorRotationYDegrees = 90.0f,
            Joints =
            [
                new JointFrameDto
                {
                    Name = "Pelvis",
                    PositionLocal = new Vector3Dto(0.1f, 1.2f, 2.3f),
                    RotationLocal = QuaternionDto.Identity,
                    Confidence = 0.91f
                },
                new JointFrameDto
                {
                    Name = "Head",
                    PositionLocal = new Vector3Dto(0.2f, 1.8f, 2.4f),
                    RotationLocal = new QuaternionDto(0.0f, 0.1f, 0.0f, 0.99f),
                    Confidence = 0.82f
                }
            ]
        };

        byte[] payload = MessagePackSerializer.Serialize(source);
        StationSkeletonFrame actual = MessagePackSerializer.Deserialize<StationSkeletonFrame>(payload);

        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(source.StationId, actual.StationId);
        Assert.Equal(source.CameraSerial, actual.CameraSerial);
        Assert.Equal(source.DeviceType, actual.DeviceType);
        Assert.Equal(source.TimestampUsec, actual.TimestampUsec);
        Assert.Equal(source.HasPlayer, actual.HasPlayer);
        Assert.Equal(source.State, actual.State);
        Assert.Equal(source.Confidence, actual.Confidence);
        Assert.Equal(source.IsInsideFootMarker, actual.IsInsideFootMarker);
        Assert.Equal(source.IsInsideTrackingRoi, actual.IsInsideTrackingRoi);
        Assert.Equal(source.TrackingLostSeconds, actual.TrackingLostSeconds);
        Assert.Equal(source.PelvisLocal.X, actual.PelvisLocal.X);
        Assert.Equal(source.PelvisLocal.Y, actual.PelvisLocal.Y);
        Assert.Equal(source.PelvisLocal.Z, actual.PelvisLocal.Z);
        Assert.Equal(source.BodyRotation.X, actual.BodyRotation.X);
        Assert.Equal(source.BodyRotation.Y, actual.BodyRotation.Y);
        Assert.Equal(source.BodyRotation.Z, actual.BodyRotation.Z);
        Assert.Equal(source.BodyRotation.W, actual.BodyRotation.W);
        Assert.Equal(source.Joints.Length, actual.Joints.Length);
        Assert.Equal("Pelvis", actual.Joints[0].Name);
        Assert.Equal(0.91f, actual.Joints[0].Confidence);
        Assert.Equal("Head", actual.Joints[1].Name);
        Assert.Equal(1.8f, actual.Joints[1].PositionLocal.Y);
        Assert.Equal(source.AnchorPosition.X, actual.AnchorPosition.X);
        Assert.Equal(source.AnchorPosition.Y, actual.AnchorPosition.Y);
        Assert.Equal(source.AnchorPosition.Z, actual.AnchorPosition.Z);
        Assert.Equal(source.AnchorRotationYDegrees, actual.AnchorRotationYDegrees);
    }

    [Fact]
    public void DsccEvent_RoundTrips_WithMessagePack()
    {
        DsccEvent source = new()
        {
            EventType = ProtocolConstants.PlayerEnterEvent,
            StationId = 1,
            TimestampUsec = 987654321L,
            Properties =
            {
                ["cameraSerial"] = "BOLT_SERIAL",
                ["state"] = StationStateDto.Entering.ToString()
            }
        };

        byte[] payload = MessagePackSerializer.Serialize(source);
        DsccEvent actual = MessagePackSerializer.Deserialize<DsccEvent>(payload);

        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(source.EventType, actual.EventType);
        Assert.Equal(source.StationId, actual.StationId);
        Assert.Equal(source.TimestampUsec, actual.TimestampUsec);
        Assert.Equal(source.Properties["cameraSerial"], actual.Properties["cameraSerial"]);
        Assert.Equal(source.Properties["state"], actual.Properties["state"]);
    }

    [Fact]
    public void MirrorPerformerFacingCamera_MirrorsXAndSwapsLeftRightJoints()
    {
        StationSkeletonFrame source = new()
        {
            StationId = 1,
            CameraSerial = "BOLT_SERIAL",
            DeviceType = "FemtoBolt",
            TimestampUsec = 123456789L,
            HasPlayer = true,
            State = StationStateDto.Active,
            Confidence = 0.87f,
            IsInsideFootMarker = true,
            IsInsideTrackingRoi = true,
            TrackingLostSeconds = 0.0f,
            PelvisLocal = new Vector3Dto(0.25f, 1.2f, 2.3f),
            BodyRotation = new QuaternionDto(0.1f, 0.2f, -0.3f, 0.9f),
            AnchorPosition = new Vector3Dto(-3.0f, 0.0f, 0.5f),
            AnchorRotationYDegrees = 45.0f,
            Joints =
            [
                new JointFrameDto
                {
                    Name = "WristLeft",
                    PositionLocal = new Vector3Dto(-0.4f, 1.1f, 2.2f),
                    RotationLocal = new QuaternionDto(0.11f, 0.22f, 0.33f, 0.88f),
                    Confidence = 0.71f
                },
                new JointFrameDto
                {
                    Name = "WristRight",
                    PositionLocal = new Vector3Dto(0.6f, 1.3f, 2.4f),
                    RotationLocal = new QuaternionDto(-0.12f, -0.23f, 0.34f, 0.89f),
                    Confidence = 0.82f
                },
                new JointFrameDto
                {
                    Name = "Head",
                    PositionLocal = new Vector3Dto(0.1f, 1.8f, 2.4f),
                    RotationLocal = QuaternionDto.Identity,
                    Confidence = 0.93f
                }
            ]
        };

        StationSkeletonFrame actual = SkeletonFrameTransforms.MirrorPerformerFacingCamera(source);

        Assert.Equal(-0.25f, actual.PelvisLocal.X);
        Assert.Equal(source.PelvisLocal.Y, actual.PelvisLocal.Y);
        Assert.Equal(source.PelvisLocal.Z, actual.PelvisLocal.Z);
        Assert.Equal(source.BodyRotation.X, actual.BodyRotation.X);
        Assert.Equal(-source.BodyRotation.Y, actual.BodyRotation.Y);
        Assert.Equal(-source.BodyRotation.Z, actual.BodyRotation.Z);
        Assert.Equal(source.BodyRotation.W, actual.BodyRotation.W);

        var leftSourceAsRight = Assert.Single(actual.Joints, joint => joint.Name == "WristRight");
        Assert.Equal(0.4f, leftSourceAsRight.PositionLocal.X);
        Assert.Equal(1.1f, leftSourceAsRight.PositionLocal.Y);
        Assert.Equal(2.2f, leftSourceAsRight.PositionLocal.Z);
        Assert.Equal(0.11f, leftSourceAsRight.RotationLocal.X);
        Assert.Equal(-0.22f, leftSourceAsRight.RotationLocal.Y);
        Assert.Equal(-0.33f, leftSourceAsRight.RotationLocal.Z);
        Assert.Equal(0.88f, leftSourceAsRight.RotationLocal.W);
        Assert.Equal(0.71f, leftSourceAsRight.Confidence);

        var rightSourceAsLeft = Assert.Single(actual.Joints, joint => joint.Name == "WristLeft");
        Assert.Equal(-0.6f, rightSourceAsLeft.PositionLocal.X);
        Assert.Equal(1.3f, rightSourceAsLeft.PositionLocal.Y);
        Assert.Equal(2.4f, rightSourceAsLeft.PositionLocal.Z);
        Assert.Equal(-0.12f, rightSourceAsLeft.RotationLocal.X);
        Assert.Equal(0.23f, rightSourceAsLeft.RotationLocal.Y);
        Assert.Equal(-0.34f, rightSourceAsLeft.RotationLocal.Z);
        Assert.Equal(0.89f, rightSourceAsLeft.RotationLocal.W);
        Assert.Equal(0.82f, rightSourceAsLeft.Confidence);

        var head = Assert.Single(actual.Joints, joint => joint.Name == "Head");
        Assert.Equal(-0.1f, head.PositionLocal.X);
        Assert.Equal(0.93f, head.Confidence);

        // The Unity-space anchor must pass through the mirror untouched.
        Assert.Equal(-3.0f, actual.AnchorPosition.X);
        Assert.Equal(0.5f, actual.AnchorPosition.Z);
        Assert.Equal(45.0f, actual.AnchorRotationYDegrees);
    }
}
