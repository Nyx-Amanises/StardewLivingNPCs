using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Diagnostics;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Diagnostics;

public sealed class DiagnosticMarkdownLogWriterTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "livingnpcs-diagnostic-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AppendPreservesExistingLogAndWritesOneRestartSeparatorPerFile()
    {
        string filePath = Path.Combine(this.root, "ai_response_logs", "save", "Penny.md");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "older entry");

        DateTimeOffset startedAt = new(2026, 7, 26, 1, 2, 3, 456, TimeSpan.FromHours(8));
        var timestamps = new Queue<DateTimeOffset>(
        [
            new DateTimeOffset(2026, 7, 26, 1, 3, 4, 567, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 7, 26, 1, 4, 5, 678, TimeSpan.FromHours(8))
        ]);
        var session = new DiagnosticLogSession(
            new DiagnosticRunInfo(startedAt, "session-123", "0.2.0", "test-build"),
            () => timestamps.Dequeue());

        session.Append(filePath, timestamp => $"entry `{DiagnosticMarkdownLogWriter.FormatWallClockTimestamp(timestamp)}`{Environment.NewLine}");
        session.Append(filePath, timestamp => $"entry `{DiagnosticMarkdownLogWriter.FormatWallClockTimestamp(timestamp)}`{Environment.NewLine}");

        string log = File.ReadAllText(filePath);
        Assert.StartsWith("older entry", log);
        Assert.Equal(1, CountOccurrences(log, "# LivingNPCs diagnostic session / restart"));
        Assert.Contains("- Session started (wall clock): `2026-07-26T01:02:03.456+08:00`", log);
        Assert.Contains("- Session ID: `session-123`", log);
        Assert.Contains("- Mod version: `0.2.0`", log);
        Assert.Contains("- Build identifier: `test-build`", log);
        Assert.Contains("entry `2026-07-26T01:03:04.567+08:00`", log);
        Assert.Contains("entry `2026-07-26T01:04:05.678+08:00`", log);
    }

    [Fact]
    public void AppendWritesRestartSeparatorIndependentlyForEveryDiagnosticFile()
    {
        DateTimeOffset now = new(2026, 7, 26, 9, 10, 11, TimeSpan.Zero);
        var session = new DiagnosticLogSession(
            new DiagnosticRunInfo(now, "same-run", "0.2.0", "build-id"),
            () => now);
        string first = Path.Combine(this.root, "ai_response_logs", "Penny.md");
        string second = Path.Combine(this.root, "context_routing_logs", "Penny.md");

        session.Append(first, _ => "first entry\n");
        session.Append(second, _ => "second entry\n");

        Assert.Contains("livingnpcs-diagnostic-session:same-run", File.ReadAllText(first));
        Assert.Contains("livingnpcs-diagnostic-session:same-run", File.ReadAllText(second));
    }

    [Fact]
    public async Task DifferentDiagnosticFilesDoNotShareOneGlobalBuildLock()
    {
        DateTimeOffset now = new(2026, 7, 26, 9, 10, 11, TimeSpan.Zero);
        var session = new DiagnosticLogSession(
            new DiagnosticRunInfo(now, "parallel-run", "0.2.0", "build-id"),
            () => now);
        using var bothBuildersEntered = new CountdownEvent(2);
        using var releaseBuilders = new ManualResetEventSlim(false);

        Task first = Task.Run(() => session.Append(
            Path.Combine(this.root, "Penny.md"),
            _ =>
            {
                bothBuildersEntered.Signal();
                releaseBuilders.Wait(TimeSpan.FromSeconds(3));
                return "first\n";
            }));
        Task second = Task.Run(() => session.Append(
            Path.Combine(this.root, "Pam.md"),
            _ =>
            {
                bothBuildersEntered.Signal();
                releaseBuilders.Wait(TimeSpan.FromSeconds(3));
                return "second\n";
            }));

        bool enteredConcurrently = bothBuildersEntered.Wait(TimeSpan.FromSeconds(3));
        releaseBuilders.Set();
        await Task.WhenAll(first, second);

        Assert.True(enteredConcurrently);
    }

    [Theory]
    [InlineData("Penny", "Penny")]
    [InlineData("", "unknown-npc")]
    public void SafeFileNameKeepsOrdinaryNamesAndHandlesEmptyInput(string input, string expected)
    {
        Assert.Equal(expected, DiagnosticMarkdownLogWriter.GetSafeFileName(input));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch
        {
            // A failed temporary-directory cleanup should not hide the assertion result.
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
