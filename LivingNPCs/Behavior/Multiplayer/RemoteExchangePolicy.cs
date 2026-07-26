using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>farmhand 记忆手册打开时机的纯决策。</summary>
internal enum FarmhandBookAction
{
    /// <summary>快照未到且未超时：继续等。</summary>
    Wait,

    /// <summary>快照已到：用新数据开书。</summary>
    OpenFresh,

    /// <summary>超时但镜像里有旧数据：先开旧数据并提示。</summary>
    OpenStaleWithNotice,

    /// <summary>超时且镜像为空：放弃并提示占位。</summary>
    GiveUpWithNotice
}

/// <summary>
/// 远程交换入账的纯策略（单测覆盖）。v1 裁决：farmhand 的交换只入"心智账本"
/// （记忆/偏好/冲突/情绪/行为倾向/好感增量），世界动作（出游/送礼/送钱/节日互动）与
/// 求助请求全部剥除——它们的执行副作用（NPC 寻路、物品与金钱、任务栏）属于主机玩家
/// 自己的会话；主机收到 farmhand 的这类请求直接丢弃并记 Trace。
/// </summary>
internal static class RemoteExchangePolicy
{
    /// <summary>剥除的顶层字段（大小写不敏感，与 ValleyTalkExchangeParser 的读取口径一致）。</summary>
    private static readonly string[] StrippedFields = { "actions", "helpRequests", "helpRequestUpdates" };

    /// <summary>
    /// 从分析 JSON 中剥掉世界动作与求助字段，返回净化后的 JSON 与被丢弃的动作类型清单
    /// （供主机 Trace 记录，出游即在其中）。JSON 无效或非对象时原样返回（解析器自会兜底）。
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
        foreach (string field in StrippedFields)
        {
            string? actualKey = root
                .Select(pair => pair.Key)
                .FirstOrDefault(key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
            if (actualKey == null)
            {
                continue;
            }

            if (string.Equals(field, "actions", StringComparison.OrdinalIgnoreCase)
                && root[actualKey] is JsonArray actions)
            {
                foreach (JsonNode? action in actions)
                {
                    if (action is not JsonObject actionObject)
                    {
                        continue;
                    }

                    JsonNode? typeNode = actionObject
                        .FirstOrDefault(pair => string.Equals(pair.Key, "type", StringComparison.OrdinalIgnoreCase))
                        .Value;
                    if (typeNode is JsonValue typeValue
                        && typeValue.TryGetValue(out string? type)
                        && !string.IsNullOrWhiteSpace(type))
                    {
                        droppedActionTypes.Add(type.Trim());
                    }
                }
            }

            root.Remove(actualKey);
            changed = true;
        }

        return changed ? root.ToJsonString() : analysisJson;
    }

    /// <summary>farmhand 开书决策：快照到达即开新；未超时继续等；超时按镜像有无降级。</summary>
    public static FarmhandBookAction PlanBookOpen(bool snapshotArrived, bool timedOut, bool mirrorHasData)
    {
        if (snapshotArrived)
        {
            return FarmhandBookAction.OpenFresh;
        }

        if (!timedOut)
        {
            return FarmhandBookAction.Wait;
        }

        return mirrorHasData ? FarmhandBookAction.OpenStaleWithNotice : FarmhandBookAction.GiveUpWithNotice;
    }
}
