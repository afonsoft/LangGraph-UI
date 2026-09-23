using System.Security.Claims;
using KnowledgeHub.Server.Auth;
using KnowledgeHub.Server.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260923-rate-limiting — CallerPartitioner precedence and
/// McpToolRateLimiter acquire semantics.
/// </summary>
public sealed class RateLimitingTests
{
    private static DefaultHttpContext Http(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        if (claims.Length > 0)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return ctx;
    }

    [Fact]
    public void Partitioner_ApiKeyClaim_Wins()
    {
        var keyId = Guid.NewGuid();
        var ctx = Http(
            new Claim(ApiKeyAuthenticationHandler.KeyIdClaim, keyId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var (key, kind, anon) = CallerPartitioner.Resolve(ctx);
        Assert.Equal($"key:{keyId}", key);
        Assert.Equal(CallerPartitioner.Kind.ApiKey, kind);
        Assert.False(anon);
    }

    [Fact]
    public void Partitioner_UserClaim_Second()
    {
        var userId = Guid.NewGuid();
        var ctx = Http(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        var (key, kind, anon) = CallerPartitioner.Resolve(ctx);
        Assert.Equal($"user:{userId}", key);
        Assert.Equal(CallerPartitioner.Kind.User, kind);
        Assert.False(anon);
    }

    [Fact]
    public void Partitioner_NoClaims_AnonymousIp()
    {
        var ctx = Http();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.7");

        var (key, kind, anon) = CallerPartitioner.Resolve(ctx);
        Assert.Equal("anon:10.0.0.7", key);
        Assert.Equal(CallerPartitioner.Kind.Ip, kind);
        Assert.True(anon);
    }

    [Fact]
    public void Partitioner_ForwardedFor_OnlyWhenTrusted()
    {
        var ctx = Http();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4, 5.6.7.8";

        Assert.Equal("anon:10.0.0.1", CallerPartitioner.Resolve(ctx, trustForwardedHeaders: false).Key);
        Assert.Equal("anon:1.2.3.4", CallerPartitioner.Resolve(ctx, trustForwardedHeaders: true).Key);
    }

    [Fact]
    public void McpLimiter_NonLlmTool_Passes()
    {
        using var limiter = new McpToolRateLimiter(
            new RateLimitOptions { LlmPermitLimit = 1 },
            NullLogger<McpToolRateLimiter>.Instance);

        for (var i = 0; i < 10; i++)
            Assert.True(limiter.TryAcquire("read_document", null, out _));
    }

    [Fact]
    public void McpLimiter_LlmTool_ExhaustsThenReportsRetryAfter()
    {
        using var limiter = new McpToolRateLimiter(
            new RateLimitOptions { LlmPermitLimit = 1, LlmWindowSeconds = 60 },
            NullLogger<McpToolRateLimiter>.Instance);

        Assert.True(limiter.TryAcquire("agent_chat", null, out _));
        Assert.False(limiter.TryAcquire("agent_chat", null, out var retry));
        Assert.True(retry > 0);
    }

    [Fact]
    public void McpLimiter_Disabled_AlwaysPasses()
    {
        using var limiter = new McpToolRateLimiter(
            new RateLimitOptions { Enabled = false, LlmPermitLimit = 1 },
            NullLogger<McpToolRateLimiter>.Instance);

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("agent_chat", null, out _));
    }

    [Fact]
    public void McpLimiter_WriteTool_UsesSyncBucket()
    {
        using var limiter = new McpToolRateLimiter(
            new RateLimitOptions { SyncPermitLimit = 1, SyncWindowSeconds = 60 },
            NullLogger<McpToolRateLimiter>.Instance);

        Assert.True(limiter.TryAcquire("write_knowledge", null, out _));
        Assert.False(limiter.TryAcquire("write_knowledge", null, out _));
        // different bucket — llm tool unaffected
        Assert.True(limiter.TryAcquire("search_knowledge", null, out _));
    }
}
