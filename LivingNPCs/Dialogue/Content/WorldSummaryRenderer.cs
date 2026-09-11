using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 把 WorldSummary 分节数据渲染成 WP10 消费的最终字符串（WP15 §3.3）。
/// 提示词键（seasonCrops/seasonForage/generalAnd/gameSummaryTranslations）经注入的查找委托取得，
/// 使渲染逻辑可脱离游戏内容管线单测。
/// </summary>
internal sealed class WorldSummaryRenderer
{
    private readonly Func<string, string> getPrompt;
    private readonly IMonitor? monitor;

    /// <param name="getPrompt">提示词骨架查找；查不到返回空串。</param>
    public WorldSummaryRenderer(Func<string, string> getPrompt, IMonitor? monitor)
    {
        this.getPrompt = getPrompt;
        this.monitor = monitor;
    }

    public string Render(WorldSummary? summary)
    {
        if (summary == null)
        {
            return string.Empty;
        }

        if (summary.SectionOrder == null || summary.SectionOrder.Count == 0)
        {
            this.monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.assetInvalid",
                    new { asset = "world summary", problem = "it has no SectionOrder; an empty summary is returned" },
                    "Content asset 'world summary' is invalid: it has no SectionOrder; an empty summary is returned"),
                LogLevel.Error);
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var pair in summary.SectionOrder)
        {
            WorldSummarySection? section = summary.GetSection(pair.Key);
            if (section == null)
            {
                this.monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.assetInvalid",
                        new { asset = "world summary", problem = $"section '{pair.Key}' is listed in SectionOrder but missing; skipped" },
                        $"Content asset 'world summary' is invalid: section '{pair.Key}' is listed in SectionOrder but missing; skipped"),
                    LogLevel.Error);
                continue;
            }

            if (pair.Value)
            {
                builder.AppendLine($"### {pair.Key} :");
            }

            if (!string.IsNullOrWhiteSpace(section.Text))
            {
                builder.AppendLine(section.Text);
            }

            if (string.Equals(pair.Key, "Locations", StringComparison.Ordinal))
            {
                this.RenderLocationEntries(builder, section);
            }
            else
            {
                foreach (WorldSummaryEntry entry in section.Entries.Values)
                {
                    builder.AppendLine(this.RenderEntry(entry));
                }
            }

            builder.AppendLine();
        }

        string translations = this.getPrompt("gameSummaryTranslations");
        if (!string.IsNullOrWhiteSpace(translations))
        {
            builder.AppendLine(translations);
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>Locations 条目按 Region 分组（首见序），组前输出 Region 行。</summary>
    private void RenderLocationEntries(StringBuilder builder, WorldSummarySection section)
    {
        foreach (var group in section.Entries.Values.GroupBy(entry => entry.Region ?? string.Empty))
        {
            if (!string.IsNullOrWhiteSpace(group.Key))
            {
                builder.AppendLine($"**{group.Key}**");
            }

            foreach (WorldSummaryEntry entry in group)
            {
                builder.AppendLine(this.RenderEntry(entry));
            }
        }
    }

    internal string RenderEntry(WorldSummaryEntry entry)
    {
        var line = new StringBuilder($"- **{entry.Name}** - {entry.Description}");
        this.AppendNamedList(line, "seasonCrops", entry.Crops);
        this.AppendNamedList(line, "seasonForage", entry.Forage);
        return line.ToString();
    }

    /// <summary>Capture this localized constant while content access is still on the game thread.</summary>
    internal string GetTranslations() => this.getPrompt("gameSummaryTranslations");

    /// <summary>Render already captured entry strings without consulting the content pipeline.</summary>
    internal static string RenderCapturedSection(
        string name,
        bool showHeading,
        string text,
        IReadOnlyList<(string Region, string Text)> entries)
    {
        var builder = new StringBuilder();
        if (showHeading)
            builder.AppendLine($"### {name} :");
        if (!string.IsNullOrWhiteSpace(text))
            builder.AppendLine(text);

        if (string.Equals(name, "Locations", StringComparison.Ordinal))
        {
            foreach (var group in entries.GroupBy(entry => entry.Region))
            {
                if (!string.IsNullOrWhiteSpace(group.Key))
                    builder.AppendLine($"**{group.Key}**");
                foreach (var entry in group)
                    builder.AppendLine(entry.Text);
            }
        }
        else
        {
            foreach (var entry in entries)
                builder.AppendLine(entry.Text);
        }

        builder.AppendLine();
        return builder.ToString();
    }

    /// <summary>季节条目的作物/采集列表："{引导文案} A, B {and} C."。</summary>
    private void AppendNamedList(StringBuilder line, string leadKey, List<string>? items)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        string lead = this.getPrompt(leadKey);
        line.Append(' ');
        if (!string.IsNullOrWhiteSpace(lead))
        {
            line.Append(lead.TrimEnd()).Append(' ');
        }

        line.Append(JoinNaturally(items, this.getPrompt("generalAnd"))).Append('.');
    }

    /// <summary>"A, B 和 C" 式连接；连接词为空退化为全逗号。</summary>
    internal static string JoinNaturally(IReadOnlyList<string> items, string andWord)
    {
        if (items.Count == 1)
        {
            return items[0];
        }

        if (string.IsNullOrWhiteSpace(andWord))
        {
            return string.Join(", ", items);
        }

        return items.Count == 2
            ? $"{items[0]} {andWord.Trim()} {items[1]}"
            : $"{string.Join(", ", items.Take(items.Count - 1))} {andWord.Trim()} {items[^1]}";
    }
}
