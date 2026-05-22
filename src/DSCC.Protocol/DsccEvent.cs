using MessagePack;

namespace DSCC.Protocol;

[MessagePackObject]
public sealed class DsccEvent
{
    [Key(0)]
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentProtocolVersion;

    [Key(1)]
    public string EventType { get; set; } = string.Empty;

    [Key(2)]
    public int? StationId { get; set; }

    [Key(3)]
    public long TimestampUsec { get; set; }

    [Key(4)]
    public Dictionary<string, string> Properties { get; set; } = new();
}
