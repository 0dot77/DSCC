namespace DSCC.Protocol;

public static class ProtocolConstants
{
    public const int CurrentProtocolVersion = 1;

    public const int DefaultSkeletonPort = 55010;
    public const int DefaultEventPort = 55011;
    public const int DefaultStatusPort = 55012;

    public const string GameStartEvent = "game/start";
    public const string GameResetEvent = "game/reset";
    public const string PlayerEnterEvent = "player/enter";
    public const string PlayerExitEvent = "player/exit";
    public const string CalibrationReloadEvent = "calibration/reload";
}
