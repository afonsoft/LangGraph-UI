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

    private int _generation;

    /// <summary>Raises (or restores) the level; <paramref name="minutes"/> ≤0
    /// restores the configured default immediately — a raised level with no
    /// timer would otherwise stay forever.</summary>
    public (string Level, DateTimeOffset? AutoResetAt) Set(LogEventLevel level, int minutes)
    {
        lock (_gate)
        {
            _resetTimer?.Dispose();
            _resetTimer = null;
            _autoResetAt = null;

            var effective = minutes <= 0 ? ConfiguredDefault : level;
            _switch.MinimumLevel = effective;

            if (effective != ConfiguredDefault)
            {
                var gen = ++_generation;
                _autoResetAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
                _resetTimer = new Timer(_ =>
                {
                    lock (_gate)
                    {
                        // SPEC-20260926-ops-and-ui-polish: a callback already
                        // queued when the timer was disposed must not clobber a
                        // NEWER session's level — generation guards it.
                        if (gen != _generation)
                            return;
                        _switch.MinimumLevel = ConfiguredDefault;
                        _autoResetAt = null;
                    }
                }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
            }
            else
            {
                // Restoring bumps the generation too — any still-queued timer
                // from a prior session becomes a no-op.
                ++_generation;
            }
            return (_switch.MinimumLevel.ToString(), _autoResetAt);
        }
    }

    public void Dispose() => _resetTimer?.Dispose();
}
