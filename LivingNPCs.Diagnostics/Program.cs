using System.Text;

string root = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? Path.GetFullPath(args[0])
    : FindRepositoryRoot(AppContext.BaseDirectory);

if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
{
    Console.Error.WriteLine("无法找到 StardewLivingNPCs 仓库根目录。可以传入路径作为第一个参数。");
    Environment.Exit(2);
    return;
}

var tests = new List<RegressionCheck>
{
    CheckFileContains(
        "状态摘要包含记忆召回原因",
        Path.Combine(root, "LivingNPCs", "Behavior", "BehaviorMemory.cs"),
        [
            "当前检索长期记忆",
            "当前检索玩家偏好",
            "当前检索社区印象",
            "最近行为选择"
        ]
    ),
    CheckFileContains(
        "行为选择原因仍被记录",
        Path.Combine(root, "LivingNPCs", "Behavior", "RuleBasedBehaviorPlanner.cs"),
        [
            "their current context made them more willing to step closer",
            "unresolved conflict",
            "recent conversation invited closeness",
            "repeated chat with low relationship"
        ]
    ),
    CheckFileContains(
        "求助生成原因可调试",
        Path.Combine(root, "LivingNPCs", "Behavior", "HelpRequestAdvisor.cs"),
        [
            "BuildDebugLabel",
            "候选物品",
            "候选问题",
            "深度"
        ]
    ),
    CheckFileContains(
        "关键 NPC 情绪表达风格未丢失",
        Path.Combine(root, "LivingNPCs", "Behavior", "EmotionalExpressionStyle.cs"),
        [
            "\"Flor\"",
            "\"Shane\"",
            "\"Haley\"",
            "\"Harvey\"",
            "ReflectiveCareful",
            "GuardedBlunt",
            "SharpQuick",
            "PoliteAnxious"
        ]
    ),
    CheckFileContains(
        "游戏内调试命令仍存在",
        Path.Combine(root, "LivingNPCs", "Behavior", "BehaviorEngine.cs"),
        [
            "livingnpcs_debug",
            "livingnpcs_prompt",
            "livingnpcs_export",
            "livingnpcs_eval"
        ]
    ),
    CheckFileContains(
        "README 记录调试工具",
        Path.Combine(root, "README.md"),
        [
            "调试与评估工具",
            "livingnpcs_debug",
            "livingnpcs_export",
            "LivingNPCs.Diagnostics"
        ]
    )
};

int passed = tests.Count(test => test.Passed);
var report = new StringBuilder();
report.AppendLine($"LivingNPCs 离线回归检查：{passed}/{tests.Count} 通过");
foreach (var test in tests)
{
    report.AppendLine($"- {(test.Passed ? "OK" : "FAIL")} {test.Name}: {test.Detail}");
}

Console.WriteLine(report.ToString().TrimEnd());
if (passed != tests.Count)
{
    Environment.Exit(1);
}

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "LivingNPCs", "Behavior", "BehaviorMemory.cs")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return string.Empty;
}

static RegressionCheck CheckFileContains(string name, string path, IReadOnlyCollection<string> requiredSnippets)
{
    if (!File.Exists(path))
    {
        return new RegressionCheck(name, false, $"文件不存在：{path}");
    }

    string text = File.ReadAllText(path);
    var missing = requiredSnippets
        .Where(snippet => !text.Contains(snippet, StringComparison.Ordinal))
        .ToList();

    return missing.Count == 0
        ? new RegressionCheck(name, true, "关键片段存在")
        : new RegressionCheck(name, false, $"缺少：{string.Join(", ", missing)}");
}

internal sealed record RegressionCheck(string Name, bool Passed, string Detail);
