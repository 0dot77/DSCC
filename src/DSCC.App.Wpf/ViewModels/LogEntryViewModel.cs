namespace DSCC.App.Wpf.ViewModels;

public sealed record LogEntryViewModel(DateTimeOffset Timestamp, string Level, string Message)
{
    public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
}
