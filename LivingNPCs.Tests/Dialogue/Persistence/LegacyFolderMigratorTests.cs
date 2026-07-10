using System;
using System.IO;
using Newtonsoft.Json;
using Xunit;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Dialogue.Persistence;

[Collection("LlmLayer")]
public sealed class LegacyFolderMigratorTests : IDisposable
{
    private readonly FakeMonitor monitor = new();
    private readonly string root;
    private readonly string modsDir;
    private readonly string newModDir;

    public LegacyFolderMigratorTests()
    {
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, this.monitor, new());
        this.root = Path.Combine(Path.GetTempPath(), "livingnpcs-wp14-" + Guid.NewGuid().ToString("N"));
        this.modsDir = Path.Combine(this.root, "Mods");
        this.newModDir = Path.Combine(this.modsDir, "LivingNPCs");
        Directory.CreateDirectory(this.newModDir);
    }

    public void Dispose()
    {
        LegacyFolderMigrator.ConfigImporter = null;
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, null!, new());
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }

    /// <summary>搭旧部署的嵌套结构：Mods\ValleyTalk\ValleyTalk\ + 两个内容包（不同 UniqueID，必须被忽略）。</summary>
    private string CreateLegacyNestedDeployment()
    {
        string outer = Path.Combine(this.modsDir, "ValleyTalk");
        string inner = Path.Combine(outer, "ValleyTalk");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "manifest.json"),
            """{"Name": "ValleyTalk", "UniqueID": "dandm1.ValleyTalk", "Version": "0.1.5"}""");
        File.WriteAllText(Path.Combine(inner, "config.json"),
            """{"EnableMod": true, "Provider": "DeepSeek", "ApiKey": "sk-secret", "ModelName": "deepseek-chat", "QueryTimeout": 85, "DisableCharacters": "Krobus, Dwarf"}""");

        string cpDir = Path.Combine(outer, "[CP] ValleyTalk Base");
        Directory.CreateDirectory(cpDir);
        File.WriteAllText(Path.Combine(cpDir, "manifest.json"),
            """{"Name": "CP", "UniqueID": "dandm1.CPValleyTalk", "ContentPackFor": {"UniqueID": "Pathoschild.ContentPatcher"}}""");
        string sveDir = Path.Combine(outer, "ValleyTalk for SVE");
        Directory.CreateDirectory(sveDir);
        File.WriteAllText(Path.Combine(sveDir, "manifest.json"),
            """{"Name": "SVE", "UniqueID": "dandm1.ValleyTalkSVE", "ContentPackFor": {"UniqueID": "Pathoschild.ContentPatcher"}}""");

        Directory.CreateDirectory(Path.Combine(inner, "multiplayer"));
        File.WriteAllText(Path.Combine(inner, "multiplayer", "Farm_123.json"), "{}");
        Directory.CreateDirectory(Path.Combine(inner, "conversation_logs", "Farm_123"));
        File.WriteAllText(Path.Combine(inner, "conversation_logs", "Farm_123", "Abigail.md"), "# Abigail");

        // 诊断日志目录：不搬（裁决 3）。
        Directory.CreateDirectory(Path.Combine(inner, "ai_response_logs", "Farm_123"));
        File.WriteAllText(Path.Combine(inner, "ai_response_logs", "Farm_123", "Abigail.md"), "diag");
        return inner;
    }

    [Fact]
    public void FindsNestedLegacyFolderAndIgnoresContentPacks()
    {
        string inner = this.CreateLegacyNestedDeployment();
        var migrator = new LegacyFolderMigrator(this.newModDir);

        Assert.Equal(Path.GetFullPath(inner), Path.GetFullPath(migrator.FindLegacyFolder()!));
    }

    [Fact]
    public void CopiesMultiplayerAndTranscriptsButNotDiagnostics()
    {
        this.CreateLegacyNestedDeployment();
        var migrator = new LegacyFolderMigrator(this.newModDir);

        var result = migrator.RunOnGameLaunched(legacyModLoaded: false);

        Assert.True(result.Ran);
        Assert.Equal(1, result.MultiplayerFilesCopied);
        Assert.Equal(1, result.ConversationLogFilesCopied);
        Assert.True(File.Exists(Path.Combine(this.newModDir, "multiplayer", "Farm_123.json")));
        Assert.True(File.Exists(Path.Combine(this.newModDir, "conversation_logs", "Farm_123", "Abigail.md")));
        Assert.False(Directory.Exists(Path.Combine(this.newModDir, "ai_response_logs")));
        Assert.True(File.Exists(Path.Combine(this.newModDir, "data", "migration-state.json")));
    }

    [Fact]
    public void HandsParsedConfigToTheImporterHook()
    {
        this.CreateLegacyNestedDeployment();
        LegacyValleyTalkConfig? received = null;
        bool invoked = false;
        LegacyFolderMigrator.ConfigImporter = config =>
        {
            invoked = true;
            received = config;
        };

        new LegacyFolderMigrator(this.newModDir).RunOnGameLaunched(legacyModLoaded: false);

        Assert.True(invoked);
        Assert.NotNull(received);
        Assert.Equal("DeepSeek", received!.Provider);
        Assert.True(received.HasKnownProvider);
        Assert.Equal("sk-secret", received.ApiKey);
        Assert.Equal("Krobus, Dwarf", received.DisableCharacters);
        Assert.Equal(85, received.QueryTimeout);
    }

    [Fact]
    public void SecondRunSelfHealsSilently()
    {
        this.CreateLegacyNestedDeployment();
        var migrator = new LegacyFolderMigrator(this.newModDir);
        var first = migrator.RunOnGameLaunched(legacyModLoaded: false);

        // 迁移文件因 mod 更新/重新部署被清掉：标记仍在，静默补拷（Ran=false 表示不再播报）。
        File.Delete(Path.Combine(this.newModDir, "multiplayer", "Farm_123.json"));
        var second = migrator.RunOnGameLaunched(legacyModLoaded: false);

        Assert.True(first.Ran);
        Assert.False(second.Ran);
        Assert.True(File.Exists(Path.Combine(this.newModDir, "multiplayer", "Farm_123.json")));
        Assert.Equal(1, second.MultiplayerFilesCopied);
    }

    [Fact]
    public void SkipsEverythingWhileLegacyModIsLoaded()
    {
        this.CreateLegacyNestedDeployment();
        bool importerInvoked = false;
        LegacyFolderMigrator.ConfigImporter = _ => importerInvoked = true;

        var result = new LegacyFolderMigrator(this.newModDir).RunOnGameLaunched(legacyModLoaded: true);

        Assert.False(result.Ran);
        Assert.False(importerInvoked);
        Assert.False(File.Exists(Path.Combine(this.newModDir, "data", "migration-state.json")));
    }

    [Fact]
    public void NoLegacyFolderEndsSilentlyButStillNotifiesImporterWithNull()
    {
        LegacyValleyTalkConfig? received = new();
        bool invoked = false;
        LegacyFolderMigrator.ConfigImporter = config =>
        {
            invoked = true;
            received = config;
        };

        var result = new LegacyFolderMigrator(this.newModDir).RunOnGameLaunched(legacyModLoaded: false);

        Assert.False(result.Ran);
        Assert.True(invoked);
        Assert.Null(received); // WP15 据此仍置 LegacyConfigImported = true（裁决 5）
        // 不写标记：将来从备份恢复旧文件夹时仍可拾起。
        Assert.False(File.Exists(Path.Combine(this.newModDir, "data", "migration-state.json")));
    }

    [Fact]
    public void ManifestWithCommentsFallsBackToRegexDetection()
    {
        string inner = Path.Combine(this.modsDir, "ValleyTalk", "ValleyTalk");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "manifest.json"),
            "{\n  // SMAPI manifests tolerate comments\n  \"Name\": \"ValleyTalk\",\n  \"UniqueID\": \"dandm1.ValleyTalk\",\n}");

        Assert.NotNull(new LegacyFolderMigrator(this.newModDir).FindLegacyFolder());
    }
}
