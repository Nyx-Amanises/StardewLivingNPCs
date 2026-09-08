using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Behavior.Ui;
using LivingNPCs.Dialogue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewValley;

namespace LivingNPCs.Tests;

public sealed class MemoryBookMomentLocalizationTests
{
    private const string LegacyOuting = "the farmer and 潘妮 spent time together at 海边, taking in the surroundings together";
    private const string ClassroomRequest = "给文森特和贾斯准备手工课材料，需要一个地晶。";
    private const string LegacyFavor = "the farmer helped with a personal request: " + ClassroomRequest
        + "; this could naturally grow into deeper relationship if the next conversation supports it";

    private static readonly Lazy<Dictionary<string, string>> Chinese = new(() => ReadTranslations("zh.json"));
    private static readonly Lazy<Dictionary<string, string>> English = new(() => ReadTranslations("default.json"));

    [Fact]
    public void SavedOutingUsesTheCurrentLanguageAndNpcNameWithoutRewritingTheState()
    {
        var state = StateWithExperience(new SharedExperienceFact
        {
            Type = "companion_outing",
            Key = "companion_outing:Beach",
            Summary = LegacyOuting,
            LocationName = "Beach",
            LocationLabel = "海边",
            LastUpdatedTotalDays = 40
        });
        string saved = JsonConvert.SerializeObject(state);

        var chinese = Render(state, "zh");
        var english = Render(state, "en");
        var chineseAgain = Render(state, "zh");

        string chineseBody = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("潘妮", chineseBody);
        Assert.Contains("海边", chineseBody);
        Assert.Contains("风景", chineseBody);
        Assert.Contains("海边", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.DoesNotContain("Penny", AllText(chinese));
        Assert.DoesNotContain("taking in the surroundings", AllText(chinese));

        string englishBody = Assert.Single(english, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("Penny", englishBody);
        Assert.Contains("beach", englishBody);
        Assert.Contains("surroundings", englishBody);
        Assert.Contains("beach", Assert.Single(english, line => line.Kind == MemoryBookLineKind.Muted).Text);
        AssertEnglish(AllText(english));
        AssertNoPromptFragments(AllText(chinese));
        AssertNoPromptFragments(AllText(english));
        Assert.Equal(chinese.ToArray(), chineseAgain.ToArray());
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Theory]
    [InlineData("taking in the surroundings together", "风景", "surroundings")]
    [InlineData("looking around the place together", "逛", "looking around")]
    [InlineData("sharing some quiet time", "安静", "quiet")]
    [InlineData("spending relaxed public time together", "轻松", "relaxing")]
    [InlineData("walking the festival edge together", "节日", "festival")]
    [InlineData("visiting the place together", "作伴", "visiting")]
    public void BriefOutingsRetainEachActivityInBothLanguages(string activity, string chineseMeaning, string englishMeaning)
    {
        var state = StateWithExperience(new SharedExperienceFact
        {
            Type = "companion_outing",
            Key = "companion_outing:Beach",
            Summary = $"the farmer and 潘妮 briefly went together to 海边, {activity}",
            LocationName = "Beach",
            LocationLabel = "海边",
            LastUpdatedTotalDays = 40
        });

        var chinese = Render(state, "zh");
        var english = Render(state, "en");

        string chineseBody = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("短暂", chineseBody);
        Assert.Contains("潘妮", chineseBody);
        Assert.Contains("海边", chineseBody);
        Assert.Contains(chineseMeaning, chineseBody);
        Assert.DoesNotContain(activity, AllText(chinese));
        string englishBody = Assert.Single(english, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("short", englishBody);
        Assert.Contains("Penny", englishBody);
        Assert.Contains("beach", englishBody);
        Assert.Contains(englishMeaning, englishBody);
        AssertEnglish(AllText(english));
        AssertNoPromptFragments(AllText(chinese));
        AssertNoPromptFragments(AllText(english));
    }

    [Theory]
    [InlineData(LegacyOuting, "海边")]
    [InlineData("the farmer and 潘妮 briefly went together to 海边, sharing some quiet time", "")]
    public void SnapshotWithoutExperienceMetadataUsesItsLegacySummaryPrefix(string summary, string locationLabel)
    {
        // Older snapshots only carried prose and display labels for these experiences.
        var snapshot = new BookSnapshotMessage
        {
            CapturedTotalDays = 42,
            Npcs = [new BookNpcSnapshot
            {
                NpcName = "Penny",
                SharedExperiences = [new BookSharedExperienceSnapshot
                {
                    Summary = summary,
                    LocationLabel = locationLabel,
                    LastUpdatedTotalDays = 40
                }]
            }]
        };
        var source = MemoryBookData.BuildSourceFromSnapshot(snapshot, _ => "Penny", _ => 0, Translation("en"));
        Assert.True(source.TryGetState("Penny", out LivingNpcState? state));
        Assert.NotNull(state);
        SharedExperienceFact experience = Assert.Single(state.SharedExperiences);
        Assert.Empty(experience.Type);
        Assert.Empty(experience.Key);
        Assert.Empty(experience.LocationName);

        var chinese = Render(state, "zh");
        var english = Render(state, "en");

        Assert.Contains("潘妮", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text);
        Assert.Contains("海边", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.Contains("Penny", Assert.Single(english, line => line.Kind == MemoryBookLineKind.Body).Text);
        Assert.Contains("beach", Assert.Single(english, line => line.Kind == MemoryBookLineKind.Muted).Text);
        AssertEnglish(AllText(english));
        AssertNoPromptFragments(AllText(chinese));
        AssertNoPromptFragments(AllText(english));
        Assert.Equal(summary, experience.Summary);
        Assert.Equal(locationLabel, snapshot.Npcs[0].SharedExperiences[0].LocationLabel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletedFavorKeepsLocalDetailsAndReconstructsForeignDetailsFromTheMatchingRequest(bool hasMetadata)
    {
        var state = StateWithExperience(new SharedExperienceFact
        {
            Type = hasMetadata ? "help_request" : string.Empty,
            Key = hasMetadata ? "help_request:classroom" : string.Empty,
            Summary = LegacyFavor,
            LocationLabel = "a personal favor",
            LastUpdatedTotalDays = 40
        });
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Summary = "请带来一条沙丁鱼。",
            RequestedItemId = "(O)131",
            RequestedItemLabel = "沙丁鱼",
            Status = "Pending",
            CreatedTotalDays = 38
        });
        state.HelpRequests.Add(ClassroomFavor("Fulfilled"));
        string saved = JsonConvert.SerializeObject(state);
        var itemCalls = new List<(string Id, string Label)>();

        var chinese = Render(state, "zh");
        var english = Render(state, "en", (id, label) =>
        {
            itemCalls.Add((id, label));
            return (id, label) switch
            {
                ("(O)86", "地晶") => "Earth Crystal",
                ("(O)131", "沙丁鱼") => "Sardine",
                _ => throw new InvalidOperationException($"Unexpected item: {id} / {label}")
            };
        });

        string chineseBody = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("潘妮", chineseBody);
        Assert.Contains(ClassroomRequest, chineseBody);
        Assert.Contains("小忙", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.DoesNotContain("a personal favor", AllText(chinese));
        string englishBody = Assert.Single(english, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("Penny", englishBody);
        Assert.Contains("Earth Crystal", englishBody);
        Assert.Contains("personal favor", Assert.Single(english, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.DoesNotContain("Sardine", englishBody);
        Assert.Contains(english, line => line.Kind == MemoryBookLineKind.MemoryFact && line.Text.Contains("Earth Crystal"));
        Assert.Contains(("(O)86", "地晶"), itemCalls);
        AssertEnglish(AllText(english));
        AssertNoPromptFragments(AllText(chinese));
        AssertNoPromptFragments(AllText(english));
        Assert.Equal(chinese.ToArray(), Render(state, "zh").ToArray());
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Fact]
    public void CompletedFavorWithoutAStoredRequestKeepsLocalDetailsAndUsesAForeignLanguageFallback()
    {
        var state = StateWithExperience(new SharedExperienceFact
        {
            Summary = LegacyFavor,
            LocationLabel = "a personal favor",
            LastUpdatedTotalDays = 40
        });

        var chinese = Render(state, "zh");
        var english = Render(state, "en");

        Assert.Contains(ClassroomRequest, Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text);
        Assert.Contains("小忙", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Muted).Text);
        string englishBody = Assert.Single(english, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("Penny", englishBody);
        Assert.Contains("help", englishBody, StringComparison.OrdinalIgnoreCase);
        AssertEnglish(AllText(english));
        AssertNoPromptFragments(AllText(chinese));
        AssertNoPromptFragments(AllText(english));
        Assert.Equal(LegacyFavor, state.SharedExperiences[0].Summary);
        Assert.Empty(state.HelpRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnglishRequestsWithChineseItemNamesStillLocalizeTheRequestBody(bool includeSharedExperience)
    {
        var state = StateWithItemRequest("Bring 地晶 for the classroom.", "地晶", includeSharedExperience);
        string saved = JsonConvert.SerializeObject(state);
        var itemCalls = new List<(string Id, string Label)>();

        var chinese = Render(state, "zh", (id, label) =>
        {
            itemCalls.Add((id, label));
            return "地晶";
        });

        Assert.Contains("地晶", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.MemoryFact).Text);
        if (includeSharedExperience)
        {
            string body = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text;
            Assert.Contains("潘妮", body);
            Assert.Contains("地晶", body);
        }

        Assert.Contains(("(O)86", "地晶"), itemCalls);
        Assert.DoesNotContain("Bring", AllText(chinese), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("classroom", AllText(chinese), StringComparison.OrdinalIgnoreCase);
        AssertNoPromptFragments(AllText(chinese));
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChineseRequestsWithEnglishItemNamesKeepTheirDetailsAndLocalizeTheKnownItem(bool includeSharedExperience)
    {
        var state = StateWithItemRequest("请帮我带来 Earth Crystal。", "Earth Crystal", includeSharedExperience);
        string saved = JsonConvert.SerializeObject(state);
        var itemCalls = new List<(string Id, string Label)>();

        var chinese = Render(state, "zh", (id, label) =>
        {
            itemCalls.Add((id, label));
            return "地晶";
        });

        string request = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.MemoryFact).Text;
        Assert.Contains("请帮我带来", request);
        Assert.Contains("地晶", request);
        if (includeSharedExperience)
        {
            string body = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text;
            Assert.Contains("潘妮", body);
            Assert.Contains("请帮我带来", body);
            Assert.Contains("地晶", body);
        }

        Assert.Contains(("(O)86", "Earth Crystal"), itemCalls);
        Assert.DoesNotContain("Earth Crystal", AllText(chinese), StringComparison.OrdinalIgnoreCase);
        AssertNoPromptFragments(AllText(chinese));
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Fact]
    public void MixedEnglishFavorWithoutAStoredRequestUsesAChineseFallback()
    {
        var state = StateWithItemRequest("Bring 地晶 for the classroom.", "地晶", includeSharedExperience: true);
        state.HelpRequests.Clear();
        string saved = JsonConvert.SerializeObject(state);

        var chinese = Render(state, "zh");

        string body = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text;
        Assert.Contains("潘妮", body);
        Assert.Contains("帮", body);
        Assert.Contains("小忙", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.DoesNotContain("Bring", AllText(chinese), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("classroom", AllText(chinese), StringComparison.OrdinalIgnoreCase);
        AssertNoPromptFragments(AllText(chinese));
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Theory]
    [InlineData("zh", "custom_memory", "我们在自建木桥旁看见了萤火虫。", "自建木桥旁")]
    [InlineData("en", "custom_memory", "We saw fireflies beside my handmade bridge.", "my handmade bridge")]
    [InlineData("zh", "companion_outing", "我们在自建木桥旁看见了萤火虫。", "自建木桥旁")]
    [InlineData("en", "companion_outing", "We saw fireflies beside my handmade bridge.", "my handmade bridge")]
    public void UnrecognizedMemoriesInTheCurrentLanguageArePreserved(string locale, string type, string summary, string location)
    {
        var state = StateWithExperience(new SharedExperienceFact
        {
            Type = type,
            Summary = summary,
            LocationLabel = location,
            LastUpdatedTotalDays = 40
        });
        string saved = JsonConvert.SerializeObject(state);

        var lines = Render(state, locale);

        Assert.Equal(summary, Assert.Single(lines, line => line.Kind == MemoryBookLineKind.Body).Text);
        Assert.Contains(location, Assert.Single(lines, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Theory]
    [InlineData("Fulfilled", "已完成", "Done")]
    [InlineData("Pending", "进行中", "In progress")]
    [InlineData("Offered", "开过口", "Asked")]
    [InlineData("Declined", "婉拒", "Declined")]
    [InlineData("Expired", "过期", "Lapsed")]
    public void FavorStatusesAndGiftNamesFollowTheCurrentLanguage(string status, string chineseStatus, string englishStatus)
    {
        var state = new LivingNpcState { NpcName = "Penny", LastGiftName = "海参", LastGiftTotalDays = 41 };
        state.HelpRequests.Add(ClassroomFavor(status));
        string saved = JsonConvert.SerializeObject(state);
        var itemCalls = new List<(string Id, string Label)>();

        var chinese = Render(state, "zh", (_, label) => label);
        var english = Render(state, "en", (id, label) =>
        {
            itemCalls.Add((id, label));
            return (id, label) switch
            {
                ("(O)86", "地晶") => "Earth Crystal",
                ("", "海参") => "Sea Cucumber",
                _ => throw new InvalidOperationException($"Unexpected item: {id} / {label}")
            };
        });

        string chineseFavor = Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.MemoryFact).Text;
        Assert.Contains(chineseStatus, chineseFavor);
        Assert.Contains(ClassroomRequest, chineseFavor);
        Assert.Contains("海参", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Body).Text);
        string englishFavor = Assert.Single(english, line => line.Kind == MemoryBookLineKind.MemoryFact).Text;
        Assert.Contains(englishStatus, englishFavor);
        Assert.Contains("Earth Crystal", englishFavor);
        Assert.Contains("Sea Cucumber", Assert.Single(english, line => line.Kind == MemoryBookLineKind.Body).Text);
        Assert.Contains(("(O)86", "地晶"), itemCalls);
        Assert.Contains((string.Empty, "海参"), itemCalls);
        AssertEnglish(AllText(english));
        Assert.Equal(chinese.ToArray(), Render(state, "zh", (_, label) => label).ToArray());
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Theory]
    [InlineData("zh", "I have a question.", "周末安排", "周末安排")]
    [InlineData("en", "我有个问题。", "weekend plans", "weekend plans")]
    [InlineData("zh", "I have a question.", "weekend plans", "问题")]
    [InlineData("en", "我有个问题。", "周末安排", "question")]
    [InlineData("zh", "I have a question.", "", "问题")]
    [InlineData("en", "我有个问题。", "", "question")]
    public void QuestionRequestsUseALocalTopicOrAGenericQuestion(string locale, string summary, string topic, string meaning)
    {
        var state = new LivingNpcState { NpcName = "Penny" };
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Type = "question_request",
            Summary = summary,
            QuestionTopic = topic,
            Status = "Pending",
            CreatedTotalDays = 40
        });

        var lines = Render(state, locale);

        Assert.Contains(meaning, Assert.Single(lines, line => line.Kind == MemoryBookLineKind.MemoryFact).Text);
        Assert.DoesNotContain(summary, AllText(lines));
        if (locale == "en")
        {
            AssertEnglish(AllText(lines));
        }
        else
        {
            Assert.DoesNotContain("weekend plans", AllText(lines));
            Assert.DoesNotContain("question", AllText(lines), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OmittingLocalePreservesExistingFreeTextAndGiftLabels()
    {
        const string summary = "一起去海边看了日落";
        var state = StateWithExperience(new SharedExperienceFact { Summary = summary, LocationLabel = "the beach" });
        state.HelpRequests.Add(ClassroomFavor("Fulfilled"));
        state.LastGiftName = "海参";
        state.LastGiftTotalDays = 41;

        var lines = MemoryBookData.BuildMomentLines(state, 42, Translation("en"));

        Assert.Contains(lines, line => line.Kind == MemoryBookLineKind.Body && line.Text == summary);
        Assert.Contains(ClassroomRequest, Assert.Single(lines, line => line.Kind == MemoryBookLineKind.MemoryFact).Text);
        Assert.Contains(lines, line => line.Kind == MemoryBookLineKind.Body && line.Text.Contains("海参"));
    }

    [Theory]
    [InlineData(-1, "日期不详", "Date unknown")]
    [InlineData(0, "第1年春1日", "Year 1 · Spring 1")]
    [InlineData(23, "第1年春24日", "Year 1 · Spring 24")]
    [InlineData(27, "第1年春28日", "Year 1 · Spring 28")]
    [InlineData(28, "第1年夏1日", "Year 1 · Summer 1")]
    [InlineData(55, "第1年夏28日", "Year 1 · Summer 28")]
    [InlineData(56, "第1年秋1日", "Year 1 · Fall 1")]
    [InlineData(83, "第1年秋28日", "Year 1 · Fall 28")]
    [InlineData(84, "第1年冬1日", "Year 1 · Winter 1")]
    [InlineData(111, "第1年冬28日", "Year 1 · Winter 28")]
    [InlineData(112, "第2年春1日", "Year 2 · Spring 1")]
    public void TotalDaysDatesUseTheZeroBasedGameCalendarInBothLanguages(int totalDays, string chinese, string english)
    {
        Assert.Equal(chinese, MemoryBookData.FormatTotalDaysDate(totalDays, Translation("zh")));
        Assert.Equal(english, MemoryBookData.FormatTotalDaysDate(totalDays, Translation("en")));
    }

    [Theory]
    [InlineData(0, Season.Spring, 1)]
    [InlineData(1, Season.Spring, 0)]
    [InlineData(1, Season.Spring, 29)]
    [InlineData(1, (Season)99, 1)]
    public void InvalidConversationDatesUseALocalizedPlaceholder(int year, Season season, int day)
    {
        var time = new StardewTime(year, season, day, 900);

        Assert.Equal("日期不详", MemoryBookData.FormatStardewDate(time, Translation("zh")));
        Assert.Equal("Date unknown", MemoryBookData.FormatStardewDate(time, Translation("en")));
    }

    [Fact]
    public void ExperienceAndGiftDatesStayFixedWhileRosterContactStillUsesRelativeTime()
    {
        var state = StateWithExperience(new SharedExperienceFact
        {
            Summary = LegacyOuting,
            LocationName = "Beach",
            CreatedTotalDays = 23,
            LastUpdatedTotalDays = 23
        });
        state.LastGiftName = "海参";
        state.LastGiftTotalDays = 41;
        string saved = JsonConvert.SerializeObject(state);

        var chinese = Render(state, "zh");
        var english = Render(state, "en", (_, _) => "Sea Cucumber");
        var laterChinese = MemoryBookData.BuildMomentLines(state, 100, Translation("zh"), "潘妮", "zh");
        var roster = MemoryBookData.BuildRoster([state], _ => "潘妮", _ => 0, 42, Translation("zh"));

        Assert.StartsWith("第1年春24日 · ", Assert.Single(chinese, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.Contains(chinese, line => line.Text == "最近一次礼物：海参（第1年夏14日）");
        Assert.StartsWith("Year 1 · Spring 24 · ", Assert.Single(english, line => line.Kind == MemoryBookLineKind.Muted).Text);
        Assert.Contains(english, line => line.Text == "Last gift: Sea Cucumber (Year 1 · Summer 14)");
        Assert.Equal(chinese.ToArray(), laterChinese.ToArray());
        Assert.Equal("最近接触：昨天", Assert.Single(roster).SubtitleText);
        Assert.Equal(saved, JsonConvert.SerializeObject(state));
    }

    [Theory]
    [InlineData(23, 40, "第1年夏13日")]
    [InlineData(23, -1, "第1年春24日")]
    [InlineData(-1, -1, "日期不详")]
    public void ExperienceDatesPreferTheLastOccurrenceAndFallBackForLegacyRecords(int createdDay, int updatedDay, string expectedDate)
    {
        var state = StateWithExperience(new SharedExperienceFact
        {
            Summary = LegacyOuting,
            LocationName = "Beach",
            CreatedTotalDays = createdDay,
            LastUpdatedTotalDays = updatedDay
        });

        var lines = Render(state, "zh");

        Assert.StartsWith(expectedDate + " · ", Assert.Single(lines, line => line.Kind == MemoryBookLineKind.Muted).Text);
    }

    private static LivingNpcState StateWithExperience(SharedExperienceFact experience) => new()
    {
        NpcName = "Penny",
        SharedExperiences = [experience]
    };

    private static NpcHelpRequestFact ClassroomFavor(string status) => new()
    {
        Type = "item_request",
        Summary = ClassroomRequest,
        RequestedItemId = "(O)86",
        RequestedItemLabel = "地晶",
        Status = status,
        CreatedTotalDays = 39
    };

    private static LivingNpcState StateWithItemRequest(string summary, string itemLabel, bool includeSharedExperience)
    {
        NpcHelpRequestFact request = ClassroomFavor(includeSharedExperience ? "Fulfilled" : "Pending");
        request.Summary = summary;
        request.RequestedItemLabel = itemLabel;
        var state = new LivingNpcState { NpcName = "Penny", HelpRequests = [request] };
        if (includeSharedExperience)
        {
            state.SharedExperiences.Add(new SharedExperienceFact
            {
                Type = "help_request",
                Key = "help_request:classroom",
                Summary = $"the farmer helped with a personal request: {summary}"
                    + "; this could naturally grow into deeper relationship if the next conversation supports it",
                LocationLabel = "a personal favor",
                LastUpdatedTotalDays = 40
            });
        }

        return state;
    }

    private static List<MemoryBookLine> Render(LivingNpcState state, string locale, Func<string, string, string>? localizeItem = null)
        => MemoryBookData.BuildMomentLines(state, 42, Translation(locale), locale == "zh" ? "潘妮" : "Penny", locale, localizeItem);

    private static string AllText(IEnumerable<MemoryBookLine> lines)
        => string.Join("\n", lines.Select(line => $"{line.Text}\n{line.HoverText}"));

    private static void AssertEnglish(string text) => Assert.DoesNotMatch(@"[\u3400-\u9fff\uf900-\ufaff]", text);

    private static void AssertNoPromptFragments(string text)
    {
        Assert.DoesNotContain("the farmer and ", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("briefly went together to ", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("the farmer helped with a personal request:", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("this could naturally grow into ", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("if the next conversation supports it", text, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryBookData.Translate Translation(string locale)
    {
        Dictionary<string, string> translations = (locale == "zh" ? Chinese : English).Value;
        return (key, tokens) =>
        {
            Assert.True(translations.TryGetValue(key, out string? template), $"Missing {locale} translation: {key}");
            string text = template!;
            if (tokens != null)
            {
                foreach (JProperty token in JObject.FromObject(tokens).Properties())
                {
                    text = text.Replace("{{" + token.Name + "}}", token.Value.ToString());
                }
            }

            Assert.DoesNotContain("{{", text);
            return text;
        };
    }

    private static Dictionary<string, string> ReadTranslations(string filename)
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory != null; depth++)
        {
            string path = Path.Combine(directory, "LivingNPCs", "i18n", filename);
            if (File.Exists(path))
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? throw new InvalidOperationException($"Could not read translations from {path}.");
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException($"Could not locate LivingNPCs/i18n/{filename} from the test output directory.");
    }
}
