using System;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Persistence;

/// <summary>
/// F4 修复验证：TokenUsageTracker 在多线程 Record（对话生成、礼物邮件、记忆压缩等
/// 后台任务并发记账）与并发读取（控制台汇总/导出构建）下不丢计数、不抛
/// "Collection was modified"。修复前该类零同步，本组测试在无锁实现下会崩或计数错乱。
/// </summary>
[Collection("LlmLayer")]
public sealed class TokenUsageTrackerConcurrencyTests
{
    private static TokenUsage OneUsage()
    {
        return new TokenUsage
        {
            PromptTokens = 1,
            CompletionTokens = 1,
            TotalTokens = 2
        };
    }

    [Fact]
    public async Task Parallel_Records_Are_All_Counted()
    {
        var tracker = new TokenUsageTracker();
        const int writers = 8;
        const int recordsPerWriter = 250;

        var tasks = new Task[writers];
        for (int i = 0; i < writers; i++)
        {
            int writerIndex = i;
            tasks[i] = Task.Run(() =>
            {
                for (int n = 0; n < recordsPerWriter; n++)
                {
                    tracker.Record($"Npc{writerIndex}", OneUsage(), "prov", "model", "success");
                }
            });
        }

        await Task.WhenAll(tasks);

        // 世界未载入（单测环境）时只记会话账本；总计 = 2 token × 8 × 250。
        int expectedTotal = 2 * writers * recordsPerWriter;
        int expectedPrompt = writers * recordsPerWriter;
        string summary = tracker.BuildConsoleSummary();
        Assert.Contains($"Session: {expectedTotal} total ({expectedPrompt} prompt", summary);
    }

    [Fact]
    public async Task Concurrent_Readers_And_Writers_Do_Not_Throw()
    {
        var tracker = new TokenUsageTracker();
        var stop = false;

        Task reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                _ = tracker.BuildConsoleSummary();
                _ = tracker.SaveLedgerForTests;
            }
        });

        var writers = new Task[4];
        for (int i = 0; i < writers.Length; i++)
        {
            writers[i] = Task.Run(() =>
            {
                for (int n = 0; n < 500; n++)
                {
                    tracker.Record("Abigail", OneUsage(), "prov", "model", "success");
                }
            });
        }

        await Task.WhenAll(writers);
        Volatile.Write(ref stop, true);
        await reader;

        Assert.Contains($"Session: {2 * 4 * 500} total", tracker.BuildConsoleSummary());
    }

    [Fact]
    public void Import_And_Reset_Do_Not_Deadlock_With_Summary()
    {
        var tracker = new TokenUsageTracker();
        tracker.Record("Abigail", OneUsage(), "prov", "model", "success");
        tracker.ImportMigratedLedger(new TokenUsageLedger());
        _ = tracker.BuildConsoleSummary();
        _ = tracker.SaveLedgerForTests;
    }
}
