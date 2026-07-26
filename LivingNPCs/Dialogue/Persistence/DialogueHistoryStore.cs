using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 每 NPC 对话历史的存取通道（WP14 §5）。写操作进内存缓存，Saving 时统一落盘；
/// "遗忘"立即落盘；SaveLoaded/ReturnedToTitle 清空会话缓存。
/// 主机读写 SaveData（逻辑键 dialogue.history.v1_&lt;净化名小写&gt;，slug 全小写）；
/// 远程 farmhand 读写 multiplayer/&lt;SaveFolderName&gt;.json（键为 NPC 内部名原大小写），
/// 文件名按当前存档每次计算（修正旧实现"单例定死文件名"的怪癖，§3.2.1）。
/// 物理护栏（§5.3）：单 NPC 序列化 &gt;256KB 触发强制修剪回调，仍超限则丢最老条目直至达标；
/// 全 mod 键总体积 &gt;4MB 记 Warn。修剪策略本身归 WP10。
/// </summary>
internal sealed class DialogueHistoryStore : IDialogueHistory
{
    public const string HistoryKeyPrefix = "dialogue.history.v1_";
    private const string MultiplayerFolderName = "multiplayer";
    private const string OwnPhysicalPrefix = "smapi/mod-data/yuki.livingnpcs/";
    internal const int MaxSerializedNpcBytes = 256 * 1024;
    internal const long TotalSizeWarnBytes = 4L * 1024 * 1024;

    private static DialogueHistoryStore? instance;

    /// <summary>进程级单例；测试可用 SetInstance 注入假环境实例。</summary>
    public static DialogueHistoryStore Instance =>
        instance ??= new DialogueHistoryStore(new SmapiPersistenceEnvironment(DialogueServices.Helper));

    internal static void SetInstance(DialogueHistoryStore? store)
    {
        instance = store;
    }

    private readonly IPersistenceEnvironment env;
    private readonly object gate = new();
    private readonly Dictionary<string, StardewEventHistory> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> dirty = new(StringComparer.OrdinalIgnoreCase);
    private bool totalSizeWarned;

    /// <summary>WP10 注入的强制修剪回调（护栏触发时优先调用）；缺省直接用模型自带的契约修剪。</summary>
    public Func<string, StardewEventHistory, List<Tuple<StardewTime, ConversationHistory>>>? ForcePruneCallback { get; set; }

    /// <summary>被修剪会话的归档通道；缺省接转录导出器。测试可替换。</summary>
    public Action<string, List<Tuple<StardewTime, ConversationHistory>>>? ArchiveSink { get; set; }
        = Diagnostics.ConversationTranscriptExporter.ArchivePrunedConversations;

    public DialogueHistoryStore(IPersistenceEnvironment env)
    {
        this.env = env;
    }

    /// <summary>v2 无碰撞键（F8）：常规名字与 v1 完全一致；含非法字符/超长的名字改用单射编码。</summary>
    public static string HistoryLogicalKey(string npcName)
    {
        return HistoryKeyPrefix + SaveNameSanitizer.SanitizeUniqueLower(npcName);
    }

    /// <summary>v1 旧键（可碰撞的删字符/低字节截断形态）：仅供读取回退与迁移改名（F8）。</summary>
    internal static string LegacyHistoryLogicalKey(string npcName)
    {
        return HistoryKeyPrefix + SaveNameSanitizer.SanitizeLower(npcName);
    }

    /// <summary>取某 NPC 的历史（缓存实例，可直接变更；变更后调 MarkDirty）。</summary>
    public StardewEventHistory GetHistory(string npcName)
    {
        lock (this.gate)
        {
            return this.GetOrLoad(npcName);
        }
    }

    public void MarkDirty(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return;
        }

        lock (this.gate)
        {
            this.dirty.Add(npcName);
        }
    }

    /// <summary>WP10 的修剪入口：执行契约修剪并把被修剪会话交给归档通道。</summary>
    public void PruneAndArchive(string npcName)
    {
        List<Tuple<StardewTime, ConversationHistory>> dropped;
        lock (this.gate)
        {
            var history = this.GetOrLoad(npcName);
            dropped = history.Prune(this.env.Now);
            if (dropped.Count > 0)
            {
                this.dirty.Add(npcName);
            }
        }

        this.ArchiveDropped(npcName, dropped);
    }

    /// <summary>让 NPC 遗忘：写一份空历史并立即落盘。返回是否确有历史被清除。</summary>
    public bool Forget(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return false;
        }

        lock (this.gate)
        {
            return this.ForgetNoLockInternal(npcName);
        }
    }

    /// <summary>全量清除（含未缓存但已持久化的 NPC）。返回是否确有历史被清除。</summary>
    public bool ForgetAll()
    {
        lock (this.gate)
        {
            bool anyCleared = false;
            foreach (string npcName in this.EnumeratePersistedNpcNames().Union(this.cache.Keys.ToList(), StringComparer.OrdinalIgnoreCase).ToList())
            {
                anyCleared |= this.ForgetNoLockInternal(npcName);
            }

            return anyCleared;
        }
    }

    private bool ForgetNoLockInternal(string npcName)
    {
        var existing = this.GetOrLoad(npcName);
        bool hadContent = existing.Any() || existing.ThirdPartyHistory.Count > 0;
        this.cache[npcName] = new StardewEventHistory { NpcName = npcName };
        if (this.PersistOne(npcName))
        {
            this.dirty.Remove(npcName);
        }
        else
        {
            // 立即落盘失败（如帮工文件暂不可写）：保留脏标记，下次保存重试（F6）。
            this.dirty.Add(npcName);
        }

        return hadContent;
    }

    // ---- IDialogueHistory（01 §2） ----

    public void Append(string npcName, ExchangeRecord record)
    {
        if (string.IsNullOrWhiteSpace(npcName) || record == null)
        {
            return;
        }

        lock (this.gate)
        {
            var history = this.GetOrLoad(npcName);
            switch (record.Kind)
            {
                case ExchangeKind.Conversation:
                    UpsertConversation(history, record);
                    break;
                case ExchangeKind.DialogueLines:
                    history.Add(record.Time, new DialogueHistory(ToHistoryLines(record.Lines)));
                    break;
                case ExchangeKind.EventLines:
                    history.Add(record.Time, new DialogueEventHistory(
                        new List<string>(record.ListenerNames ?? new List<string>()),
                        ToHistoryLines(record.Lines),
                        record.EventName));
                    break;
                case ExchangeKind.Overheard:
                    history.Add(record.Time, new OverheardHistory(record.SpeakerName, ToHistoryLines(record.Lines)));
                    break;
            }

            this.dirty.Add(npcName);
        }
    }

    public IReadOnlyList<ExchangeRecord> Recent(string npcName, int maxItems)
    {
        if (string.IsNullOrWhiteSpace(npcName) || maxItems <= 0)
        {
            return Array.Empty<ExchangeRecord>();
        }

        lock (this.gate)
        {
            var history = this.GetOrLoad(npcName);
            var merged = new List<ExchangeRecord>();

            merged.AddRange(history.ConversationHistory.Select(entry => new ExchangeRecord
            {
                Kind = ExchangeKind.Conversation,
                Time = entry.Item1,
                ConversationId = entry.Item2.ConversationElements.FirstOrDefault()?.Id ?? string.Empty,
                Lines = entry.Item2.ConversationElements
                    .Select(element => new ExchangeLine(element.Text, element.IsPlayerLine))
                    .ToList()
            }));

            merged.AddRange(history.DialogueHistory.Select(entry => new ExchangeRecord
            {
                Kind = ExchangeKind.DialogueLines,
                Time = entry.Item1,
                Lines = entry.Item2.Dialogues.Select(line => new ExchangeLine(line.Text, false)).ToList()
            }));

            merged.AddRange(history.EventHistory.Select(entry => new ExchangeRecord
            {
                Kind = ExchangeKind.EventLines,
                Time = entry.Item1,
                EventName = entry.Item2.EventName,
                ListenerNames = new List<string>(entry.Item2.Listeners),
                Lines = entry.Item2.Dialogues.Select(line => new ExchangeLine(line.Text, false)).ToList()
            }));

            merged.AddRange(history.OverheardHistory.Select(entry => new ExchangeRecord
            {
                Kind = ExchangeKind.Overheard,
                Time = entry.Item1,
                SpeakerName = entry.Item2.SpeakerName,
                Lines = entry.Item2.Dialogues.Select(line => new ExchangeLine(line.Text, false)).ToList()
            }));

            var ordered = merged.OrderBy(record => record.Time).ToList();
            return ordered.Count <= maxItems ? ordered : ordered.Skip(ordered.Count - maxItems).ToList();
        }
    }

    // ---- 事件时机（由 DialoguePersistence/WP12 接线） ----

    public void OnSaveLoaded()
    {
        lock (this.gate)
        {
            this.cache.Clear();
            this.dirty.Clear();
            this.totalSizeWarned = false;
        }
    }

    public void OnReturnedToTitle()
    {
        lock (this.gate)
        {
            this.cache.Clear();
            this.dirty.Clear();
        }
    }

    public void FlushOnSaving()
    {
        lock (this.gate)
        {
            if (!this.env.IsWorldReady || this.dirty.Count == 0)
            {
                return;
            }

            if (this.env.IsMainPlayer)
            {
                // 脏标记只在该 NPC 确实写成功后清除（F6）：失败的留到下次保存重试。
                foreach (string npcName in this.dirty.ToList())
                {
                    try
                    {
                        this.PersistHost(npcName);
                        this.dirty.Remove(npcName);
                    }
                    catch (Exception ex)
                    {
                        DialogueServices.Monitor?.Log(
                            Util.GetConsoleString(
                                "dialogue.log.stepFailed",
                                new { step = $"persist the dialogue history of {npcName}", error = ex.Message },
                                $"Failed to persist the dialogue history of {npcName}: {ex.Message}"),
                            LogLevel.Warn);
                    }
                }

                this.WarnIfTotalSizeExceeded();
            }
            else if (this.PersistFarmhandEntries(this.dirty.ToList()))
            {
                // 帮工历史是单文件整体写：只有写成功才清全部脏标记（F6）。
                this.dirty.Clear();
            }
        }
    }

    // ---- 私有实现（调用方持有 gate） ----

    private StardewEventHistory GetOrLoad(string npcName)
    {
        if (this.cache.TryGetValue(npcName, out var cached))
        {
            return cached;
        }

        var history = this.Load(npcName);
        this.cache[npcName] = history;
        return history;
    }

    private StardewEventHistory Load(string npcName)
    {
        if (!this.env.IsWorldReady)
        {
            return new StardewEventHistory { NpcName = npcName };
        }

        try
        {
            JObject? root;
            string? migrateFromLegacyKey = null;
            if (this.env.IsMainPlayer)
            {
                root = this.env.ReadSaveJson(HistoryLogicalKey(npcName));
                if (root == null)
                {
                    root = this.TryReadLegacySlugRoot(npcName, out migrateFromLegacyKey);
                }
            }
            else
            {
                root = this.FindFarmhandEntry(npcName);
            }

            var history = HistoryJson.Parse(root, npcName);
            // 回档保护：删除晚于当前游戏时间的条目。
            history.RemoveAfter(this.env.Now);
            if (migrateFromLegacyKey != null)
            {
                this.MigrateLegacySlug(npcName, migrateFromLegacyKey, history);
            }

            return history;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.historyLoadFailed",
                    new { npc = npcName, error = ex.Message },
                    $"Failed to load dialogue history for {npcName}; treating as empty: {ex.Message}"),
                LogLevel.Error);
            return new StardewEventHistory { NpcName = npcName };
        }
    }

    private bool PersistOne(string npcName)
    {
        if (!this.env.IsWorldReady)
        {
            // 未载入存档：无处可写，视为已处理（缓存即最终状态）。
            return true;
        }

        if (this.env.IsMainPlayer)
        {
            try
            {
                this.PersistHost(npcName);
                return true;
            }
            catch (Exception ex)
            {
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.stepFailed",
                        new { step = $"persist the dialogue history of {npcName}", error = ex.Message },
                        $"Failed to persist the dialogue history of {npcName}: {ex.Message}"),
                    LogLevel.Warn);
                return false;
            }
        }

        return this.PersistFarmhandEntries(new[] { npcName });
    }

    /// <summary>
    /// v1 旧键回退读取（F8）：仅当 v2 键与 v1 键不同、旧槽存在、且旧槽记录的 npcName
    /// 属于本 NPC（或旧数据未记名）时返回旧槽内容；撞键槽属于别的 NPC 时视为无数据，
    /// 绝不继承他人历史、也不动那个槽（留给真正的主人迁移）。
    /// </summary>
    private JObject? TryReadLegacySlugRoot(string npcName, out string? legacyKey)
    {
        legacyKey = null;
        string v1Key = LegacyHistoryLogicalKey(npcName);
        if (string.Equals(v1Key, HistoryLogicalKey(npcName), StringComparison.Ordinal))
        {
            return null;
        }

        JObject? legacy = this.env.ReadSaveJson(v1Key);
        if (legacy == null)
        {
            return null;
        }

        string? owner = legacy.Value<string>("npcName");
        if (!string.IsNullOrWhiteSpace(owner) && !string.Equals(owner, npcName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        legacyKey = v1Key;
        return legacy;
    }

    /// <summary>把 v1 旧槽迁往 v2 无碰撞键（写新、删旧一次完成，同一次保存原子生效）；
    /// 失败则保留旧槽不动，下次读取重试。</summary>
    private void MigrateLegacySlug(string npcName, string legacyKey, StardewEventHistory history)
    {
        try
        {
            history.NpcName = npcName;
            this.env.WriteSaveData(HistoryLogicalKey(npcName), history);
            this.env.WriteSaveData(legacyKey, null);
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.historySlugMigrated",
                    new { npc = npcName },
                    $"Migrated the dialogue history of {npcName} to a collision-free save key."),
                LogLevel.Info);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = $"migrate the dialogue history key of {npcName}", error = ex.Message },
                    $"Failed to migrate the dialogue history key of {npcName}: {ex.Message}"),
                LogLevel.Warn);
        }
    }

    private void PersistHost(string npcName)
    {
        if (!this.cache.TryGetValue(npcName, out var history))
        {
            return;
        }

        history.NpcName = npcName;
        string json = SerializeCompact(history);
        if (Encoding.UTF8.GetByteCount(json) > MaxSerializedNpcBytes)
        {
            var dropped = this.ForcePruneCallback != null
                ? this.ForcePruneCallback(npcName, history)
                : history.Prune(this.env.Now);

            json = SerializeCompact(history);
            var extraDropped = new List<Tuple<StardewTime, ConversationHistory>>();
            while (Encoding.UTF8.GetByteCount(json) > MaxSerializedNpcBytes && DropOldestEntry(history, extraDropped))
            {
                json = SerializeCompact(history);
            }

            dropped.AddRange(extraDropped);
            this.ArchiveDropped(npcName, dropped);
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.historyPruned",
                    new { npc = npcName, kb = MaxSerializedNpcBytes / 1024 },
                    $"Dialogue history for {npcName} exceeded {MaxSerializedNpcBytes / 1024} KB; forced a prune before saving."),
                LogLevel.Warn);
        }

        this.env.WriteSaveData(HistoryLogicalKey(npcName), history);
    }

    private bool PersistFarmhandEntries(IReadOnlyCollection<string> npcNames)
    {
        string? relativePath = this.MultiplayerRelativePath();
        if (relativePath == null)
        {
            return false;
        }

        try
        {
            // 读损坏时隔离坏文件后从空开始（F6）：写入立即恢复，不再每晚静默失败。
            JObject root = this.ReadFarmhandRootTolerant(relativePath) ?? new JObject();
            foreach (string npcName in npcNames)
            {
                if (!this.cache.TryGetValue(npcName, out var history))
                {
                    continue;
                }

                history.NpcName = npcName;
                // 键为 NPC 内部名原大小写；替换文件里大小写不同的旧键，避免双份。
                string? existingKey = root.Properties()
                    .Select(property => property.Name)
                    .FirstOrDefault(name => string.Equals(name, npcName, StringComparison.OrdinalIgnoreCase));
                if (existingKey != null && existingKey != npcName)
                {
                    root.Remove(existingKey);
                }

                root[npcName] = JObject.FromObject(history, CreateSerializer());
            }

            this.env.WriteModJsonFile(relativePath, root);
            return true;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "write the farmhand dialogue history file", error = ex.Message },
                    $"Failed to write the farmhand dialogue history file: {ex.Message}"),
                LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// 容错读取帮工历史根对象（F6）：解析失败先把坏文件改名成 .corrupt-时间戳 备份
    /// （绝不静默丢弃字节），再按"无文件"处理——后续读取干净、下次写入直接重建。
    /// </summary>
    private JObject? ReadFarmhandRootTolerant(string relativePath)
    {
        try
        {
            return this.env.ReadModJsonFile(relativePath);
        }
        catch (Exception ex)
        {
            this.QuarantineCorruptFarmhandFile(relativePath, ex);
            return null;
        }
    }

    private void QuarantineCorruptFarmhandFile(string relativePath, Exception cause)
    {
        string backupLabel;
        try
        {
            string source = Path.Combine(this.env.ModDirectoryPath, relativePath);
            string target = source + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            if (File.Exists(source))
            {
                File.Move(source, target);
                backupLabel = target;
            }
            else
            {
                backupLabel = "(source file not found)";
            }
        }
        catch (Exception moveEx)
        {
            // 改名失败也不致命：写入路径本就整文件覆盖，仅备份缺失。
            backupLabel = $"(rename failed: {moveEx.Message})";
        }

        DialogueServices.Monitor?.Log(
            Util.GetConsoleString(
                "dialogue.log.farmhandHistoryQuarantined",
                new { file = relativePath, backup = backupLabel, error = cause.Message },
                $"The farmhand dialogue history file '{relativePath}' is corrupt and was quarantined to '{backupLabel}'; starting fresh so future saves work again: {cause.Message}"),
            LogLevel.Warn);
    }

    private JObject? FindFarmhandEntry(string npcName)
    {
        string? relativePath = this.MultiplayerRelativePath();
        if (relativePath == null)
        {
            return null;
        }

        JObject? root = this.ReadFarmhandRootTolerant(relativePath);
        if (root == null)
        {
            return null;
        }

        if (root[npcName] is JObject exact)
        {
            return exact;
        }

        var property = root.Properties()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, npcName, StringComparison.OrdinalIgnoreCase));
        return property?.Value as JObject;
    }

    private string? MultiplayerRelativePath()
    {
        string? saveFolder = this.env.SaveFolderName;
        return string.IsNullOrWhiteSpace(saveFolder) ? null : $"{MultiplayerFolderName}/{saveFolder}.json";
    }

    private IEnumerable<string> EnumeratePersistedNpcNames()
    {
        if (!this.env.IsWorldReady)
        {
            return Enumerable.Empty<string>();
        }

        if (!this.env.IsMainPlayer)
        {
            string? relativePath = this.MultiplayerRelativePath();
            var root = relativePath == null ? null : this.ReadFarmhandRootTolerant(relativePath);
            return root?.Properties().Select(property => property.Name).ToList() ?? new List<string>();
        }

        var data = this.env.GetSaveCustomData();
        if (data == null)
        {
            return Enumerable.Empty<string>();
        }

        // 新历史 JSON 顶层冗余存有原名（§5.1），枚举式处理不再依赖小写键反查。
        var names = new List<string>();
        foreach (var pair in data)
        {
            if (!pair.Key.StartsWith(OwnPhysicalPrefix + HistoryKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                string? npcName = JObject.Parse(pair.Value).Value<string>("npcName");
                if (!string.IsNullOrWhiteSpace(npcName))
                {
                    names.Add(npcName);
                }
            }
            catch
            {
                // 坏值由读取路径按空历史处理，这里跳过即可。
            }
        }

        return names;
    }

    private void WarnIfTotalSizeExceeded()
    {
        if (this.totalSizeWarned)
        {
            return;
        }

        var data = this.env.GetSaveCustomData();
        if (data == null)
        {
            return;
        }

        long totalBytes = 0;
        foreach (var pair in data)
        {
            if (pair.Key.StartsWith(OwnPhysicalPrefix, StringComparison.OrdinalIgnoreCase))
            {
                totalBytes += Encoding.UTF8.GetByteCount(pair.Value ?? string.Empty);
            }
        }

        if (totalBytes > TotalSizeWarnBytes)
        {
            this.totalSizeWarned = true;
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.saveDataLarge",
                    new { kb = totalBytes / 1024, mb = TotalSizeWarnBytes / 1024 / 1024 },
                    $"LivingNPCs save data totals {totalBytes / 1024} KB (> {TotalSizeWarnBytes / 1024 / 1024} MB); saving may slow down. Consider forgetting some NPC histories."),
                LogLevel.Warn);
        }
    }

    private void ArchiveDropped(string npcName, List<Tuple<StardewTime, ConversationHistory>> dropped)
    {
        if (dropped == null || dropped.Count == 0)
        {
            return;
        }

        try
        {
            this.ArchiveSink?.Invoke(npcName, dropped);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = $"archive pruned conversations for {npcName}", error = ex.Message },
                    $"Failed to archive pruned conversations for {npcName}: {ex.Message}"),
                LogLevel.Warn);
        }
    }

    private static void UpsertConversation(StardewEventHistory history, ExchangeRecord record)
    {
        var elements = record.Lines
            .Select(line => new ConversationElement(line.Text, line.IsPlayerLine))
            .ToList();
        if (!string.IsNullOrWhiteSpace(record.ConversationId) && elements.Count > 0)
        {
            elements[0].Id = record.ConversationId;
            int existingIndex = history.ConversationHistory.FindIndex(entry =>
                entry.Item2.ConversationElements.FirstOrDefault()?.Id == record.ConversationId);
            if (existingIndex >= 0)
            {
                history.ConversationHistory[existingIndex] = Tuple.Create(record.Time, new ConversationHistory(elements));
                return;
            }
        }

        history.Add(record.Time, new ConversationHistory(elements));
    }

    private static List<HistoryLine> ToHistoryLines(List<ExchangeLine>? lines)
    {
        return lines?.Select(line => new HistoryLine(line.Text)).ToList() ?? new List<HistoryLine>();
    }

    private static string SerializeCompact(StardewEventHistory history)
    {
        return JsonConvert.SerializeObject(history, Formatting.None, new StringEnumConverter());
    }

    private static JsonSerializer CreateSerializer()
    {
        var serializer = new JsonSerializer();
        serializer.Converters.Add(new StringEnumConverter());
        return serializer;
    }

    private static bool DropOldestEntry(StardewEventHistory history, List<Tuple<StardewTime, ConversationHistory>> droppedConversations)
    {
        StardewTime? oldest = null;
        int bucket = -1;
        int index = -1;

        void Consider<T>(List<Tuple<StardewTime, T>> list, int bucketId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (oldest == null || list[i].Item1.CompareTo(oldest.Value) < 0)
                {
                    oldest = list[i].Item1;
                    bucket = bucketId;
                    index = i;
                }
            }
        }

        Consider(history.EventHistory, 0);
        Consider(history.OverheardHistory, 1);
        Consider(history.DialogueHistory, 2);
        Consider(history.ConversationHistory, 3);

        switch (bucket)
        {
            case 0:
                history.EventHistory.RemoveAt(index);
                return true;
            case 1:
                history.OverheardHistory.RemoveAt(index);
                return true;
            case 2:
                history.DialogueHistory.RemoveAt(index);
                return true;
            case 3:
                droppedConversations.Add(history.ConversationHistory[index]);
                history.ConversationHistory.RemoveAt(index);
                return true;
            default:
                return false;
        }
    }
}
