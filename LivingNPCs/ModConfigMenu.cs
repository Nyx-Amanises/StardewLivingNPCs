using GenericModConfigMenu;
using StardewModdingAPI;

namespace LivingNPCs;

internal static class ModConfigMenu
{
    private static readonly string[] ManualModes =
    [
        "Auto",
        "FacePlayer",
        "Emote",
        "ApproachPlayer",
        "Pause",
        "LookAround",
        "StepAway"
    ];

    public static void Register(ModEntry modEntry, ModConfig config)
    {
        var configMenu = modEntry.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu == null)
        {
            modEntry.Monitor.Log("Generic Mod Config Menu not installed. LivingNPCs will still work, but no in-game config menu will be shown.", LogLevel.Info);
            return;
        }

        var manifest = modEntry.ModManifest;
        configMenu.Register(
            mod: manifest,
            reset: config.ResetToDefaults,
            save: () => modEntry.Helper.WriteConfig(config)
        );

        configMenu.AddParagraph(
            mod: manifest,
            text: () => "LivingNPCs 已加载。能看到这个页面，就说明 Mod 正在运行。"
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "基础设置"
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "启用 LivingNPCs",
            tooltip: () => "修改后需要重启游戏才会完全生效。",
            getValue: () => config.EnableMod,
            setValue: value => config.EnableMod = value
        );

        configMenu.AddKeybindList(
            mod: manifest,
            name: () => "行为快捷键",
            tooltip: () => "靠近 NPC 时按下这个键，会触发一次小型 NPC 行为。",
            getValue: () => config.BehaviorHotkey,
            setValue: value => config.BehaviorHotkey = value
        );

        configMenu.AddKeybindList(
            mod: manifest,
            name: () => "查看记忆快捷键",
            tooltip: () => "靠近 NPC 时按下这个键，会把该 NPC 的 LivingNPCs 行为记忆输出到 SMAPI 控制台。",
            getValue: () => config.InspectMemoryHotkey,
            setValue: value => config.InspectMemoryHotkey = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "调试日志",
            tooltip: () => "在 SMAPI 控制台输出更多 LivingNPCs 调试信息。",
            getValue: () => config.Debug,
            setValue: value => config.Debug = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "显示触发提示",
            tooltip: () => "手动触发后在左下角显示执行结果或失败原因，方便判断是否生效。",
            getValue: () => config.ShowHudMessages,
            setValue: value => config.ShowHudMessages = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "手动测试"
        );

        configMenu.AddTextOption(
            mod: manifest,
            name: () => "手动行为模式",
            tooltip: () => "选择按快捷键时固定测试哪种行为。自动模式会按规则选择。",
            getValue: () => config.ManualBehaviorMode,
            setValue: value => config.ManualBehaviorMode = value,
            allowedValues: ManualModes,
            formatAllowedValue: FormatManualMode
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "手动表情编号",
            tooltip: () => "当手动行为模式为显示表情时使用。16 通常是感叹号。",
            getValue: () => config.ManualEmoteId,
            setValue: value => config.ManualEmoteId = value,
            min: 0,
            max: 40,
            interval: 1
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "记忆设置"
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "每个 NPC 保留记忆数",
            tooltip: () => "每个 NPC 最多保存多少条 LivingNPCs 行为记忆到存档。",
            getValue: () => config.MaxMemoryEntriesPerNpc,
            setValue: value => config.MaxMemoryEntriesPerNpc = value,
            min: 1,
            max: 100,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "发送给 ValleyTalk 的记忆数",
            tooltip: () => "每次生成上下文时，最多把最近多少条行为记忆发送给 ValleyTalk。",
            getValue: () => config.PromptMemoryEntries,
            setValue: value => config.PromptMemoryEntries = value,
            min: 1,
            max: 20,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "记录对话启动",
            tooltip: () => "点击 NPC 开始聊天时，记录一条轻量互动记忆，并提前发送上下文给 ValleyTalk。",
            getValue: () => config.EnableConversationMemory,
            setValue: value => config.EnableConversationMemory = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "AI 对话额外好感",
            tooltip: () => "让 AI 对话按聊天质量在原版对话好感之外额外增加好感，每个 NPC 每天受上限约束。",
            getValue: () => config.EnableAiDialogueFriendship,
            setValue: value => config.EnableAiDialogueFriendship = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "AI 对话每日额外好感上限",
            tooltip: () => "每个 NPC 每天最多通过 AI 对话额外获得多少好感。原版对话好感不计入这个上限。",
            getValue: () => config.MaxAiDialogueFriendshipPerNpcPerDay,
            setValue: value => config.MaxAiDialogueFriendshipPerNpcPerDay = value,
            min: 0,
            max: 30,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "对话后续气泡",
            tooltip: () => "允许 AI 对话在合适时留下一个短暂的后续头顶气泡，例如约好同行后边走边补上一句。",
            getValue: () => config.EnableDialogueFollowUps,
            setValue: value => config.EnableDialogueFollowUps = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "AI 影响世界"
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许 AI 触发世界动作",
            tooltip: () => "允许 AI 在严格限制下触发少量实际效果，例如小礼物、少量金钱、浇水或短暂同行。",
            getValue: () => config.EnableAiWorldActions,
            setValue: value => config.EnableAiWorldActions = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许 NPC 送小礼物",
            tooltip: () => "只有关系已有一点熟悉时，NPC 才可能偶尔给玩家一个低价值小礼物。",
            getValue: () => config.AllowAiSmallGifts,
            setValue: value => config.AllowAiSmallGifts = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许 NPC 送钱",
            tooltip: () => "只有关系较熟时，NPC 才可能偶尔给玩家少量金钱。",
            getValue: () => config.AllowAiMoneyGifts,
            setValue: value => config.AllowAiMoneyGifts = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "单次送钱上限",
            tooltip: () => "每次 AI 触发送钱动作时，最多给玩家多少金币。",
            getValue: () => config.MaxAiMoneyGiftAmount,
            setValue: value => config.MaxAiMoneyGiftAmount = value,
            min: 25,
            max: 1000,
            interval: 25
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许 NPC 帮忙浇水",
            tooltip: () => "只有在农场且关系较熟时，NPC 才可能给附近已种植但未浇水的作物浇水。",
            getValue: () => config.AllowAiFarmHelp,
            setValue: value => config.AllowAiFarmHelp = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "单次最多浇水格数",
            tooltip: () => "一次 AI 帮忙浇水最多影响多少格附近作物。",
            getValue: () => config.MaxAiWateredTilesPerAction,
            setValue: value => config.MaxAiWateredTilesPerAction = value,
            min: 1,
            max: 24,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许 NPC 短暂同行",
            tooltip: () => "只有关系已有一点熟悉且 NPC 当前空闲时，AI 才可能让 NPC 短时间陪玩家同行。",
            getValue: () => config.AllowAiWalkTogether,
            setValue: value => config.AllowAiWalkTogether = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "同行最长时长",
            tooltip: () => "一次 AI 同行最多持续多少游戏分钟。",
            getValue: () => config.MaxAiWalkTogetherMinutes,
            setValue: value => config.MaxAiWalkTogetherMinutes = value,
            min: 5,
            max: 60,
            interval: 5
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "NPC 状态"
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "启用 NPC 状态",
            tooltip: () => "记录每个 NPC 的轻量当前状态，例如心情、注意度和回应倾向。",
            getValue: () => config.EnableNpcState,
            setValue: value => config.EnableNpcState = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "每日状态衰减",
            tooltip: () => "每天开始时，NPC 状态向普通状态回落的幅度。",
            getValue: () => config.NpcStateDailyDecay,
            setValue: value => config.NpcStateDailyDecay = value,
            min: 0,
            max: 50,
            interval: 1
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "行为设置"
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "被动行为",
            tooltip: () => "允许附近 NPC 在不按快捷键时偶尔自然反应。",
            getValue: () => config.EnablePassiveBehaviors,
            setValue: value => config.EnablePassiveBehaviors = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "被动触发概率",
            tooltip: () => "每 10 分钟游戏时间触发一次被动行为的概率。",
            getValue: () => config.PassiveBehaviorChancePercent,
            setValue: value => config.PassiveBehaviorChancePercent = value,
            min: 0,
            max: 100,
            interval: 1,
            formatValue: value => $"{value}%"
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "每个 NPC 每日最多行为次数",
            tooltip: () => "限制每个 NPC 每天最多被 LivingNPCs 触发多少次行为。",
            getValue: () => config.MaxBehaviorsPerNpcPerDay,
            setValue: value => config.MaxBehaviorsPerNpcPerDay = value,
            min: 0,
            max: 20,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "互动距离",
            tooltip: () => "LivingNPCs 能影响附近 NPC 的最大距离，单位是格子。",
            getValue: () => config.MaxInteractionDistanceTiles,
            setValue: value => config.MaxInteractionDistanceTiles = value,
            min: 1,
            max: 20,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许转向玩家",
            tooltip: () => "允许 NPC 朝向玩家。",
            getValue: () => config.AllowFacePlayer,
            setValue: value => config.AllowFacePlayer = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许表情",
            tooltip: () => "允许 NPC 显示表情气泡。",
            getValue: () => config.AllowEmotes,
            setValue: value => config.AllowEmotes = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "允许走近玩家",
            tooltip: () => "允许 NPC 短距离寻路到玩家旁边的安全格子。",
            getValue: () => config.AllowApproachPlayer,
            setValue: value => config.AllowApproachPlayer = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "ValleyTalk 集成"
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "发送行为上下文到 ValleyTalk",
            tooltip: () => "让 ValleyTalk 的对话生成参考 NPC 最近的 LivingNPCs 行为。",
            getValue: () => config.EnableValleyTalkPromptBridge,
            setValue: value => config.EnableValleyTalkPromptBridge = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => "AI 行为规划"
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => "启用 AI 行为规划",
            tooltip: () => "可选功能。使用 OpenAI-compatible 接口来选择 NPC 行为意图。",
            getValue: () => config.EnableAiPlanner,
            setValue: value => config.EnableAiPlanner = value
        );

        configMenu.AddTextOption(
            mod: manifest,
            name: () => "规划接口地址",
            tooltip: () => "OpenAI-compatible /v1/chat/completions 接口地址。",
            getValue: () => config.AiPlannerEndpoint,
            setValue: value => config.AiPlannerEndpoint = value
        );

        configMenu.AddTextOption(
            mod: manifest,
            name: () => "规划模型",
            tooltip: () => "用于选择 NPC 行为意图的模型名称。",
            getValue: () => config.AiPlannerModel,
            setValue: value => config.AiPlannerModel = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => "规划超时时间",
            tooltip: () => "AI 行为规划超时后，会回退到规则行为。单位：秒。",
            getValue: () => config.AiPlannerTimeoutSeconds,
            setValue: value => config.AiPlannerTimeoutSeconds = value,
            min: 1,
            max: 60,
            interval: 1,
            formatValue: value => $"{value}s"
        );
    }

    private static string FormatManualMode(string value)
    {
        return value switch
        {
            "Auto" => "自动选择",
            "FacePlayer" => "只测试：转向玩家",
            "Emote" => "只测试：显示表情",
            "ApproachPlayer" => "只测试：走近玩家",
            "Pause" => "只测试：停下看向玩家",
            "LookAround" => "只测试：环顾四周",
            "StepAway" => "只测试：后退一步",
            _ => value
        };
    }
}
