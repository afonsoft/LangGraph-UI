using Serilog.Core;
using Serilog.Events;

namespace KnowledgeHub.Server.Telemetry;

/// <summary>
/// SPEC-20260925-runtime-log-level: singleton wrapper over
/// <see cref="LoggingLevelSwitch"/> with an auto-reset timer — a temporary
/// Debug raise can't be forgotten and fill the disk.
/// </summary>
public sealed class LogLevelControl : IDisposable
{
    private readonly LoggingLevelSwitch _switch = new();
    private readonly Lock _gate = new();
    private Timer? _resetTimer;
    private DateTimeOffset? _autoResetAt;

    public LoggingLevelSwitch Switch => _switch;

    public LogEventLevel ConfiguredDefault { get; } = LogEventLevel.Information;

    public (string Level, DateTimeOffset? AutoResetAt) Current()
    {
        lock (_gate)
            return (_switch.MinimumLevel.ToString(), _autoResetAt);
    }

    /// <summary>Raises (or restores) the level; <paramref name="minutes"/> ≤0
    /// restores the configured default immediately.</summary>
    public (string Level, DateTimeOffset? AutoResetAt) Set(LogEventLevel level, int minutes)
    {
        lock (_gate)
        {
            _resetTimer?.Dispose();
            _resetTimer = null;
            _autoResetAt = null;

            _switch.MinimumLevel = level;

            if (level != ConfiguredDefault && minutes > 0)
            {
                _autoResetAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
                _resetTimer = new Timer(_ =>
                {
                    lock (_gate)
                    {
                        _switch.MinimumLevel = ConfiguredDefault;
                        _autoResetAt = null;
                    }
                }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
            }
            return (_switch.MinimumLevel.ToString(), _autoResetAt);
        }
    }

    public void Dispose() => _resetTimer?.Dispose();
}
