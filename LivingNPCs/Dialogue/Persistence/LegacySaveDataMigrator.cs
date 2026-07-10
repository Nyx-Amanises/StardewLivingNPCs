using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 旧 mod（dandm1.ValleyTalk）存档数据 → 新键的迁移器（WP14 §4.2）。每次 SaveLoaded 由主机执行；
/// 幂等靠迁移标记键 dialogue.migration.v1（内存已迁移键集合，支持"装回 NPC mod 后补迁"）。
/// 全程绝不写 dandm1.valleytalk 前缀的任何键；旧键永不自动删除（裁决 1），仅 purge 命令手动清除。
/// </summary>
internal sealed class LegacySaveDataMigrator
{
    public const string LegacyPhysicalPrefix = "smapi/mod-data/dandm1.valleytalk/";
    public const string LegacyLedgerPhysicalKey = LegacyPhysicalPrefix + "tokenusageledger";
    public const string LegacyHistoryPhysicalPrefix = LegacyPhysicalPrefix + "eventhistory_";
    public const string MigrationMarkerKey = "dialogue.migration.v1";

    private readonly IPersistenceEnvironment env;
    private readonly TokenUsageTracker tracker;

    public LegacySaveDataMigrator(IPersistenceEnvironment env, TokenUsageTracker? tracker = null)
    {
        this.env = env;
        this.tracker = tracker ?? TokenUsageTracker.Instance;
    }

    public sealed class Result
    {
        public bool Ran { get; set; }
        public int MigratedNpcCount { get; set; }
        public bool LedgerMigrated { get; set; }
        public List<string> FailedKeys { get; } = new();
        public List<string> PendingKeys { get; } = new();
    }

    public Result RunOnSaveLoaded()
    {
        var result = new Result();
        // 只主机执行；旧 mod 仍在加载时跳过全部迁移（§4.1，对话引擎亦保持关闭，由 WP12 接线提示）。
        if (!this.env.IsWorldReady || !this.env.IsMainPlayer || this.env.IsLegacyValleyTalkLoaded)
        {
            return result;
        }

        var customData = this.env.GetSaveCustomData();
        if (customData == null)
        {
            return result;
        }

        var legacyKeys = customData.Keys
            .Where(key => key.StartsWith(LegacyPhysicalPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (legacyKeys.Count == 0)
        {
            return result;
        }

        var marker = this.ReadMarker();
        var candidates = legacyKeys
            .Where(key => !marker.MigratedKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
                && !marker.FailedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            return result;
        }

        var reverseNameMap = this.BuildReverseNameMap();
        var pending = new List<string>();

        foreach (string physicalKey in candidates)
        {
            if (!customData.TryGetValue(physicalKey, out string? rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                marker.FailedKeys.Add(physicalKey);
                result.FailedKeys.Add(physicalKey);
                continue;
            }

            if (string.Equals(physicalKey, LegacyLedgerPhysicalKey, StringComparison.OrdinalIgnoreCase))
            {
                if (this.TryMigrateLedger(rawValue))
                {
                    marker.MigratedKeys.Add(physicalKey);
                    result.LedgerMigrated = true;
                }
                else
                {
                    marker.FailedKeys.Add(physicalKey);
                    result.FailedKeys.Add(physicalKey);
                }

                continue;
            }

            if (physicalKey.StartsWith(LegacyHistoryPhysicalPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string lowerName = physicalKey[LegacyHistoryPhysicalPrefix.Length..];
                if (!reverseNameMap.TryGetValue(lowerName, out string? npcName))
                {
                    // 未匹配的残留键（多为已卸载的 NPC mod）：保留原样不迁移、不删除，留待补迁（裁决 4）。
                    pending.Add(physicalKey);
                    continue;
                }

                if (this.TryMigrateHistory(rawValue, npcName))
                {
                    marker.MigratedKeys.Add(physicalKey);
                    result.MigratedNpcCount++;
                }
                else
                {
                    marker.FailedKeys.Add(physicalKey);
                    result.FailedKeys.Add(physicalKey);
                }

                continue;
            }

            // 未知形态的旧键：保留原样，计入待定名单以便日志可见。
            pending.Add(physicalKey);
        }

        result.PendingKeys.AddRange(pending);
        // 仅剩 pending（未匹配 NPC）的一轮不算执行过：不写标记、不出日志，等补迁条件出现。
        result.Ran = result.MigratedNpcCount > 0 || result.LedgerMigrated || result.FailedKeys.Count > 0;

        if (result.Ran)
        {
            marker.CompletedAtUtc = DateTime.UtcNow;
            marker.MigratedNpcCount += result.MigratedNpcCount;
            marker.LedgerMigrated |= result.LedgerMigrated;
            marker.PendingKeys = pending;
            this.env.WriteSaveData(MigrationMarkerKey, marker);
        }

        this.Report(result);
        return result;
    }

    /// <summary>purge 命令的实现：从存档 CustomData 移除全部旧物理键（裁决 1，需用户显式 confirm）。</summary>
    public int PurgeLegacyKeys()
    {
        var customData = this.env.GetSaveCustomData();
        if (customData == null)
        {
            return 0;
        }

        var legacyKeys = customData.Keys
            .Where(key => key.StartsWith(LegacyPhysicalPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int removed = 0;
        foreach (string key in legacyKeys)
        {
            if (this.env.RemoveSaveCustomData(key))
            {
                removed++;
            }
        }

        return removed;
    }

    public int CountLegacyKeys()
    {
        var customData = this.env.GetSaveCustomData();
        return customData?.Keys.Count(key => key.StartsWith(LegacyPhysicalPrefix, StringComparison.OrdinalIgnoreCase)) ?? 0;
    }

    private MigrationMarker ReadMarker()
    {
        try
        {
            return this.env.ReadSaveJson(MigrationMarkerKey)?.ToObject<MigrationMarker>() ?? new MigrationMarker();
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "read the migration marker (a fresh migration is assumed)", error = ex.Message },
                    $"Failed to read the migration marker (a fresh migration is assumed): {ex.Message}"),
                LogLevel.Warn);
            return new MigrationMarker();
        }
    }

    /// <summary>物理键里的 NPC 名已被 SMAPI 小写化；对当前存档全部 NPC 内部名套净化规则再转小写建反查映射（§4.2.3）。</summary>
    private Dictionary<string, string> BuildReverseNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string npcName in this.env.KnownNpcNames)
        {
            string sanitizedLower = SaveNameSanitizer.SanitizeLower(npcName);
            if (sanitizedLower.Length > 0 && !map.ContainsKey(sanitizedLower))
            {
                map[sanitizedLower] = npcName;
            }
        }

        return map;
    }

    private bool TryMigrateLedger(string rawValue)
    {
        try
        {
            // 老存档没有 CacheWritePromptTokens 字段（0.1.5 期新增），缺省 0（§3.1.2）。
            var ledger = JsonConvert.DeserializeObject<TokenUsageLedger>(rawValue);
            if (ledger == null)
            {
                return false;
            }

            this.tracker.ImportMigratedLedger(ledger);
            // 立即写穿到新键（tracker 的 Saving 落盘只在主机，本方法本就仅主机执行）。
            this.env.WriteSaveData(TokenUsageTracker.SaveDataKey, ledger);
            return true;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.migrationFailed",
                    new { what = "token usage ledger", error = ex.Message },
                    $"Legacy ValleyTalk migration failed (token usage ledger): {ex.Message}"),
                LogLevel.Warn);
            return false;
        }
    }

    private bool TryMigrateHistory(string rawValue, string npcName)
    {
        try
        {
            var history = HistoryJson.Parse(JObject.Parse(rawValue), npcName);
            // 写之前跑一遍与读取路径相同的容错清洗（§4.2.4）；回档保护同样适用。
            history.RemoveAfter(this.env.Now);
            this.env.WriteSaveData(DialogueHistoryStore.HistoryLogicalKey(npcName), history);
            return true;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.migrationFailed",
                    new { what = $"dialogue history of {npcName}", error = ex.Message },
                    $"Legacy ValleyTalk migration failed (dialogue history of {npcName}): {ex.Message}"),
                LogLevel.Warn);
            return false;
        }
    }

    private void Report(Result result)
    {
        if (!result.Ran)
        {
            return;
        }

        string summary = Util.GetConsoleString(
            "dialogue.migration.save.summary",
            new
            {
                npcCount = result.MigratedNpcCount,
                ledger = result.LedgerMigrated,
                pendingCount = result.PendingKeys.Count,
                failedCount = result.FailedKeys.Count
            },
            $"Migrated ValleyTalk save data: {result.MigratedNpcCount} NPC histories, ledger: {result.LedgerMigrated}, pending: {result.PendingKeys.Count}, failed: {result.FailedKeys.Count}.");
        DialogueServices.Monitor?.Log(summary, LogLevel.Info);

        foreach (string key in result.PendingKeys)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.migration.keyDeferred", new { key }),
                LogLevel.Trace);
        }

        foreach (string key in result.FailedKeys)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.legacyKeyKept",
                    new { key },
                    $"Legacy key failed to migrate (kept untouched): {key}"),
                LogLevel.Warn);
        }

        if (result.MigratedNpcCount > 0 || result.LedgerMigrated)
        {
            Llm.LlmHudNotifier.Show(Util.GetConsoleString(
                "dialogue.migration.save.hud",
                new { npcCount = result.MigratedNpcCount },
                $"LivingNPCs: migrated dialogue memories of {result.MigratedNpcCount} NPCs from ValleyTalk."));
        }

        if (result.FailedKeys.Count > 0)
        {
            Llm.LlmHudNotifier.Show(Util.GetConsoleString(
                "dialogue.migration.save.hudPartial",
                null,
                "LivingNPCs: some ValleyTalk data could not be migrated; see the SMAPI log."));
        }
    }
}

/// <summary>迁移标记（存档键 dialogue.migration.v1 的值）。</summary>
internal sealed class MigrationMarker
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("completedAtUtc")]
    public DateTime? CompletedAtUtc { get; set; }

    [JsonProperty("migratedKeys")]
    public List<string> MigratedKeys { get; set; } = new();

    [JsonProperty("failedKeys")]
    public List<string> FailedKeys { get; set; } = new();

    [JsonProperty("pendingKeys")]
    public List<string> PendingKeys { get; set; } = new();

    [JsonProperty("migratedNpcCount")]
    public int MigratedNpcCount { get; set; }

    [JsonProperty("ledgerMigrated")]
    public bool LedgerMigrated { get; set; }
}
