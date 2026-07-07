using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

using LivingNPCs.Dialogue.Llm;
namespace LivingNPCs.Dialogue.Persistence;

internal sealed class TokenUsageTracker
{
    // 新契约存档键（WP14 §5.1）；仅主机读写存档，远程 farmhand 只保留会话内统计（§3.1.2）。
    internal const string SaveDataKey = "dialogue.tokens.v1";
    private const string ExportRootFolderName = "token_usage";
    private const int RecentEntryLimit = 20;

    public static TokenUsageTracker Instance { get; } = new();

    private TokenUsageLedger sessionLedger = new();
    private TokenUsageLedger saveLedger = new();
    private bool hasLoadedSaveLedger;

    // 常规代码走 Instance；internal 构造仅供测试与迁移器注入独立实例。
    internal TokenUsageTracker()
    {
    }

    /// <summary>测试断言用：当前存档账本。</summary>
    internal TokenUsageLedger SaveLedgerForTests => this.saveLedger;

    public void RegisterEvents()
    {
        DialogueServices.Helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        DialogueServices.Helper.Events.GameLoop.Saving += this.OnSaving;
        DialogueServices.Helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
    }

    public void Record(string npcName, TokenUsage usage, string provider, string modelName, string outcome)
    {
        if (usage == null || !usage.HasAnyTokens)
        {
            return;
        }

        var entry = new TokenUsageEntry
        {
            NpcName = string.IsNullOrWhiteSpace(npcName) ? "(system)" : npcName,
            Provider = string.IsNullOrWhiteSpace(provider) ? "unknown" : provider,
            ModelName = string.IsNullOrWhiteSpace(modelName) ? "(default)" : modelName,
            Outcome = string.IsNullOrWhiteSpace(outcome) ? "unknown" : outcome,
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            TotalTokens = usage.TotalTokens,
            CachedPromptTokens = usage.CachedPromptTokens,
            CacheWritePromptTokens = usage.CacheWritePromptTokens,
            ReasoningTokens = usage.ReasoningTokens,
            IsEstimated = usage.IsEstimated,
            Source = usage.Source,
            Year = Context.IsWorldReady ? Game1.year : 0,
            Season = Context.IsWorldReady ? Game1.currentSeason : string.Empty,
            DayOfMonth = Context.IsWorldReady ? Game1.dayOfMonth : 0,
            TimeOfDay = Context.IsWorldReady ? Game1.timeOfDay : 0,
            RecordedAtUtc = DateTime.UtcNow
        };

        this.sessionLedger.Add(entry);
        if (Context.IsWorldReady)
        {
            this.EnsureSaveLedgerLoaded();
            this.saveLedger.Add(entry);
        }
    }

    /// <summary>迁移器导入旧账本（§4.2.2）：替换内存中的存档账本；新键的落盘由迁移器/Saving 负责。</summary>
    public void ImportMigratedLedger(TokenUsageLedger ledger)
    {
        if (ledger == null)
        {
            return;
        }

        this.saveLedger = ledger;
        this.hasLoadedSaveLedger = true;
    }

    public string BuildConsoleSummary()
    {
        this.EnsureSaveLedgerLoaded();

        var builder = new StringBuilder();
        builder.AppendLine("LivingNPCs token usage");
        if (Context.IsWorldReady && !Context.IsMainPlayer)
        {
            builder.AppendLine(Util.GetConsoleString(
                "dialogue.tokens.hostOnly",
                null,
                "Note: the token usage ledger is persisted by the host only; these are session stats."));
        }

        builder.AppendLine(this.BuildTotalsLine("Session", this.sessionLedger.Totals));
        builder.AppendLine(this.BuildTotalsLine("Current save", this.saveLedger.Totals));

        if (this.saveLedger.ByModel.Count > 0)
        {
            builder.AppendLine("By model:");
            foreach (var pair in this.saveLedger.ByModel
                .OrderByDescending(pair => pair.Value.TotalTokens)
                .Take(5))
            {
                builder.AppendLine($"- {pair.Key}: {this.FormatTotals(pair.Value)}");
            }
        }

        if (this.saveLedger.ByNpc.Count > 0)
        {
            builder.AppendLine("Top NPCs:");
            foreach (var pair in this.saveLedger.ByNpc
                .OrderByDescending(pair => pair.Value.TotalTokens)
                .Take(8))
            {
                builder.AppendLine($"- {pair.Key}: {this.FormatTotals(pair.Value)}");
            }
        }

        if (this.saveLedger.RecentEntries.Count > 0)
        {
            builder.AppendLine("Recent requests:");
            foreach (var entry in this.saveLedger.RecentEntries.TakeLast(5))
            {
                string estimateLabel = entry.IsEstimated ? "estimated" : "official";
                builder.AppendLine($"- {entry.NpcName}: {entry.TotalTokens} total ({entry.PromptTokens} prompt + {entry.CompletionTokens} output, {estimateLabel}, {entry.Provider}/{entry.ModelName})");
            }
        }

        if (Context.IsWorldReady)
        {
            builder.AppendLine($"Export path: {this.GetExportPath()}");
        }

        return builder.ToString().TrimEnd();
    }

    public string ExportCurrentSave()
    {
        this.EnsureSaveLedgerLoaded();
        string exportPath = this.GetExportPath();
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        File.WriteAllText(exportPath, this.BuildMarkdownExport(), Encoding.UTF8);
        return exportPath;
    }

    public void ResetCurrentSave()
    {
        this.saveLedger = new TokenUsageLedger();
        this.hasLoadedSaveLedger = true;
        if (Context.IsWorldReady && Context.IsMainPlayer)
        {
            DialogueServices.Helper.Data.WriteSaveData(SaveDataKey, this.saveLedger);
        }
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.hasLoadedSaveLedger = false;
        this.EnsureSaveLedgerLoaded();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.EnsureSaveLedgerLoaded();
        if (Context.IsMainPlayer)
        {
            DialogueServices.Helper.Data.WriteSaveData(SaveDataKey, this.saveLedger);
        }

        this.ExportCurrentSave();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.saveLedger = new TokenUsageLedger();
        this.hasLoadedSaveLedger = false;
    }

    private void EnsureSaveLedgerLoaded()
    {
        if (this.hasLoadedSaveLedger || !Context.IsWorldReady)
        {
            return;
        }

        // 远程 farmhand 调用 ReadSaveData 会被 SMAPI 拒绝：不读存档，只保留会话内统计。
        this.saveLedger = Context.IsMainPlayer
            ? DialogueServices.Helper.Data.ReadSaveData<TokenUsageLedger>(SaveDataKey) ?? new TokenUsageLedger()
            : new TokenUsageLedger();
        this.hasLoadedSaveLedger = true;
    }

    private string BuildMarkdownExport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# LivingNPCs Token Usage");
        builder.AppendLine();
        builder.AppendLine($"- Save: `{Constants.SaveFolderName}`");
        builder.AppendLine($"- Exported UTC: `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}`");
        builder.AppendLine($"- {this.BuildTotalsLine("Current save", this.saveLedger.Totals)}");
        builder.AppendLine();
        builder.AppendLine("## By model");
        builder.AppendLine();
        foreach (var pair in this.saveLedger.ByModel.OrderByDescending(pair => pair.Value.TotalTokens))
        {
            builder.AppendLine($"- `{pair.Key}`: {this.FormatTotals(pair.Value)}");
        }

        builder.AppendLine();
        builder.AppendLine("## By NPC");
        builder.AppendLine();
        foreach (var pair in this.saveLedger.ByNpc.OrderByDescending(pair => pair.Value.TotalTokens))
        {
            builder.AppendLine($"- `{pair.Key}`: {this.FormatTotals(pair.Value)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Recent requests");
        builder.AppendLine();
        builder.AppendLine("| Time | NPC | Model | Prompt | Cached | Output | Total | Kind |");
        builder.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | --- |");
        foreach (var entry in this.saveLedger.RecentEntries)
        {
            string time = entry.Year > 0
                ? $"Y{entry.Year} {entry.Season} {entry.DayOfMonth} {entry.TimeOfDay}"
                : entry.RecordedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
            string kind = entry.IsEstimated ? "estimated" : "official";
            builder.AppendLine($"| {time} | {entry.NpcName} | {entry.Provider}/{entry.ModelName} | {entry.PromptTokens} | {entry.CachedPromptTokens} | {entry.CompletionTokens} | {entry.TotalTokens} | {kind} |");
        }

        return builder.ToString();
    }

    private string GetExportPath()
    {
        string saveFolder = Context.IsWorldReady && !string.IsNullOrWhiteSpace(Constants.SaveFolderName)
            ? Constants.SaveFolderName
            : "no_save_loaded";
        return Path.Combine(DialogueServices.Helper.DirectoryPath, ExportRootFolderName, $"{saveFolder}.md");
    }

    private string BuildTotalsLine(string label, TokenUsageTotals totals)
    {
        return $"{label}: {this.FormatTotals(totals)}";
    }

    private string FormatTotals(TokenUsageTotals totals)
    {
        string cacheHitRate = totals.PromptTokens > 0
            ? $" ({totals.CachedPromptTokens * 100.0 / totals.PromptTokens:0.#}% hit)"
            : string.Empty;
        return $"{totals.TotalTokens} total ({totals.PromptTokens} prompt + {totals.CompletionTokens} output; {totals.OfficialTokens} official, {totals.EstimatedTokens} estimated; cache read {totals.CachedPromptTokens}{cacheHitRate}, cache write {totals.CacheWritePromptTokens}; reasoning {totals.ReasoningTokens})";
    }
}

internal sealed class TokenUsageLedger
{
    private const int RecentEntryLimit = 20;

    public TokenUsageTotals Totals { get; set; } = new();

    public Dictionary<string, TokenUsageTotals> ByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, TokenUsageTotals> ByNpc { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<TokenUsageEntry> RecentEntries { get; set; } = new();

    public void Add(TokenUsageEntry entry)
    {
        this.Totals.Add(entry);

        string modelKey = $"{entry.Provider}/{entry.ModelName}";
        if (!this.ByModel.TryGetValue(modelKey, out var modelTotals))
        {
            modelTotals = new TokenUsageTotals();
            this.ByModel[modelKey] = modelTotals;
        }
        modelTotals.Add(entry);

        if (!this.ByNpc.TryGetValue(entry.NpcName, out var npcTotals))
        {
            npcTotals = new TokenUsageTotals();
            this.ByNpc[entry.NpcName] = npcTotals;
        }
        npcTotals.Add(entry);

        this.RecentEntries.Add(entry);
        while (this.RecentEntries.Count > RecentEntryLimit)
        {
            this.RecentEntries.RemoveAt(0);
        }
    }
}

internal sealed class TokenUsageTotals
{
    public long PromptTokens { get; set; }

    public long CompletionTokens { get; set; }

    public long TotalTokens { get; set; }

    public long CachedPromptTokens { get; set; }

    public long CacheWritePromptTokens { get; set; }

    public long ReasoningTokens { get; set; }

    public long OfficialTokens { get; set; }

    public long EstimatedTokens { get; set; }

    public void Add(TokenUsageEntry entry)
    {
        this.PromptTokens += entry.PromptTokens;
        this.CompletionTokens += entry.CompletionTokens;
        this.TotalTokens += entry.TotalTokens;
        this.CachedPromptTokens += entry.CachedPromptTokens;
        this.CacheWritePromptTokens += entry.CacheWritePromptTokens;
        this.ReasoningTokens += entry.ReasoningTokens;

        if (entry.IsEstimated)
        {
            this.EstimatedTokens += entry.TotalTokens;
        }
        else
        {
            this.OfficialTokens += entry.TotalTokens;
        }
    }
}

internal sealed class TokenUsageEntry
{
    public string NpcName { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int TotalTokens { get; set; }

    public int CachedPromptTokens { get; set; }

    public int CacheWritePromptTokens { get; set; }

    public int ReasoningTokens { get; set; }

    public bool IsEstimated { get; set; }

    public string Source { get; set; } = string.Empty;

    public int Year { get; set; }

    public string Season { get; set; } = string.Empty;

    public int DayOfMonth { get; set; }

    public int TimeOfDay { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}
