using DSCC.Protocol;
using MessagePack;
using System.Text.Json;

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
            BodyCount = 2,
            SelectedBodyId = 42,
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
        Assert.Equal(source.BodyCount, actual.BodyCount);
        Assert.Equal(source.SelectedBodyId, actual.SelectedBodyId);
    }

    [Fact]
    public void StationSkeletonFrame_SerializesTauriCompatibleArrayPrefix()
    {
        StationSkeletonFrame source = new()
        {
            StationId = 3,
            CameraSerial = "MEGA-003",
            DeviceType = "FemtoMega",
            TimestampUsec = 987654321L,
            HasPlayer = true,
            State = StationStateDto.Active,
            Confidence = 0.92f,
            IsInsideFootMarker = true,
            IsInsideTrackingRoi = true,
            TrackingLostSeconds = 0.0f,
            PelvisLocal = new Vector3Dto(0.1f, 1.0f, 2.0f),
            BodyRotation = new QuaternionDto(0.0f, 0.2f, 0.0f, 0.98f),
            Joints =
            [
                new JointFrameDto
                {
                    Name = "Pelvis",
                    PositionLocal = new Vector3Dto(0.1f, 1.0f, 2.0f),
                    RotationLocal = QuaternionDto.Identity,
                    Confidence = 0.95f
                }
            ],
            AnchorPosition = new Vector3Dto(-4.0f, 0.0f, 0.5f),
            AnchorRotationYDegrees = 15.0f,
            BodyCount = 1,
            SelectedBodyId = 33
        };

        byte[] payload = MessagePackSerializer.Serialize(source);
        using JsonDocument document = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(payload));
        JsonElement fields = document.RootElement;

        Assert.Equal(JsonValueKind.Array, fields.ValueKind);
        Assert.Equal(18, fields.GetArrayLength());

        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, fields[0].GetInt32());
        Assert.Equal(3, fields[1].GetInt32());
        Assert.Equal("MEGA-003", fields[2].GetString());
        Assert.Equal("FemtoMega", fields[3].GetString());
        Assert.Equal(987654321L, fields[4].GetInt64());
        Assert.True(fields[5].GetBoolean());
        Assert.Equal((int)StationStateDto.Active, fields[6].GetInt32());
        Assert.Equal(0.92f, fields[7].GetSingle());
        Assert.True(fields[8].GetBoolean());
        Assert.True(fields[9].GetBoolean());
        Assert.Equal(0.0f, fields[10].GetSingle());

        Assert.Equal(0.1f, fields[11][0].GetSingle());
        Assert.Equal(1.0f, fields[11][1].GetSingle());
        Assert.Equal(2.0f, fields[11][2].GetSingle());
        Assert.Equal(0.0f, fields[12][0].GetSingle());
        Assert.Equal(0.2f, fields[12][1].GetSingle());
        Assert.Equal(0.0f, fields[12][2].GetSingle());
        Assert.Equal(0.98f, fields[12][3].GetSingle());

        Assert.Equal("Pelvis", fields[13][0][0].GetString());
        Assert.Equal(0.1f, fields[13][0][1][0].GetSingle());
        Assert.Equal(1.0f, fields[13][0][1][1].GetSingle());
        Assert.Equal(2.0f, fields[13][0][1][2].GetSingle());
        Assert.Equal(0.95f, fields[13][0][3].GetSingle());

        Assert.Equal(-4.0f, fields[14][0].GetSingle());
        Assert.Equal(0.0f, fields[14][1].GetSingle());
        Assert.Equal(0.5f, fields[14][2].GetSingle());
        Assert.Equal(15.0f, fields[15].GetSingle());

        // The Tauri app reads only indices 0..15. New diagnostic metadata must
        // stay appended after that prefix so older app builds can ignore it.
        Assert.Equal(1, fields[16].GetInt32());
        Assert.Equal(33L, fields[17].GetInt64());
    }

    [Fact]
    public void StationSkeletonFrame_DeserializesLegacyPayloadWithoutBodyMetadata()
    {
        object[] legacyFields =
        [
            ProtocolConstants.CurrentProtocolVersion,
            7,
            "LEGACY-MEGA",
            "FemtoMega",
            123456789L,
            true,
            (int)StationStateDto.Active,
            0.91f,
            true,
            true,
            0.0f,
            new object[] { 0.1f, 1.2f, 2.3f },
            new object[] { 0.0f, 0.0f, 0.0f, 1.0f },
            new object[]
            {
                new object[]
                {
                    "Pelvis",
                    new object[] { 0.1f, 1.2f, 2.3f },
                    new object[] { 0.0f, 0.0f, 0.0f, 1.0f },
                    0.95f
                }
            },
            new object[] { -3.0f, 0.0f, 0.5f },
            45.0f
        ];

        byte[] payload = MessagePackSerializer.Serialize(legacyFields);
        StationSkeletonFrame actual = MessagePackSerializer.Deserialize<StationSkeletonFrame>(payload);

        Assert.Equal(7, actual.StationId);
        Assert.Equal("LEGACY-MEGA", actual.CameraSerial);
        Assert.True(actual.HasPlayer);
        Assert.Equal(StationStateDto.Active, actual.State);
        Assert.Single(actual.Joints);
        Assert.Equal("Pelvis", actual.Joints[0].Name);
        Assert.Equal(0.5f, actual.AnchorPosition.Z);
        Assert.Equal(45.0f, actual.AnchorRotationYDegrees);
        Assert.Equal(0, actual.BodyCount);
        Assert.Equal(-1, actual.SelectedBodyId);
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
            BodyCount = 2,
            SelectedBodyId = 77,
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
        Assert.Equal(source.BodyCount, actual.BodyCount);
        Assert.Equal(source.SelectedBodyId, actual.SelectedBodyId);
    }
}
