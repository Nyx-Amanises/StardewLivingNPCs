using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class BehaviorDiagnostics
{
    public static string BuildNpcMarkdownReport(
        NPC npc,
        BehaviorMemory memory,
        ModConfig config)
    {
        string summary = memory.BuildDebugSummary(npc, config.PromptMemoryEntries, config.EnableNpcState);
        string prompt = memory.BuildPromptContext(
            npc,
            config.PromptMemoryEntries,
            config.EnableNpcState,
            config.EnableHelpRequests ? config.MaxPendingHelpRequestsPerNpc : 0,
            config.HelpRequestCooldownDays,
            config.MinRelationshipTrustForHelpRequests
        );
        var world = WorldContext.For(npc, memory.GetState(npc));

        var report = new StringBuilder();
        report.AppendLine($"# LivingNPCs 调试报告：{npc.displayName}");
        report.AppendLine();
        report.AppendLine("| 项目 | 值 |");
        report.AppendLine("| --- | --- |");
        report.AppendLine($"| NPC | {EscapeTable(npc.Name)} / {EscapeTable(npc.displayName)} |");
        report.AppendLine($"| 地点 | {EscapeTable(world.LocationDisplayName)} |");
        report.AppendLine($"| 日期时间 | {EscapeTable($"{world.Season} {world.DayOfMonth}，{world.TimeOfDay}")} |");
        report.AppendLine($"| 场景 | {EscapeTable(world.DebugLabel)} |");
        report.AppendLine($"| 世界进度 | {EscapeTable(world.Progression.DebugLabel)} |");
        report.AppendLine($"| NPC 可知进度 | {EscapeTable(world.ProgressionKnowledge.DebugLabel)} |");
        report.AppendLine($"| 求助生成适配 | {EscapeTable(HelpRequestAdvisor.BuildDebugLabel(npc, world.Progression))} |");
        report.AppendLine();
        report.AppendLine("## 状态摘要");
        report.AppendLine();
        report.AppendLine("```text");
        report.AppendLine(summary);
        report.AppendLine("```");
        report.AppendLine();
        report.AppendLine("## ValleyTalk 隐藏上下文预览");
        report.AppendLine();
        report.AppendLine("这段内容就是 LivingNPCs 会注入 ValleyTalk 的上下文，里面能看到本次召回了哪几条长期记忆、玩家偏好和社区印象。");
        report.AppendLine();
        report.AppendLine("```text");
        report.AppendLine(prompt.TrimEnd());
        report.AppendLine("```");
        return report.ToString();
    }

    public static string BuildStateOnlyMarkdownReport(LivingNpcState state)
    {
        var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
        var report = new StringBuilder();
        report.AppendLine($"# LivingNPCs 状态报告：{state.NpcName}");
        report.AppendLine();
        report.AppendLine("| 项目 | 值 |");
        report.AppendLine("| --- | --- |");
        report.AppendLine($"| 心情 | {EscapeTable(state.MoodLabel)} |");
        report.AppendLine($"| 人际情绪 | {EscapeTable(state.EmotionLabel)} |");
        report.AppendLine($"| 情绪表达风格 | {EscapeTable(emotionalStyle.DebugSummaryLabel)} |");
        report.AppendLine($"| 注意度 | {state.Attention}/100 |");
        report.AppendLine($"| 开放度 | {state.Openness}/100 |");
        report.AppendLine($"| 熟悉度 | {state.Familiarity}/100 |");
        report.AppendLine($"| 关系信任 | {EscapeTable(state.RelationshipTrustDebugLabel)} |");
        report.AppendLine($"| 互动节奏 | {EscapeTable(state.InteractionRhythmLabel)} |");
        report.AppendLine($"| 最近互动 | {EscapeTable(state.LastInteractionLabel)} |");
        report.AppendLine();
        report.AppendLine("## 关系与记忆");
        report.AppendLine();
        AppendBullet(report, "长期记忆", state.LongTermMemoryDebugLabel);
        AppendBullet(report, "玩家偏好", state.PlayerPreferenceDebugLabel);
        AppendBullet(report, "社区印象", state.CommunityImpressionDebugLabel);
        AppendBullet(report, "对话驱动行为", state.DialogueBehaviorInfluenceDebugLabel);
        AppendBullet(report, "长期约定", state.CommitmentDebugLabel);
        AppendBullet(report, "主动求助", state.HelpRequestDebugLabel);
        AppendBullet(report, "冲突记忆", state.ConflictDebugLabel);
        return report.ToString();
    }

    public static string BuildTrackedStateIndex(IEnumerable<LivingNpcState> states)
    {
        var orderedStates = states
            .OrderByDescending(state => state.LastUpdatedTotalDays)
            .ThenByDescending(state => state.LastUpdatedTimeOfDay)
            .ThenBy(state => state.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var report = new StringBuilder();
        report.AppendLine("# LivingNPCs 全局状态索引");
        report.AppendLine();
        if (orderedStates.Count == 0)
        {
            report.AppendLine("当前存档还没有记录任何 NPC 状态。");
            return report.ToString();
        }

        report.AppendLine("| NPC | 情绪 | 熟悉度 | 关系信任 | 待处理 | 最近互动 |");
        report.AppendLine("| --- | --- | ---: | ---: | --- | --- |");
        foreach (var state in orderedStates)
        {
            string pending = FormatPendingWork(state);
            report.AppendLine($"| {EscapeTable(state.NpcName)} | {EscapeTable(state.EmotionLabel)} | {state.Familiarity} | {state.RelationshipTrust} | {EscapeTable(pending)} | {EscapeTable(state.LastInteractionLabel)} |");
        }

        return report.ToString();
    }

    public static string RunRuntimeEvaluationSuite()
    {
        var results = new List<DiagnosticCheckResult>
        {
            CheckEmotionStyle("Flor", "ReflectiveCareful"),
            CheckEmotionStyle("Shane", "GuardedBlunt"),
            CheckEmotionStyle("Haley", "SharpQuick"),
            CheckEmotionStyle("Harvey", "PoliteAnxious"),
            CheckDispositionSource("Flor", "Ridgeside Village"),
            CheckDispositionSource("Claire", "Stardew Valley Expanded")
        };

        int passed = results.Count(result => result.Passed);
        var report = new StringBuilder();
        report.AppendLine($"LivingNPCs 运行时诊断：{passed}/{results.Count} 通过");
        foreach (var result in results)
        {
            report.AppendLine($"- {(result.Passed ? "OK" : "FAIL")} {result.Name}: {result.Detail}");
        }

        return report.ToString().TrimEnd();
    }

    private static DiagnosticCheckResult CheckEmotionStyle(string npcName, string expectedKey)
    {
        var cue = EmotionalExpressionStyle.For(npcName, NpcDisposition.ForName(npcName));
        return new DiagnosticCheckResult(
            $"情绪风格 {npcName}",
            string.Equals(cue.Key, expectedKey, StringComparison.Ordinal),
            $"expected {expectedKey}, actual {cue.Key}"
        );
    }

    private static DiagnosticCheckResult CheckDispositionSource(string npcName, string expectedSource)
    {
        var profile = NpcDisposition.ForName(npcName);
        return new DiagnosticCheckResult(
            $"角色来源 {npcName}",
            string.Equals(profile.SourceLabel, expectedSource, StringComparison.OrdinalIgnoreCase),
            $"expected {expectedSource}, actual {profile.SourceLabel}"
        );
    }

    private static string FormatPendingWork(LivingNpcState state)
    {
        int commitments = state.Commitments.Count(commitment => commitment.Status == "Pending");
        int requests = state.HelpRequests.Count(request => request.Status is "Offered" or "Pending");
        int conflicts = state.Conflicts.Count(conflict => conflict.Status is "Active" or "Recovering");
        var parts = new List<string>();
        if (commitments > 0)
        {
            parts.Add($"约定 {commitments}");
        }

        if (requests > 0)
        {
            parts.Add($"求助 {requests}");
        }

        if (conflicts > 0)
        {
            parts.Add($"冲突 {conflicts}");
        }

        return parts.Count == 0 ? "无" : string.Join("，", parts);
    }

    private static void AppendBullet(StringBuilder report, string label, string value)
    {
        report.AppendLine($"- **{label}**：{value}");
    }

    private static string EscapeTable(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "无"
            : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private sealed record DiagnosticCheckResult(string Name, bool Passed, string Detail);
}
