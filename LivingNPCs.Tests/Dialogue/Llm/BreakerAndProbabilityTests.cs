using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue.Llm;

namespace LivingNPCs.Tests.Dialogue.Llm;

/// <summary>纯逻辑，无全局状态，不需要挂 LlmLayer collection。</summary>
public sealed class LlmCircuitBreakerTests
{
    [Fact]
    public void TwoConsecutiveAuthFailuresTripUntilReset()
    {
        var breaker = new LlmCircuitBreaker();

        Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(success: false, 401));
        Assert.Equal(LlmSuspensionKind.None, breaker.CurrentSuspension);
        Assert.Equal(LlmSuspensionKind.Auth, breaker.RecordOutcome(success: false, 403));
        Assert.Equal(LlmSuspensionKind.Auth, breaker.CurrentSuspension);

        // 密钥不会自愈：时间流逝不恢复，仅配置变更（Reset）复位。
        breaker.Reset();
        Assert.Equal(LlmSuspensionKind.None, breaker.CurrentSuspension);
    }

    [Fact]
    public void AuthSuspensionIgnoresElapsedTime()
    {
        DateTime now = DateTime.UtcNow;
        var breaker = new LlmCircuitBreaker(() => now);
        breaker.RecordOutcome(false, 401);
        breaker.RecordOutcome(false, 401);
        Assert.Equal(LlmSuspensionKind.Auth, breaker.CurrentSuspension);

        now += TimeSpan.FromHours(6);
        Assert.Equal(LlmSuspensionKind.Auth, breaker.CurrentSuspension);
    }

    [Fact]
    public void FiveRateLimitFailuresCoolDownForTenMinutes()
    {
        DateTime now = DateTime.UtcNow;
        var breaker = new LlmCircuitBreaker(() => now);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(false, 429));
        }

        Assert.Equal(LlmSuspensionKind.RateLimit, breaker.RecordOutcome(false, 429));
        Assert.Equal(LlmSuspensionKind.RateLimit, breaker.CurrentSuspension);

        now += TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1);
        Assert.Equal(LlmSuspensionKind.RateLimit, breaker.CurrentSuspension);

        now += TimeSpan.FromSeconds(2);
        Assert.Equal(LlmSuspensionKind.None, breaker.CurrentSuspension);

        // 自动恢复后计数清零：需再连续 5 次才会再次熔断（并再次通知）。
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(false, 429));
        }

        Assert.Equal(LlmSuspensionKind.RateLimit, breaker.RecordOutcome(false, 429));
    }

    [Fact]
    public void AnySuccessClearsBothCounters()
    {
        var breaker = new LlmCircuitBreaker();
        breaker.RecordOutcome(false, 401);
        breaker.RecordOutcome(true, 200);
        Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(false, 401));
        Assert.Equal(LlmSuspensionKind.Auth, breaker.RecordOutcome(false, 401));
    }

    [Fact]
    public void NonFatalErrorBreaksTheStreak()
    {
        var breaker = new LlmCircuitBreaker();
        breaker.RecordOutcome(false, 401);
        breaker.RecordOutcome(false, 500);
        Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(false, 401));
    }

    [Fact]
    public void DifferentFatalClassesResetEachOther()
    {
        var breaker = new LlmCircuitBreaker();
        breaker.RecordOutcome(false, 401);
        breaker.RecordOutcome(false, 429);
        Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(false, 401));
        Assert.Equal(LlmSuspensionKind.Auth, breaker.RecordOutcome(false, 403));
    }
}

/// <summary>纯逻辑，无全局状态。</summary>
public sealed class TokenProbabilityMathTests
{
    [Fact]
    public void SinglePositionTokensLandInMatchingBuckets()
    {
        var positions = new[]
        {
            (IReadOnlyDictionary<string, double>)new Dictionary<string, double> { ["Yes"] = 0.7, ["No"] = 0.2, ["Eh"] = 0.1 }
        };

        IReadOnlyDictionary<string, double> buckets = TokenProbabilityMath.AggregateOptions(positions, new[] { "Yes", "No" });

        Assert.Equal(0.7, buckets["Yes"], 5);
        Assert.Equal(0.2, buckets["No"], 5);
    }

    [Fact]
    public void MultiTokenOptionsMultiplyAlongThePath()
    {
        var positions = new IReadOnlyDictionary<string, double>[]
        {
            new Dictionary<string, double> { ["App"] = 0.5, ["B"] = 0.5 },
            new Dictionary<string, double> { ["le"] = 0.8, ["x"] = 0.2 }
        };

        IReadOnlyDictionary<string, double> buckets = TokenProbabilityMath.AggregateOptions(positions, new[] { "Apple" });

        Assert.Equal(0.4, buckets["Apple"], 5);
    }

    [Fact]
    public void LeadingWhitespaceAndCaseAreIgnored()
    {
        var positions = new IReadOnlyDictionary<string, double>[]
        {
            new Dictionary<string, double> { [" Y"] = 0.6, ["N"] = 0.4 },
            new Dictionary<string, double> { ["ES"] = 1.0 }
        };

        IReadOnlyDictionary<string, double> buckets = TokenProbabilityMath.AggregateOptions(positions, new[] { "yes", "no" });

        Assert.Equal(0.6, buckets["yes"], 5);
        Assert.Equal(0.0, buckets["no"], 5);
    }

    [Fact]
    public void TokenOvershootStillCountsForTheCoveredOption()
    {
        var positions = new IReadOnlyDictionary<string, double>[]
        {
            new Dictionary<string, double> { ["Yes."] = 0.9 }
        };

        IReadOnlyDictionary<string, double> buckets = TokenProbabilityMath.AggregateOptions(positions, new[] { "Yes" });

        Assert.Equal(0.9, buckets["Yes"], 5);
    }

    [Fact]
    public void MismatchedBranchesArePruned()
    {
        var positions = new IReadOnlyDictionary<string, double>[]
        {
            new Dictionary<string, double> { ["Ba"] = 1.0 },
            new Dictionary<string, double> { ["nana"] = 1.0 }
        };

        IReadOnlyDictionary<string, double> buckets = TokenProbabilityMath.AggregateOptions(positions, new[] { "Apple" });

        Assert.Equal(0.0, buckets["Apple"], 5);
    }
}
