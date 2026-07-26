using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace LivingNPCs.Dialogue.Diagnostics;

/// <summary>
/// Appends Markdown diagnostics while marking the first write to each file in this mod process.
/// The marker makes logs from separate game launches distinguishable without rotating or
/// truncating the existing files.
/// </summary>
internal static class DiagnosticMarkdownLogWriter
{
    private const string ModUniqueId = "Yuki.LivingNPCs";
    private static DiagnosticLogSession currentSession = new(
        CaptureRunInfo(),
        static () => DateTimeOffset.Now);

    public static void BeginSession(string? modVersion)
    {
        Interlocked.Exchange(
            ref currentSession,
            new DiagnosticLogSession(
                CaptureRunInfo(modVersion),
                static () => DateTimeOffset.Now));
    }

    public static void Append(string filePath, Func<DateTimeOffset, string> buildEntry)
    {
        Volatile.Read(ref currentSession).Append(filePath, buildEntry);
    }

    public static string FormatWallClockTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
    }

    public static string GetSafeFileName(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder((value ?? string.Empty).Length);
        foreach (char ch in value ?? string.Empty)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        return builder.Length == 0 ? "unknown-npc" : builder.ToString();
    }

    /// <summary>
    /// Append a fenced code block whose fence is always longer than any tilde run inside the
    /// content, so LLM output containing "~~~" lines cannot close the fence early and corrupt the
    /// log structure. Shared by every diagnostics exporter.
    /// </summary>
    public static void AppendFencedBlock(StringBuilder builder, string? text, string language)
    {
        string content = string.IsNullOrWhiteSpace(text) ? "<empty>" : text.TrimEnd();
        string fence = new string('~', Math.Max(3, LongestTildeRun(content) + 1));
        builder.Append(fence).AppendLine(language);
        builder.AppendLine(content);
        builder.AppendLine(fence);
    }

    /// <summary>Length of the longest run of consecutive '~' characters anywhere in the text.</summary>
    internal static int LongestTildeRun(string text)
    {
        int longest = 0;
        int current = 0;
        foreach (char character in text)
        {
            current = character == '~' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private static DiagnosticRunInfo CaptureRunInfo(string? explicitModVersion = null)
    {
        Assembly assembly = typeof(DiagnosticMarkdownLogWriter).Assembly;
        string assemblyVersion = FormatAssemblyVersion(assembly.GetName().Version);
        string modVersion = string.IsNullOrWhiteSpace(explicitModVersion)
            ? assemblyVersion
            : explicitModVersion.Trim();

        try
        {
            if (string.IsNullOrWhiteSpace(explicitModVersion))
            {
                string? registeredVersion = DialogueServices.Helper?
                    .ModRegistry
                    .Get(ModUniqueId)?
                    .Manifest
                    .Version
                    .ToString();
                if (!string.IsNullOrWhiteSpace(registeredVersion))
                {
                    modVersion = registeredVersion;
                }
            }
        }
        catch
        {
            // Assembly metadata remains available in tests and very early startup paths.
        }

        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?.Trim() ?? string.Empty;
        string moduleVersionId = assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture);
        string buildIdentifier = string.IsNullOrWhiteSpace(informationalVersion)
            ? $"MVID {moduleVersionId}"
            : $"{informationalVersion}; MVID {moduleVersionId}";

        return new DiagnosticRunInfo(
            DateTimeOffset.Now,
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            modVersion,
            buildIdentifier);
    }

    private static string FormatAssemblyVersion(Version? version)
    {
        if (version == null)
        {
            return "unknown";
        }

        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }
}

internal sealed class DiagnosticLogSession
{
    /// <summary>单个诊断日志文件的大小上限；超限时把现文件轮转为 .old（只保留一代），当前文件从头开始。
    /// 每条日志因此最多占约两倍上限的磁盘，长档不再无限增长。</summary>
    private const long DefaultMaxLogFileBytes = 8 * 1024 * 1024;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly ConcurrentDictionary<string, object> fileGates = new(PathComparer);
    private readonly ConcurrentDictionary<string, byte> initializedFiles = new(PathComparer);
    private readonly DiagnosticRunInfo runInfo;
    private readonly Func<DateTimeOffset> clock;
    private readonly long maxLogFileBytes;

    public DiagnosticLogSession(DiagnosticRunInfo runInfo, Func<DateTimeOffset> clock, long maxLogFileBytes = DefaultMaxLogFileBytes)
    {
        this.runInfo = runInfo ?? throw new ArgumentNullException(nameof(runInfo));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.maxLogFileBytes = maxLogFileBytes > 0 ? maxLogFileBytes : DefaultMaxLogFileBytes;
    }

    public void Append(string filePath, Func<DateTimeOffset, string> buildEntry)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A diagnostic log path is required.", nameof(filePath));
        }

        ArgumentNullException.ThrowIfNull(buildEntry);

        string fullPath = Path.GetFullPath(filePath);
        DateTimeOffset recordedAt = this.clock();
        string entry = buildEntry(recordedAt) ?? string.Empty;
        object fileGate = this.fileGates.GetOrAdd(fullPath, static _ => new object());
        lock (fileGate)
        {
            bool isFirstWriteThisRun = !this.initializedFiles.ContainsKey(fullPath);
            bool hasExistingContent = File.Exists(fullPath) && new FileInfo(fullPath).Length > 0;

            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (hasExistingContent && this.TryRotateOversizedFile(fullPath, entry.Length))
            {
                hasExistingContent = false;
                isFirstWriteThisRun = true;
                this.initializedFiles.TryRemove(fullPath, out _);
            }

            string payload = isFirstWriteThisRun
                ? this.BuildSessionSeparator(hasExistingContent) + entry
                : entry;
            File.AppendAllText(fullPath, payload, Encoding.UTF8);

            // Mark the path only after a successful write, so a transient I/O failure cannot
            // cause the next successful append to lose its restart separator.
            this.initializedFiles.TryAdd(fullPath, 0);
        }
    }

    /// <summary>超限即把现文件搬成 <c>*.old.md</c>（覆盖上一代）；搬运失败时放弃本次轮转、照常追加（不丢日志）。</summary>
    private bool TryRotateOversizedFile(string fullPath, int incomingChars)
    {
        try
        {
            long length = new FileInfo(fullPath).Length;
            if (length + incomingChars <= this.maxLogFileBytes)
            {
                return false;
            }

            string rotatedPath = fullPath + ".old";
            if (File.Exists(rotatedPath))
            {
                File.Delete(rotatedPath);
            }

            File.Move(fullPath, rotatedPath);
            return true;
        }
        catch
        {
            // 文件被占用等瞬时失败：这次继续写原文件，下次再试轮转。
            return false;
        }
    }

    internal string BuildSessionSeparator(bool prependBlankLine)
    {
        var builder = new StringBuilder();
        if (prependBlankLine)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.AppendLine($"<!-- livingnpcs-diagnostic-session:{Clean(this.runInfo.SessionId)} -->");
        builder.AppendLine("# LivingNPCs diagnostic session / restart");
        builder.AppendLine();
        builder.AppendLine($"- Session started (wall clock): `{DiagnosticMarkdownLogWriter.FormatWallClockTimestamp(this.runInfo.StartedAt)}`");
        builder.AppendLine($"- Session ID: `{Clean(this.runInfo.SessionId)}`");
        builder.AppendLine($"- Mod version: `{Clean(this.runInfo.ModVersion)}`");
        builder.AppendLine($"- Build identifier: `{Clean(this.runInfo.BuildIdentifier)}`");
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        return builder.ToString();
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Replace('`', '\'').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

internal sealed record DiagnosticRunInfo(
    DateTimeOffset StartedAt,
    string SessionId,
    string ModVersion,
    string BuildIdentifier);
