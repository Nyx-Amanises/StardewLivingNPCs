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
        report.AppendLine(I18n.Get("debug.report.npcTitle", new { npc = npc.displayName }));
        report.AppendLine();
        report.AppendLine(I18n.Get("debug.report.tableHeader"));
        report.AppendLine("| --- | --- |");
        report.AppendLine($"| NPC | {EscapeTable(npc.Name)} / {EscapeTable(npc.displayName)} |");
        AppendTableRow(report, "debug.label.location", world.LocationDisplayName);
        AppendTableRow(report, "debug.label.dateTime", I18n.Get("debug.report.dateTimeValue", new { season = world.Season, day = world.DayOfMonth, time = world.TimeOfDay }));
        AppendTableRow(report, "debug.label.scene", world.DebugLabel);
        AppendTableRow(report, "debug.label.worldProgression", world.Progression.DebugLabel);
        AppendTableRow(report, "debug.label.npcKnowledge", world.ProgressionKnowledge.DebugLabel);
        AppendTableRow(report, "debug.label.helpRequestFit", HelpRequestAdvisor.BuildDebugLabel(npc, world.Progression));
        report.AppendLine();
        report.AppendLine(I18n.Get("debug.report.stateSummaryHeading"));
        report.AppendLine();
        report.AppendLine("```text");
        report.AppendLine(summary);
        report.AppendLine("```");
        report.AppendLine();
        report.AppendLine(I18n.Get("debug.report.promptPreviewHeading"));
        report.AppendLine();
        report.AppendLine(I18n.Get("debug.report.promptPreviewDescription"));
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
        report.AppendLine(I18n.Get("debug.report.stateTitle", new { npc = state.NpcName }));
        report.AppendLine();
        report.AppendLine(I18n.Get("debug.report.tableHeader"));
        report.AppendLine("| --- | --- |");
        AppendTableRow(report, "debug.label.mood", state.MoodLabel);
        AppendTableRow(report, "debug.label.interpersonalEmotion", state.EmotionLabel);
        AppendTableRow(report, "debug.label.emotionStyle", emotionalStyle.DebugSummaryLabel);
        AppendTableRow(report, "debug.label.attention", $"{state.Attention}/100");
        AppendTableRow(report, "debug.label.openness", $"{state.Openness}/100");
        AppendTableRow(report, "debug.label.familiarity", $"{state.Familiarity}/100");
        AppendTableRow(report, "debug.label.relationshipTrust", state.RelationshipTrustDebugLabel);
        AppendTableRow(report, "debug.label.interactionRhythm", state.InteractionRhythmLabel);
        AppendTableRow(report, "debug.label.lastInteraction", state.LastInteractionLabel);
        report.AppendLine();
        report.AppendLine(I18n.Get("debug.report.relationshipMemoryHeading"));
        report.AppendLine();
        AppendBullet(report, "debug.label.longTermMemory", state.LongTermMemoryDebugLabel);
        AppendBullet(report, "debug.label.playerPreferenceMemory", state.PlayerPreferenceDebugLabel);
        AppendBullet(report, "debug.label.communityImpression", state.CommunityImpressionDebugLabel);
        AppendBullet(report, "debug.label.dialogueBehavior", state.DialogueBehaviorInfluenceDebugLabel);
        AppendBullet(report, "debug.label.helpRequests", state.HelpRequestDebugLabel);
        AppendBullet(report, "debug.label.conflictMemory", state.ConflictDebugLabel);
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
        report.AppendLine(I18n.Get("debug.report.indexTitle"));
        report.AppendLine();
        if (orderedStates.Count == 0)
        {
            report.AppendLine(I18n.Get("debug.report.indexEmpty"));
            return report.ToString();
        }

        report.AppendLine(I18n.Get("debug.report.indexHeader"));
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
            CheckEmotionStyle("Shane", "GuardedBlunt"),
            CheckEmotionStyle("Haley", "SharpQuick"),
            CheckEmotionStyle("Harvey", "PoliteAnxious"),
            CheckDispositionSource("Claire", "Stardew Valley Expanded")
        };

        int passed = results.Count(result => result.Passed);
        var report = new StringBuilder();
        report.AppendLine(I18n.Get("debug.eval.summary", new { passed, total = results.Count }));
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
            I18n.Get("debug.eval.emotionStyle", new { npc = npcName }),
            string.Equals(cue.Key, expectedKey, StringComparison.Ordinal),
            I18n.Get("debug.eval.expectedActual", new { expected = expectedKey, actual = cue.Key })
        );
    }

    private static DiagnosticCheckResult CheckDispositionSource(string npcName, string expectedSource)
    {
        var profile = NpcDisposition.ForName(npcName);
        return new DiagnosticCheckResult(
            I18n.Get("debug.eval.profileSource", new { npc = npcName }),
            string.Equals(profile.SourceLabel, expectedSource, StringComparison.OrdinalIgnoreCase),
            I18n.Get("debug.eval.expectedActual", new { expected = expectedSource, actual = profile.SourceLabel })
        );
    }

    private static string FormatPendingWork(LivingNpcState state)
    {
        int requests = state.HelpRequests.Count(request => request.Status is "Offered" or "Pending");
        int conflicts = state.Conflicts.Count(conflict => conflict.Status is "Active" or "Recovering");
        var parts = new List<string>();
        if (requests > 0)
        {
            parts.Add(I18n.Get("debug.pending.helpRequests", new { count = requests }));
        }

        if (conflicts > 0)
        {
            parts.Add(I18n.Get("debug.pending.conflicts", new { count = conflicts }));
        }

        return parts.Count == 0 ? I18n.Get("debug.none") : string.Join(I18n.Get("debug.listSeparator"), parts);
    }

    private static void AppendBullet(StringBuilder report, string labelKey, string value)
    {
        report.AppendLine(I18n.Get("debug.report.bullet", new { label = I18n.Get(labelKey), value }));
    }

    private static void AppendTableRow(StringBuilder report, string labelKey, string value)
    {
        report.AppendLine($"| {EscapeTable(I18n.Get(labelKey))} | {EscapeTable(value)} |");
    }

    private static string EscapeTable(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? I18n.Get("debug.none")
            : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private sealed record DiagnosticCheckResult(string Name, bool Passed, string Detail);
}
