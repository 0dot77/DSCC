using MessagePack;

namespace DSCC.Protocol;

[MessagePackObject]
public readonly struct Vector3Dto
{
    public static Vector3Dto Zero { get; } = new(0.0f, 0.0f, 0.0f);

    [SerializationConstructor]
    public Vector3Dto(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [Key(0)]
    public float X { get; init; }

    [Key(1)]
    public float Y { get; init; }

    [Key(2)]
    public float Z { get; init; }
}
