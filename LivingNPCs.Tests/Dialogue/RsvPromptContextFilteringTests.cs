using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using StardewValley;
using Xunit;
using BehaviorRsvAiPolicy = LivingNPCs.Behavior.RsvAiPolicy;
using DialogueRsvAiPolicy = LivingNPCs.Dialogue.Engine.RsvAiPolicy;
using RootRsvAiPolicy = LivingNPCs.RsvAiPolicy;

namespace LivingNPCs.Tests.Dialogue;

public sealed class RsvPromptContextFilteringTests
{
    [Theory]
    [InlineData("Torts")]
    [InlineData("torts")]
    [InlineData("Torts·")]
    [InlineData("Bert")]
    public void SharedPolicyBlocksRsvNpcNamesAcrossBothSubsystems(string name)
    {
        Assert.True(RootRsvAiPolicy.IsBlockedNpcName(name));
        Assert.True(DialogueRsvAiPolicy.IsBlockedNpcName(name));
        Assert.True(BehaviorRsvAiPolicy.IsBlockedNpcName(name));
    }

    [Fact]
    public void SharedPolicyLeavesOrdinaryNpcNamesAvailable()
    {
        Assert.False(RootRsvAiPolicy.IsBlockedNpcName("Penny"));
        Assert.False(DialogueRsvAiPolicy.IsBlockedNpcName("Pam"));
        Assert.False(BehaviorRsvAiPolicy.IsBlockedNpcName("Abigail"));
    }

    [Theory]
    [InlineData("Custom_Ridgeside_RidgesideVillage")]
    [InlineData("custom_ridgeside_TortsRealm")]
    [InlineData("RidgesideVillage")]
    [InlineData("RSV_Ridge")]
    [InlineData("Custom_RSVFuturePlace")]
    public void RsvLocationsAreRemovedBeforeDisplayNameResolution(string locationName)
    {
        Assert.True(RootRsvAiPolicy.IsBlockedLocationName(locationName));
        Assert.Equal(string.Empty, GameStateSnapshotCollector.PromptLocationName(locationName));
    }

    [Fact]
    public void AllRsvFormerLocationNamesAreBlockedByExactLegacyName()
    {
        string[] formerLocationNames =
        {
            "AguarBasement", "AguarCave", "AguarLab", "AlissaHouse", "AlissaShed", "BertHouse", "BertHouse2ndFloor",
            "Custom_JuneNPC_ResortDate", "EmberNight", "EzekielHouse", "EzekielPic", "FreddieHouse", "IanHouse",
            "JericHouse", "KennethHouse", "LennyHouse", "LogCabinHotel2ndFloor", "LogCabinHotel3rdFloor",
            "LogCabinHotelLobby", "LolaShed", "MaddieHouse", "MysticFalls1", "MysticFalls2", "MysticFalls3",
            "PikaHouse", "PrincessVilleDate", "Ridge", "RidgeFalls", "RidgesideVillage", "RSVAbandonedHouse",
            "RSVCableCar", "RSVCliff", "RSVGathering", "RSVGreenhouse1", "RSVGreenhouse2", "RSVHiddenWarp",
            "RSVHiddenWarp2", "RSVNinjaHouse", "RSVTheHike", "RSVTheRide", "ShiroHouse"
        };

        Assert.Equal(41, formerLocationNames.Length);
        foreach (string locationName in formerLocationNames)
        {
            Assert.True(
                RootRsvAiPolicy.IsBlockedLocationName(locationName),
                $"Expected RSV legacy location '{locationName}' to be blocked.");
            Assert.Equal(string.Empty, GameStateSnapshotCollector.PromptLocationName(locationName));
        }
    }

    [Theory]
    [InlineData("AguarCaveAnnex")]
    [InlineData("Ridgeview")]
    [InlineData("ShiroHouseboat")]
    public void LegacyLocationDenyListDoesNotUseSubstringMatching(string locationName)
    {
        Assert.False(RootRsvAiPolicy.IsBlockedLocationName(locationName));
    }

    [Fact]
    public void LegacyLocationReferencesUseTokenBoundariesInFreeText()
    {
        string context = string.Join(
            "\n",
            "- destination: AguarCave",
            "- destination: ridge",
            "- destination: AguarCaveAnnex",
            "- destination: Ridgeview",
            "- destination: ShiroHouseboat");

        string filtered = RootRsvAiPolicy.RemoveBlockedLines(context);

        Assert.Equal(
            string.Join(
                "\n",
                "- destination: ridge",
                "- destination: AguarCaveAnnex",
                "- destination: Ridgeview",
                "- destination: ShiroHouseboat"),
            filtered);
    }

    [Fact]
    public void FreeTextKeepsAmbiguousWordsWhileStructuredSourcesRemainBlocked()
    {
        const string text = "Helen and Richard found an Acorn in June near the ridge and felt bliss while eating Kiwi.";

        Assert.False(RootRsvAiPolicy.ContainsBlockedReference(text));
        Assert.Equal(text, RootRsvAiPolicy.RemoveBlockedLines(text));
        Assert.True(RootRsvAiPolicy.IsBlockedNpcName("Acorn"));
        Assert.True(RootRsvAiPolicy.IsBlockedLocationName("Ridge"));
        Assert.True(RootRsvAiPolicy.IsBlockedDialogueKey("Characters/Dialogue/Penny:june_arrival"));
    }

    [Theory]
    [InlineData("Rafseazz.RSVCP_Ancient_Music_Box")]
    [InlineData("(O)Rafseazz.RSVCP_Aurorean_Iris")]
    [InlineData("RSV_Obelisk")]
    [InlineData("RSV.CustomItem")]
    [InlineData("Custom_RSVFutureItem")]
    public void RsvContentIdsAreBlocked(string contentId)
    {
        Assert.True(RootRsvAiPolicy.IsBlockedContentId(contentId));
    }

    [Fact]
    public void PromptNpcListFiltersInternalRsvIdentityBeforeLocalization()
    {
        var candidates = new (string InternalName, string DisplayName)[]
        {
            ("Torts", "托托"),
            ("Pam", "潘姆"),
            ("Penny", "潘妮"),
            ("Bert", "伯特"),
            ("OtherJune", "June")
        };

        IReadOnlyList<string> visible = GameStateSnapshotCollector.FilterPromptVisibleNpcNames(
            candidates,
            excludedInternalName: "Penny");

        Assert.Equal(new[] { "潘姆", "June" }, visible);
    }

    [Fact]
    public void LegacyPromptLinesContainingRsvReferencesAreDropped()
    {
        string context = string.Join(
            "\n",
            "- ordinary memory about the farm",
            "- Torts has a birthday today",
            "- destination: Custom_Ridgeside_RidgesideVillage",
            "- another safe memory");

        string filtered = RootRsvAiPolicy.RemoveBlockedLines(context);

        Assert.Contains("ordinary memory", filtered);
        Assert.Contains("another safe memory", filtered);
        Assert.DoesNotContain("Torts", filtered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Custom_Ridgeside_", filtered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeLocalizedNpcAliasIsRemovedFromLegacyPromptText()
    {
        RootRsvAiPolicy.RegisterRuntimeNpcAliases(new[]
        {
            new NPC { Name = "Torts", displayName = "托托" }
        });

        string filtered = RootRsvAiPolicy.RemoveBlockedLines("- 托托今天生日\n- 潘妮在读书");

        Assert.Equal("- 潘妮在读书", filtered);
    }

    [Fact]
    public void RuntimeLocalizedLocationAliasIsRemovedFromLegacyPromptText()
    {
        RootRsvAiPolicy.RegisterRuntimeLocationAlias(
            "Custom_Ridgeside_RidgesideVillage",
            "里奇赛德村广场");

        string filtered = RootRsvAiPolicy.RemoveBlockedLines("- 地点：里奇赛德村广场\n- 地点：鹈鹕镇");

        Assert.Equal("- 地点：鹈鹕镇", filtered);
    }

    [Fact]
    public void RuntimeLocalizedItemAliasIsRemovedFromLegacyPromptText()
    {
        RootRsvAiPolicy.RegisterRuntimeContentAliases(
            "Rafseazz.RSVCP_Aurorean_Iris",
            "Aurorean Iris",
            "极光鸢尾花");

        string filtered = RootRsvAiPolicy.RemoveBlockedLines("- 收到极光鸢尾花\n- 收到咖啡");

        Assert.Equal("- 收到咖啡", filtered);
    }

    [Fact]
    public void FinalPromptAssemblyDropsBlockedLinesFromEveryPromptSource()
    {
        var input = new PromptAssemblyInput
        {
            Request = new GenerationRequest
            {
                NpcName = "Penny",
                Snapshot = new GameStateSnapshot(),
                BehaviorContext = "safe behavior line\nTorts is visiting today"
            },
            Plan = ContextRoutingPlan.Full(),
            NpcName = "Penny",
            NpcDisplayName = "Penny",
            WorldSummaryFull = "safe world line\nRSV_Obelisk is available",
            WorldSummaryBrief = "safe world line\nRSV_Obelisk is available",
            SectionOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["GameState"] = "safe override line\ndestination: Custom_Ridgeside_RidgesideVillage"
            },
            Lookup = (key, _, _) => $"[{key}]"
        };

        AssembledPrompt assembled = new PromptAssembler(input).Assemble();
        string prompt = string.Join(
            "\n",
            assembled.System,
            assembled.GameConstantContext,
            assembled.NpcConstantContext,
            assembled.CorePrompt,
            assembled.Instructions,
            assembled.Command);

        Assert.Contains("safe world line", prompt);
        Assert.Contains("safe override line", prompt);
        Assert.Contains("safe behavior line", prompt);
        Assert.DoesNotContain("Torts", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RSV_", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Custom_Ridgeside_", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistorySamplerDoesNotReturnLegacyRsvLines()
    {
        var history = new StardewEventHistory();
        history.Add(
            new StardewTime(1, StardewValley.Season.Spring, 1, 700),
            new DialogueHistory(new List<HistoryLine>
            {
                new("Torts has a birthday today."),
                new("The morning is quiet.")
            }));

        List<string> sampled = HistorySampler.Sample(
            history,
            Array.Empty<KeyValuePair<string, int>>(),
            new StardewTime(1, StardewValley.Season.Spring, 1, 800),
            "Penny",
            string.Empty);

        Assert.Single(sampled);
        Assert.Contains("The morning is quiet", sampled[0]);
        Assert.DoesNotContain("Torts", sampled[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistorySamplerDropsLegacyRsvEventKeysEvenWhenTheirLinesLookGeneric()
    {
        var history = new StardewEventHistory();
        history.Add(
            new StardewTime(1, StardewValley.Season.Spring, 1, 700),
            new DialogueEventHistory(
                new List<string>(),
                new List<HistoryLine> { new("What a lovely gathering.") },
                "event_postweddingreception"));

        List<string> sampled = HistorySampler.Sample(
            history,
            Array.Empty<KeyValuePair<string, int>>(),
            new StardewTime(1, StardewValley.Season.Spring, 1, 800),
            "Penny",
            string.Empty);

        Assert.Empty(sampled);
    }
}
