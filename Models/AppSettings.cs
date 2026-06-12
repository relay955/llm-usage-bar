namespace LLMUsageBar.Models;

public sealed class AppSettings {
    public const int DefaultRefreshIntervalMinutes = 10;

    public int RefreshIntervalMinutes { get; set; } = DefaultRefreshIntervalMinutes;
}
