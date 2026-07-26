using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>What to do with one pending ambient remark on this update tick.</summary>
internal enum AmbientRemarkTickPlan
{
    /// <summary>The remark is from another day; drop it.</summary>
    Drop,

    /// <summary>Its conditions are not met yet; keep it pending.</summary>
    Wait,

    Show
}

internal sealed class BehaviorFeedbackService
{
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly List<PendingAmbientRemark> pendingAmbientRemarks = new();
    private readonly Queue<string> pendingHudMessages = new();
    private readonly Dictionary<string, double> nextSpeechBubbleTimeByNpc = new(StringComparer.OrdinalIgnoreCase);

    public BehaviorFeedbackService(ModConfig config, IMonitor monitor)
    {
        this.config = config;
        this.monitor = monitor;
    }

    public void Clear()
    {
        this.pendingAmbientRemarks.Clear();
        this.pendingHudMessages.Clear();
        this.nextSpeechBubbleTimeByNpc.Clear();
    }

    public void QueueAmbientRemark(NPC npc, string text, int delayMinutes)
    {
        this.pendingAmbientRemarks.RemoveAll(remark => remark.NpcName == npc.Name);
        this.pendingAmbientRemarks.Add(new PendingAmbientRemark(
            npc.Name,
            text.Trim(),
            Game1.Date.TotalDays,
            BehaviorTimeMath.AddMinutesToTime(Game1.timeOfDay, Math.Clamp(delayMinutes, 0, 120)),
            npc.currentLocation?.Name ?? string.Empty,
            npc.Tile
        ));
    }

    public void ClearAmbientRemarksForNpc(string npcName)
    {
        this.pendingAmbientRemarks.RemoveAll(remark => string.Equals(remark.NpcName, npcName, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryShowNpcSpeechBubble(NPC npc, string text, int? cooldownMilliseconds = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d;
        if (this.nextSpeechBubbleTimeByNpc.TryGetValue(npc.Name, out double nextAllowed) && now < nextAllowed)
        {
            return false;
        }

        int durationMilliseconds = GetSpeechBubbleDurationMilliseconds(text);
        npc.showTextAboveHead(text, null, 2, durationMilliseconds, 0);
        this.nextSpeechBubbleTimeByNpc[npc.Name] = now + Math.Max(durationMilliseconds, cooldownMilliseconds ?? durationMilliseconds);
        return true;
    }

    public void TryShowPendingAmbientRemarks()
    {
        if (!this.config.EnableDialogueFollowUps
            || this.pendingAmbientRemarks.Count == 0
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var remark in this.pendingAmbientRemarks.ToList())
        {
            NPC? npc = Game1.currentLocation?.characters.FirstOrDefault(candidate => candidate.Name == remark.NpcName);
            float? distanceToPlayer = npc != null && Game1.player != null
                ? Vector2.Distance(npc.Tile, Game1.player.Tile)
                : null;
            var plan = PlanRemarkTick(
                remark.TotalDays,
                remark.NotBeforeTimeOfDay,
                remark.LocationName,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                npc != null,
                npc?.currentLocation?.Name,
                distanceToPlayer,
                this.config.MaxInteractionDistanceTiles);
            if (plan == AmbientRemarkTickPlan.Drop)
            {
                this.pendingAmbientRemarks.Remove(remark);
                continue;
            }

            if (plan != AmbientRemarkTickPlan.Show || npc == null)
            {
                continue;
            }

            if (!this.TryShowNpcSpeechBubble(npc, remark.Text))
            {
                continue;
            }

            this.pendingAmbientRemarks.Remove(remark);

            if (this.config.Debug)
            {
                this.monitor.Log(I18n.Get("log.feedback.ambientFollowUp", new { npc = npc.Name, text = remark.Text }), LogLevel.Debug);
            }
        }
    }

    /// <summary>
    /// Per-remark visibility decision, extracted for tests. Deliberately does NOT compare the
    /// NPC's tile with the tile the remark was queued from (PendingAmbientRemark.OriginTile is
    /// informational only): counter NPCs such as Gus, Clint, Willy, or Penny stand on one tile
    /// for hours, and the old "wait until the NPC left the original tile" condition silently
    /// swallowed their delay-0 help-request thanks and dialogue follow-ups until the day change
    /// dropped them unseen.
    /// </summary>
    internal static AmbientRemarkTickPlan PlanRemarkTick(
        int remarkTotalDays,
        int remarkNotBeforeTimeOfDay,
        string remarkLocationName,
        int currentTotalDays,
        int currentTimeOfDay,
        bool npcFound,
        string? npcLocationName,
        float? distanceToPlayerTiles,
        float maxDistanceTiles)
    {
        if (remarkTotalDays != currentTotalDays)
        {
            return AmbientRemarkTickPlan.Drop;
        }

        if (currentTimeOfDay < remarkNotBeforeTimeOfDay
            || !npcFound
            || !string.Equals(npcLocationName, remarkLocationName, StringComparison.Ordinal)
            || distanceToPlayerTiles == null
            || distanceToPlayerTiles > maxDistanceTiles)
        {
            return AmbientRemarkTickPlan.Wait;
        }

        return AmbientRemarkTickPlan.Show;
    }

    public void Show(string message)
    {
        if (!this.config.ShowHudMessages)
        {
            return;
        }

        Game1.addHUDMessage(HUDMessage.ForCornerTextbox(message));
    }

    public void ShowAfterDialogue(string message)
    {
        if (!this.config.ShowHudMessages || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (Game1.activeClickableMenu == null)
        {
            this.Show(message);
            return;
        }

        this.pendingHudMessages.Enqueue(message);
    }

    public void TryShowPendingHudMessages()
    {
        if (this.pendingHudMessages.Count == 0 || Game1.activeClickableMenu != null)
        {
            return;
        }

        this.Show(this.pendingHudMessages.Dequeue());
    }

    private static int GetSpeechBubbleDurationMilliseconds(string text)
    {
        int visibleLength = (text ?? string.Empty).Trim().Length;
        return Math.Clamp(4000 + Math.Max(0, visibleLength - 8) * 140, 4000, 10000);
    }
}
