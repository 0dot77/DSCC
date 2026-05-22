using MessagePack;

namespace DSCC.Protocol;

[MessagePackObject]
public sealed class StationSkeletonFrame
{
    [Key(0)]
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentProtocolVersion;

    [Key(1)]
    public int StationId { get; set; }

    [Key(2)]
    public string CameraSerial { get; set; } = string.Empty;

    [Key(3)]
    public string DeviceType { get; set; } = string.Empty;

    [Key(4)]
    public long TimestampUsec { get; set; }

    [Key(5)]
    public bool HasPlayer { get; set; }

    [Key(6)]
    public StationStateDto State { get; set; }

    [Key(7)]
    public float Confidence { get; set; }

    [Key(8)]
    public bool IsInsideFootMarker { get; set; }

    [Key(9)]
    public bool IsInsideTrackingRoi { get; set; }

    [Key(10)]
    public float TrackingLostSeconds { get; set; }

    [Key(11)]
    public Vector3Dto PelvisLocal { get; set; } = Vector3Dto.Zero;

    [Key(12)]
    public QuaternionDto BodyRotation { get; set; } = QuaternionDto.Identity;

    [Key(13)]
    public JointFrameDto[] Joints { get; set; } = Array.Empty<JointFrameDto>();
}
