using MessagePack;

namespace DSCC.Protocol;

[MessagePackObject]
public readonly struct QuaternionDto
{
    public static QuaternionDto Identity { get; } = new(0.0f, 0.0f, 0.0f, 1.0f);

    [SerializationConstructor]
    public QuaternionDto(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    [Key(0)]
    public float X { get; init; }

    [Key(1)]
    public float Y { get; init; }

    [Key(2)]
    public float Z { get; init; }

    [Key(3)]
    public float W { get; init; }
}
