using KnowledgeHub.Server.Telemetry;
using Serilog.Events;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>SPEC-20260925-runtime-log-level: switch + auto-reset semantics.</summary>
public sealed class LogLevelControlTests
{
    [Fact]
    public void Set_RaisesLevel_SchedulesAutoReset()
    {
        using var ctl = new LogLevelControl();
        var (level, resetAt) = ctl.Set(LogEventLevel.Debug, 15);
        Assert.Equal("Debug", level);
        Assert.NotNull(resetAt);
        Assert.Equal(LogEventLevel.Debug, ctl.Switch.MinimumLevel);
    }

    [Fact]
    public void Set_ZeroMinutes_OrDefault_RestoresImmediately()
    {
        using var ctl = new LogLevelControl();
        ctl.Set(LogEventLevel.Debug, 15);
        var (level, resetAt) = ctl.Set(LogEventLevel.Information, 0);
        Assert.Equal("Information", level);
        Assert.Null(resetAt);
    }

    [Fact]
    public void Set_SecondRaise_ReplacesTimer()
    {
        using var ctl = new LogLevelControl();
        ctl.Set(LogEventLevel.Debug, 30);
        var (_, resetAt) = ctl.Set(LogEventLevel.Verbose, 10);
        Assert.Equal(LogEventLevel.Verbose, ctl.Switch.MinimumLevel);
        Assert.NotNull(resetAt);
        Assert.True(resetAt - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(11));
    }
}
