using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace ValleyTalk;

internal sealed class TokenUsageTracker
{
    private const string SaveDataKey = "TokenUsageLedger";
    private const string ExportRootFolderName = "token_usage";
    private const int RecentEntryLimit = 20;

    public static TokenUsageTracker Instance { get; } = new();

    private TokenUsageLedger sessionLedger = new();
    private TokenUsageLedger saveLedger = new();
    private bool hasLoadedSaveLedger;

    private TokenUsageTracker()
    {
    }

    public void RegisterEvents()
    {
        ModEntry.SHelper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        ModEntry.SHelper.Events.GameLoop.Saving += this.OnSaving;
        ModEntry.SHelper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
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

    public string BuildConsoleSummary()
    {
        this.EnsureSaveLedgerLoaded();

        var builder = new StringBuilder();
        builder.AppendLine("ValleyTalk token usage");
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
        if (Context.IsWorldReady)
        {
            ModEntry.SHelper.Data.WriteSaveData(SaveDataKey, this.saveLedger);
        }
    }

    private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
    {
        this.hasLoadedSaveLedger = false;
        this.EnsureSaveLedgerLoaded();
    }

    private void OnSaving(object sender, SavingEventArgs e)
    {
        this.EnsureSaveLedgerLoaded();
        ModEntry.SHelper.Data.WriteSaveData(SaveDataKey, this.saveLedger);
        this.ExportCurrentSave();
    }

    private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
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

        this.saveLedger = ModEntry.SHelper.Data.ReadSaveData<TokenUsageLedger>(SaveDataKey) ?? new TokenUsageLedger();
        this.hasLoadedSaveLedger = true;
    }

    private string BuildMarkdownExport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ValleyTalk Token Usage");
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
        builder.AppendLine("| Time | NPC | Model | Prompt | Output | Total | Kind |");
        builder.AppendLine("| --- | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var entry in this.saveLedger.RecentEntries)
        {
            string time = entry.Year > 0
                ? $"Y{entry.Year} {entry.Season} {entry.DayOfMonth} {entry.TimeOfDay}"
                : entry.RecordedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
            string kind = entry.IsEstimated ? "estimated" : "official";
            builder.AppendLine($"| {time} | {entry.NpcName} | {entry.Provider}/{entry.ModelName} | {entry.PromptTokens} | {entry.CompletionTokens} | {entry.TotalTokens} | {kind} |");
        }

        return builder.ToString();
    }

    private string GetExportPath()
    {
        string saveFolder = Context.IsWorldReady && !string.IsNullOrWhiteSpace(Constants.SaveFolderName)
            ? Constants.SaveFolderName
            : "no_save_loaded";
        return Path.Combine(ModEntry.SHelper.DirectoryPath, ExportRootFolderName, $"{saveFolder}.md");
    }

    private string BuildTotalsLine(string label, TokenUsageTotals totals)
    {
        return $"{label}: {this.FormatTotals(totals)}";
    }

    private string FormatTotals(TokenUsageTotals totals)
    {
        return $"{totals.TotalTokens} total ({totals.PromptTokens} prompt + {totals.CompletionTokens} output; {totals.OfficialTokens} official, {totals.EstimatedTokens} estimated; cached prompt {totals.CachedPromptTokens}; reasoning {totals.ReasoningTokens})";
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

    public long ReasoningTokens { get; set; }

    public long OfficialTokens { get; set; }

    public long EstimatedTokens { get; set; }

    public void Add(TokenUsageEntry entry)
    {
        this.PromptTokens += entry.PromptTokens;
        this.CompletionTokens += entry.CompletionTokens;
        this.TotalTokens += entry.TotalTokens;
        this.CachedPromptTokens += entry.CachedPromptTokens;
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

    public int ReasoningTokens { get; set; }

    public bool IsEstimated { get; set; }

    public string Source { get; set; } = string.Empty;

    public int Year { get; set; }

    public string Season { get; set; } = string.Empty;

    public int DayOfMonth { get; set; }

    public int TimeOfDay { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}
