using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>
/// 台词展示记录器（裁决 1 方案 B：P11 在真正显示前记录）。日常/事件两类写入 +
/// 同地图 4.5 格内可收礼旁听；以（NPC × 游戏日 × 文本组）去重，等效替代旧世界的
/// IL 偏移启发式（§4.2.2 第 4 步）。
/// </summary>
internal static class DisplayedDialogueRecorder
{
    private const float OverhearTileRadius = 4.5f;

    private static readonly object gate = new();
    private static readonly HashSet<string> recordedToday = new(StringComparer.Ordinal);
    private static int memoDay = -1;

    /// <summary>测试注入的游戏日戳；null 走 Game1。</summary>
    internal static Func<int>? DayStampForTests;

    /// <summary>清空当日去重表（存档切换/回标题时由 Bootstrapper 调用）。</summary>
    public static void Reset()
    {
        lock (gate)
        {
            recordedToday.Clear();
            memoDay = -1;
        }
    }

    internal static void ResetForTests()
    {
        Reset();
        DayStampForTests = null;
    }

    public static void Record(NPC speaker, GameDialogue dialogue)
    {
        var engine = DialogueEngineHost.Instance;
        if (engine == null || speaker == null)
        {
            return;
        }

        List<string> lines = ExtractRecordableLines(dialogue.dialogues?.Select(line => line?.Text));
        if (lines.Count == 0)
        {
            return;
        }

        if (!TryMarkRecorded(speaker.Name, DayStamp(), lines))
        {
            return;
        }

        StardewTime now = CurrentTime();
        Event? currentEvent = TryGetCurrentEvent();
        if (currentEvent != null)
        {
            var listeners = currentEvent.actors?
                .Where(actor => !string.IsNullOrWhiteSpace(actor?.Name))
                .Select(actor => actor!.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? new List<string>();
            engine.History.AddEventLine(speaker.Name, currentEvent.FestivalName ?? string.Empty, lines, listeners, now);
        }
        else
        {
            engine.History.AddDialogueLine(speaker.Name, lines, now);
        }

        RecordOverheard(engine, speaker, lines, now);
    }

    /// <summary>可记录的行（纯函数）：非空白、非占位标记、非跳过标记行。</summary>
    internal static List<string> ExtractRecordableLines(IEnumerable<string?>? lines)
    {
        var result = new List<string>();
        foreach (string? line in lines ?? Enumerable.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, EngineConstants.DialogueGenerationTag, StringComparison.Ordinal)
                || line.StartsWith(EngineConstants.DialogueSkipTag, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    /// <summary>（NPC × 游戏日 × 文本组）当日去重；游戏日变更时清空记忆表。</summary>
    internal static bool TryMarkRecorded(string npcName, int day, IReadOnlyList<string> lines)
    {
        string key = $"{npcName}{string.Join("", lines)}";
        lock (gate)
        {
            if (day != memoDay)
            {
                recordedToday.Clear();
                memoDay = day;
            }

            return recordedToday.Add(key);
        }
    }

    private static void RecordOverheard(DialogueEngine engine, NPC speaker, List<string> lines, StardewTime now)
    {
        try
        {
            var location = speaker.currentLocation;
            if (location?.characters == null)
            {
                return;
            }

            foreach (NPC bystander in location.characters.ToList())
            {
                if (bystander == null
                    || ReferenceEquals(bystander, speaker)
                    || string.Equals(bystander.Name, speaker.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!bystander.CanReceiveGifts())
                {
                    continue;
                }

                if (Vector2.Distance(bystander.Tile, speaker.Tile) > OverhearTileRadius)
                {
                    continue;
                }

                engine.History.AddOverheardLine(bystander.Name, speaker.Name, lines, now);
            }
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.overheardRecordFailed", new { npc = speaker.Name, error = ex.Message }),
                StardewModdingAPI.LogLevel.Trace);
        }
    }

    private static Event? TryGetCurrentEvent()
    {
        try
        {
            return Game1.currentLocation?.currentEvent;
        }
        catch
        {
            return null;
        }
    }

    private static StardewTime CurrentTime()
    {
        try
        {
            return new StardewTime(Game1.year, Game1.season, Game1.dayOfMonth, Game1.timeOfDay);
        }
        catch
        {
            return new StardewTime(1, Season.Spring, 1, 600);
        }
    }

    private static int DayStamp()
    {
        Func<int>? stamp = DayStampForTests;
        if (stamp != null)
        {
            return stamp();
        }

        try
        {
            return (Game1.year * 112 * 4) + (Game1.seasonIndex * 28) + Game1.dayOfMonth;
        }
        catch
        {
            return 0;
        }
    }
}
