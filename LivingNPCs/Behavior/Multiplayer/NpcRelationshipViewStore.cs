using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>
/// farmhand 的主机关系视图缓存。入库和导出都克隆状态，调用方只能读取快照，不能把
/// farmhand 本地变化写回主机心智或污染后续提示词。
/// </summary>
internal sealed class NpcRelationshipViewStore
{
    private readonly Dictionary<string, NpcRelationshipViewMessage> views = new(StringComparer.OrdinalIgnoreCase);

    public int Count => this.views.Count;

    public void Apply(NpcRelationshipViewMessage? view)
    {
        if (view == null || string.IsNullOrWhiteSpace(view.NpcName))
        {
            return;
        }

        this.views[view.NpcName] = Clone(view);
    }

    public bool TryGet(string npcName, out NpcRelationshipViewMessage? view)
    {
        if (this.views.TryGetValue(npcName, out NpcRelationshipViewMessage? stored))
        {
            view = Clone(stored);
            return true;
        }

        view = null;
        return false;
    }

    public bool TryGetBehaviorContext(string npcName, out string context)
    {
        if (this.views.TryGetValue(npcName, out NpcRelationshipViewMessage? view))
        {
            context = view.BehaviorContextSummary ?? string.Empty;
            return true;
        }

        context = string.Empty;
        return false;
    }

    public List<LivingNpcState> ExportBookStates()
    {
        return this.views.Values
            .Where(view => view.BookState != null)
            .Select(view => view.BookState!.Clone())
            .ToList();
    }

    public void RetainOnly(IReadOnlySet<string> npcNames)
    {
        foreach (string key in this.views.Keys.Where(key => !npcNames.Contains(key)).ToList())
        {
            this.views.Remove(key);
        }
    }

    public void Clear()
    {
        this.views.Clear();
    }

    private static NpcRelationshipViewMessage Clone(NpcRelationshipViewMessage view)
    {
        return new NpcRelationshipViewMessage
        {
            SchemaVersion = view.SchemaVersion,
            NpcName = view.NpcName,
            BehaviorContextSummary = view.BehaviorContextSummary ?? string.Empty,
            BookState = view.BookState?.Clone(),
            UpdatedTotalDays = view.UpdatedTotalDays,
            UpdatedTimeOfDay = view.UpdatedTimeOfDay
        };
    }
}
