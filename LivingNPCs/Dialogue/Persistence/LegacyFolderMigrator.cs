using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 旧 mod 文件夹的数据迁移（WP14 §4.3，GameLaunched 执行，不依赖存档；farmhand 机器同样执行）。
/// 定位规则：从 Mods 根扫描第一与第二层子目录的 manifest.json，找 UniqueID == dandm1.ValleyTalk
/// （旧部署是嵌套结构 Mods\ValleyTalk\ValleyTalk\；内容包 ID 不匹配、不处理）。
/// 搬运：multiplayer/*.json（farmhand 历史）与 conversation_logs/（玩家可读回忆录，归档段含已修剪对话）；
/// 其余诊断日志目录不搬（裁决 3）。config.json 读出后交 WP15 注册的映射器（幂等由 WP15 的
/// LegacyConfigImported 标志决定，裁决 5）。文件搬运幂等靠 data/migration-state.json 标记。
/// 绝不删除旧文件夹内任何文件（§4.1）。
/// </summary>
internal sealed class LegacyFolderMigrator
{
    public const string LegacyUniqueId = "dandm1.ValleyTalk";
    public const string MarkerRelativePath = "data/migration-state.json";
    private const string MultiplayerFolderName = "multiplayer";
    private const string ConversationLogsFolderName = "conversation_logs";

    /// <summary>WP15-TODO：配置映射器由 WP15 注册（读旧字段 → 合入新 config → 落 LegacyConfigImported）。
    /// 参数为 null 表示没找到旧 config（WP15 仍应置标志，裁决 5：无论是否找到均置 true）。</summary>
    internal static Action<LegacyValleyTalkConfig?>? ConfigImporter;

    private readonly string modDirectoryPath;

    public LegacyFolderMigrator(string modDirectoryPath)
    {
        this.modDirectoryPath = modDirectoryPath;
    }

    public sealed class Result
    {
        public bool Ran { get; set; }
        public string? LegacyFolderPath { get; set; }
        public int MultiplayerFilesCopied { get; set; }
        public int ConversationLogFilesCopied { get; set; }
        public bool LegacyConfigFound { get; set; }
        public List<string> Errors { get; } = new();
    }

    public Result RunOnGameLaunched(bool legacyModLoaded)
    {
        var result = new Result();
        if (legacyModLoaded)
        {
            // 旧 mod 仍在加载：跳过全部迁移（§4.1），等用户删除旧文件夹后的下一次启动再做。
            return result;
        }

        string? legacyFolder = this.FindLegacyFolder();
        result.LegacyFolderPath = legacyFolder;

        // 配置导入每次启动都尝试移交（找没找到都通知），幂等由 WP15 的 LegacyConfigImported 决定。
        LegacyValleyTalkConfig? legacyConfig = null;
        if (legacyFolder != null)
        {
            legacyConfig = this.TryReadLegacyConfig(legacyFolder, result);
        }

        result.LegacyConfigFound = legacyConfig != null;
        try
        {
            ConfigImporter?.Invoke(legacyConfig);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"config import: {ex.Message}");
        }

        if (legacyFolder == null)
        {
            // 静默结束（全新用户）。不写标记：将来用户从备份恢复旧文件夹时仍可拾起。
            return result;
        }

        if (this.HasMarker())
        {
            return result;
        }

        result.Ran = true;
        this.CopyDirectory(
            Path.Combine(legacyFolder, MultiplayerFolderName),
            Path.Combine(this.modDirectoryPath, MultiplayerFolderName),
            "*.json",
            recursive: false,
            copied => result.MultiplayerFilesCopied = copied,
            result.Errors);
        this.CopyDirectory(
            Path.Combine(legacyFolder, ConversationLogsFolderName),
            Path.Combine(this.modDirectoryPath, ConversationLogsFolderName),
            "*",
            recursive: true,
            copied => result.ConversationLogFilesCopied = copied,
            result.Errors);

        this.WriteMarker(result);
        this.Report(result);
        return result;
    }

    /// <summary>扫描 Mods 根的第一与第二层子目录，认 manifest 的 UniqueID（大小写不敏感）。</summary>
    public string? FindLegacyFolder()
    {
        string? modsRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(this.modDirectoryPath));
        if (modsRoot == null || !Directory.Exists(modsRoot))
        {
            return null;
        }

        foreach (string level1 in SafeSubdirectories(modsRoot))
        {
            if (PathsEqual(level1, this.modDirectoryPath))
            {
                continue;
            }

            if (ManifestMatches(level1))
            {
                return level1;
            }

            foreach (string level2 in SafeSubdirectories(level1))
            {
                if (!PathsEqual(level2, this.modDirectoryPath) && ManifestMatches(level2))
                {
                    return level2;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> SafeSubdirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ManifestMatches(string directory)
    {
        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            string text = File.ReadAllText(manifestPath);
            try
            {
                string? uniqueId = JObject.Parse(text).Value<string>("UniqueID");
                return string.Equals(uniqueId, LegacyUniqueId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // SMAPI 的 manifest 解析比标准 JSON 宽松（注释/尾逗号）；解析失败时退回精确正则。
                return Regex.IsMatch(
                    text,
                    "\"UniqueID\"\\s*:\\s*\"dandm1\\.ValleyTalk\"",
                    RegexOptions.IgnoreCase);
            }
        }
        catch
        {
            return false;
        }
    }

    private LegacyValleyTalkConfig? TryReadLegacyConfig(string legacyFolder, Result result)
    {
        // 同目录的 config.json.bak-<时间戳> 备份忽略（§3.2.2），只认 config.json。
        string configPath = Path.Combine(legacyFolder, "config.json");
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            var config = LegacyValleyTalkConfig.TryParse(File.ReadAllText(configPath));
            if (config == null)
            {
                result.Errors.Add("config.json: unparseable");
            }

            return config;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"config.json: {ex.Message}");
            return null;
        }
    }

    private void CopyDirectory(string sourceDir, string targetDir, string pattern, bool recursive, Action<int> setCopied, List<string> errors)
    {
        int copied = 0;
        try
        {
            if (!Directory.Exists(sourceDir))
            {
                setCopied(0);
                return;
            }

            foreach (string sourceFile in Directory.EnumerateFiles(sourceDir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                string relative = Path.GetRelativePath(sourceDir, sourceFile);
                string targetFile = Path.Combine(targetDir, relative);
                try
                {
                    if (File.Exists(targetFile))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    File.Copy(sourceFile, targetFile, overwrite: false);
                    copied++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{relative}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"{sourceDir}: {ex.Message}");
        }

        setCopied(copied);
    }

    private bool HasMarker()
    {
        return File.Exists(Path.Combine(this.modDirectoryPath, MarkerRelativePath));
    }

    private void WriteMarker(Result result)
    {
        try
        {
            string markerPath = Path.Combine(this.modDirectoryPath, MarkerRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllText(markerPath, JsonConvert.SerializeObject(new FolderMigrationMarker
            {
                CompletedAtUtc = DateTime.UtcNow,
                SourcePath = result.LegacyFolderPath,
                MultiplayerFilesCopied = result.MultiplayerFilesCopied,
                ConversationLogFilesCopied = result.ConversationLogFilesCopied,
                LegacyConfigFound = result.LegacyConfigFound,
                Errors = result.Errors.ToList()
            }, Formatting.Indented));
        }
        catch (Exception ex)
        {
            result.Errors.Add($"marker: {ex.Message}");
        }
    }

    private void Report(Result result)
    {
        string summary = Util.GetConsoleString(
            "dialogue.migration.folder.summary",
            new
            {
                source = result.LegacyFolderPath,
                multiplayer = result.MultiplayerFilesCopied,
                transcripts = result.ConversationLogFilesCopied,
                config = result.LegacyConfigFound
            },
            $"Migrated ValleyTalk folder data from '{result.LegacyFolderPath}': {result.MultiplayerFilesCopied} multiplayer files, {result.ConversationLogFilesCopied} transcript files, config found: {result.LegacyConfigFound}.");
        DialogueServices.Monitor?.Log(summary, LogLevel.Info);

        foreach (string error in result.Errors)
        {
            DialogueServices.Monitor?.Log($"Folder migration issue: {error}", LogLevel.Warn);
        }
    }
}

internal sealed class FolderMigrationMarker
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("completedAtUtc")]
    public DateTime CompletedAtUtc { get; set; }

    [JsonProperty("sourcePath")]
    public string? SourcePath { get; set; }

    [JsonProperty("multiplayerFilesCopied")]
    public int MultiplayerFilesCopied { get; set; }

    [JsonProperty("conversationLogFilesCopied")]
    public int ConversationLogFilesCopied { get; set; }

    [JsonProperty("legacyConfigFound")]
    public bool LegacyConfigFound { get; set; }

    [JsonProperty("errors")]
    public List<string> Errors { get; set; } = new();
}
