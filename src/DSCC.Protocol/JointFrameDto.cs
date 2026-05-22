using MessagePack;

namespace DSCC.Protocol;

[MessagePackObject]
public sealed class JointFrameDto
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public Vector3Dto PositionLocal { get; set; } = Vector3Dto.Zero;

    [Key(2)]
    public QuaternionDto RotationLocal { get; set; } = QuaternionDto.Identity;

    [Key(3)]
    public float Confidence { get; set; }
}
