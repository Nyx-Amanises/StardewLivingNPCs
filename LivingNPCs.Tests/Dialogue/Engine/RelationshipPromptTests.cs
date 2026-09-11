using System.Text.RegularExpressions;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Tests.Dialogue.Content;
using Newtonsoft.Json;

namespace LivingNPCs.Tests.Dialogue.Engine;

[Collection("LlmLayer")]
public sealed class RelationshipPromptTests : IDisposable
{
    public RelationshipPromptTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    public void Dispose() => DialogueServices.Initialize(null!, null!, new DialogueConfig());

    [Theory]
    [InlineData("default", "Dating", "None", "is dating the farmer")]
    [InlineData("default", "Dating", "Male", "is dating the farmer")]
    [InlineData("default", "Dating", "Female", "is dating the farmer")]
    [InlineData("default", "Engaged", "Female", "are engaged")]
    [InlineData("default", "Divorced", "Male", "are divorced")]
    [InlineData("zh", "Dating", "None", "正在与农夫交往")]
    [InlineData("zh", "Dating", "Male", "正在与农夫交往")]
    [InlineData("zh", "Dating", "Female", "正在与农夫交往")]
    [InlineData("zh", "Engaged", "Female", "已经订婚")]
    [InlineData("zh", "Divorced", "Male", "已经离婚")]
    public void ExplicitStatusDoesNotAlsoTeachUnestablishedCourtship(
        string locale, string status, string gender, string expected)
    {
        var prompt = Assemble(Snapshot(status), locale, Enum.Parse<PromptGender>(gender));

        Assert.Contains(expected, prompt.CorePrompt);
        Assert.DoesNotContain("may be developing romantic interest", prompt.CorePrompt);
        Assert.DoesNotContain("not dating the farmer unless", prompt.CorePrompt);
        Assert.DoesNotContain("可能刚开始对农夫产生恋爱兴趣", prompt.CorePrompt);
        Assert.DoesNotContain("可能正在对农夫产生浪漫兴趣", prompt.CorePrompt);
        Assert.DoesNotContain("relationship word", prompt.CorePrompt);
        Assert.DoesNotContain("关系词是", prompt.CorePrompt);
        Assert.DoesNotContain("{{relationship", prompt.CorePrompt);
        Assert.DoesNotContain("clothing", prompt.CorePrompt);
        Assert.DoesNotContain("典型的女性衣着", prompt.CorePrompt);
    }

    [Theory]
    [InlineData("default", true, "publicly known")]
    [InlineData("default", false, "kept quiet or treated discreetly")]
    [InlineData("zh", true, "是公开的")]
    [InlineData("zh", false, "较低调或谨慎处理")]
    public void DatingKeepsTheActualPublicOrPrivateStatus(string locale, bool publicly, string expected)
    {
        var snapshot = new GameStateSnapshot
        {
            FriendshipPoints = 2500, NpcIsDatable = true, IsDating = true, DatingPublicly = publicly
        };

        string core = Assemble(snapshot, locale, PromptGender.Female).CorePrompt;

        Assert.Contains(expected, core);
        Assert.DoesNotContain("is and publicly", core);
        Assert.DoesNotContain("is but discreetly", core);
    }

    [Theory]
    [InlineData("default", "partner")]
    [InlineData("zh", "恋人")]
    public void LegacyDatingOverrideGetsANonemptyLocalizedRelationshipTerm(string locale, string expected)
    {
        var overrides = new Dictionary<string, string>
        {
            ["specialRelationshipDating"] = "{{Name}} is dating the farmer as their {{relationshipWord}}; {{relationshipPublic}}."
        };

        string core = Assemble(Snapshot("Dating"), locale, PromptGender.Female, promptOverrides: overrides).CorePrompt;

        Assert.Contains($"as their {expected};", core);
        Assert.DoesNotContain("{{relationship", core);
    }

    [Theory]
    [InlineData("default", "Hearts alone")]
    [InlineData("zh", "爱心数本身")]
    public void HeartsAloneStillDoNotEstablishRomance(string locale, string guard)
    {
        string core = Assemble(Snapshot("None"), locale, PromptGender.Female).CorePrompt;

        Assert.Contains(guard, core);
        Assert.DoesNotContain("is dating the farmer. Their relationship", core);
        Assert.DoesNotContain("正在与农夫交往，这段关系", core);
    }

    [Theory]
    [InlineData("Dating", "specialRelationshipDating")]
    [InlineData("Engaged", "specialRelationshipEngaged")]
    [InlineData("Married", "coreMarried")]
    [InlineData("Roommate", "coreRoommates")]
    public void CurrentCommitmentTakesPrecedenceOverHistoricalFlags(string status, string expectedKey)
    {
        var snapshot = new GameStateSnapshot
        {
            FriendshipPoints = 2500,
            NpcIsDatable = true,
            IsDating = true,
            IsEngaged = status is "Engaged" or "Married" or "Roommate",
            IsMarriedToFarmer = status is "Married" or "Roommate",
            IsRoommate = status == "Roommate",
            IsDivorced = true,
            ProposalRejected = true
        };
        var requested = new List<string>();

        Assemble(snapshot, "default", PromptGender.Female, requested);

        Assert.Contains(expectedKey, requested);
        Assert.DoesNotContain("specialRelationshipDivorced", requested);
        Assert.DoesNotContain("nonSpouseFriendshipWantToDate", requested);
        if (status != "Dating")
        {
            Assert.DoesNotContain("specialRelationshipProposalRejected", requested);
            Assert.DoesNotContain("specialRelationshipDating", requested);
        }
        else
        {
            // A rejected proposal can still matter while the couple continues dating.
            Assert.Contains("specialRelationshipProposalRejected", requested);
        }
    }

    [Theory]
    [InlineData(false, "MarriageStatus")]
    [InlineData(true, "coreRoommates")]
    public void ExplicitSharedHomeSurvivesMissingFriendshipScore(bool roommate, string section)
    {
        var requested = new List<string>();
        var prompt = Assemble(new GameStateSnapshot
        {
            IsMarriedToFarmer = !roommate, IsRoommate = roommate, FriendshipPoints = -1
        }, "default", PromptGender.None, requested);

        Assert.True(prompt.SectionLengths[section] > 0);
        Assert.DoesNotContain("meeting the farmer for the first time", prompt.CorePrompt);
        Assert.DoesNotContain(requested, key => key.StartsWith("marriageSentiment", StringComparison.Ordinal));
    }

    [Fact]
    public void CoreHeaderCarriesTheFarmerFactOnlyOnce()
    {
        const string fact = "The farmer is female.";
        var prompt = new PromptAssembler(new PromptAssemblyInput
        {
            Request = new GenerationRequest { NpcName = "Penny" },
            NpcName = "Penny",
            NpcDisplayName = "Penny",
            Lookup = (key, _, _) => key is "coreFarmerGender" or "coreGenderReferences" ? fact : null
        }).Assemble();

        Assert.Equal(1, prompt.CorePrompt.Split(fact, StringSplitOptions.None).Length - 1);
    }

    private static GameStateSnapshot Snapshot(string status) => new()
    {
        FriendshipPoints = 2500,
        NpcIsDatable = true,
        IsDating = status == "Dating",
        IsEngaged = status == "Engaged",
        IsDivorced = status == "Divorced",
        DaysUntilWedding = 3
    };

    private static AssembledPrompt Assemble(
        GameStateSnapshot snapshot, string locale, PromptGender gender, List<string>? requested = null,
        IReadOnlyDictionary<string, string>? promptOverrides = null)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "LivingNPCs", "assets", "dialogue")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string promptPath = Path.Combine(root!, "LivingNPCs", "assets", "dialogue", "prompts", locale + ".json");
        var pipeline = new FakeContentPipeline
        {
            CurrentLocale = locale,
            PlayerGenderKey = "Female",
            PromptData = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(promptPath))!,
            // The game content pipeline expands these branches before PromptTable sees them.
            Preprocessor = text => Regex.Replace(text, @"\$\{([^{}^]*)\^([^{}]*)\}\$", match => match.Groups[2].Value)
        };
        var table = new PromptTable(pipeline);
        return new PromptAssembler(new PromptAssemblyInput
        {
            Request = new GenerationRequest { NpcName = "Penny", Snapshot = snapshot },
            NpcName = "Penny",
            NpcDisplayName = "Penny",
            NpcGender = gender,
            Bio = new NpcBio { Biography = "A quiet teacher with a steady, thoughtful voice." },
            Lookup = (key, tokens, _) =>
            {
                requested?.Add(key);
                return table.Lookup(key, gender, promptOverrides, tokens, returnNull: true);
            }
        }).Assemble();
    }
}
