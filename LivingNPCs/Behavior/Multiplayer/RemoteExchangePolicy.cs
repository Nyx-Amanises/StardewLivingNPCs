using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>
/// 远程交换入账的纯策略（单测覆盖）。v1 只允许 farmhand 请求由主机权威裁决的礼物、
/// 金钱与物品型求助；出游、节日互动和未知世界动作在进入既有 RecordExchange 管道前
/// 剥除并交给调用方记 Trace。其余心智字段与求助字段保持原样，继续由现有解析器、
/// 白名单和上限规则做最终裁决。
/// </summary>
internal static class RemoteExchangePolicy
{
    private static readonly IReadOnlySet<string> AllowedRemoteWorldActions = new HashSet<string>(
        new[] { "give_small_gift", "give_meaningful_gift", "give_money" },
        StringComparer.Ordinal);

    /// <summary>
    /// 仅保留主机允许 farmhand 发起的礼物/金钱动作，返回净化后的 JSON 与被丢弃的原始
    /// 动作类型清单（供主机 Trace 记录）。helpRequests/helpRequestUpdates 与所有心智字段
    /// 不在这里改写；JSON 无效或非对象时原样返回（解析器自会安全兜底）。
    /// </summary>
    public static string SanitizeAnalysisJson(string analysisJson, out List<string> droppedActionTypes)
    {
        droppedActionTypes = new List<string>();
        if (string.IsNullOrWhiteSpace(analysisJson))
        {
            return analysisJson ?? string.Empty;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(analysisJson) as JsonObject;
        }
        catch (JsonException)
        {
            return analysisJson;
        }

        if (root == null)
        {
            return analysisJson;
        }

        bool changed = false;
        // JSON 属性名大小写敏感，但下游反序列化大小写不敏感；若恶意/畸形负载同时给出
        // actions/Actions，也必须逐个净化，不能只处理第一项让另一项绕过策略。
        foreach (string actualKey in root
                     .Select(pair => pair.Key)
                     .Where(key => string.Equals(key, "actions", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            if (root[actualKey] is not JsonArray actions)
            {
                root.Remove(actualKey);
                changed = true;
                continue;
            }

            var rejectedIndexes = new List<int>();
            for (int index = 0; index < actions.Count; index++)
            {
                JsonNode? action = actions[index];
                string rawType = string.Empty;
                if (action is not JsonObject actionObject
                    || !TryReadUnambiguousActionType(actionObject, out rawType)
                    || !AllowedRemoteWorldActions.Contains(BehaviorValueNormalizer.NormalizeWorldActionType(rawType)))
                {
                    rejectedIndexes.Add(index);
                    if (!string.IsNullOrWhiteSpace(rawType))
                    {
                        droppedActionTypes.Add(rawType);
                    }
                }
            }

            for (int index = rejectedIndexes.Count - 1; index >= 0; index--)
            {
                actions.RemoveAt(rejectedIndexes[index]);
            }

            changed |= rejectedIndexes.Count > 0;
        }

        return changed ? root.ToJsonString() : analysisJson;
    }

    private static bool TryReadUnambiguousActionType(JsonObject action, out string rawType)
    {
        rawType = string.Empty;
        List<JsonNode?> typeNodes = action
            .Where(pair => string.Equals(pair.Key, "type", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .ToList();
        if (typeNodes.Count != 1
            || typeNodes[0] is not JsonValue typeValue
            || !typeValue.TryGetValue(out string? value)
            || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        rawType = value.Trim();
        return true;
    }

}
