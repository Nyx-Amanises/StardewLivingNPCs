using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Pathfinding;

namespace ValleyTalk
{
    internal class DialogueBuilder
    {
        private static int responseIndex = 20000;
        public static DialogueBuilder Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DialogueBuilder();
                }
                return _instance;
            }
        }

        public ModConfig Config { get; internal set; }
        public DialogueContext LastContext { get; private set; }
        public bool LlmDisabled { get; set; } = false;

        private static DialogueBuilder _instance;
        private Dictionary<string, ValleyTalk.Character> _characters;
        private Random _random;
        private int _patchDate;
        private Dictionary<string, bool> _patchCharacters;

        private DialogueBuilder()
        {
            _characters = new Dictionary<string, ValleyTalk.Character>();
            _random = new Random();
        }

        private void PopulateCharacters()
        {
            foreach (var npc in Game1.characterData.Keys)
            {
                if (!_characters.ContainsKey(npc))
                {
                    var npcObject = Game1.getCharacterFromName(npc);
                    GetCharacter(npcObject);
                }
            }
        }

        public ValleyTalk.Character GetCharacter(NPC instance)
        {
            if (instance == null)
            {
                return null;
            }
            if (!_characters.ContainsKey(instance.Name))
            {
                var newCharacter = new ValleyTalk.Character(
                    instance.Name, 
                    instance);
                _characters.Add(instance.Name, newCharacter);
            }
            return _characters[instance.Name];
        }

        public ValleyTalk.Character GetCharacterByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !_characters.ContainsKey(name))
            {
                return null;
            }
            return _characters[name];
        }
        
        internal async Task<string> GenerateResponse(NPC instance, List<ConversationElement> conversation, bool dontSkipNext = false)
        {
            return (await this.GenerateResponseDetailed(instance, conversation, dontSkipNext)).FormattedLine;
        }

        internal async Task<GeneratedResponse> GenerateResponseDetailed(
            NPC instance,
            List<ConversationElement> conversation,
            bool dontSkipNext = false,
            Action<string> onToken = null)
        {
            if (!CanGenerateForNpc(instance))
            {
                return new GeneratedResponse(string.Empty, Array.Empty<string>());
            }

            var character = GetCharacter(instance);

            DialogueContext context = GetBasicContext(instance);
            context.LivingNpcExtraPrompt = LivingNpcConversationBridge.GetConversationContext(instance);
            context.CanGiveGift = false;
            var fullHistory = LastContext?.ChatHistory?.ToList() ?? new List<ConversationElement>();
            fullHistory.AddRange(conversation.Where(x => !fullHistory.Any(y => y.Id == x.Id)));
            context.ChatHistory = fullHistory;
            LastContext = context;
            var theLine = await character.CreateBasicDialogue(context, onToken);
            string playerText = conversation.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
            bool forceNoResponses = character.LastConversationAnalysis.EndConversation
                || ConversationTextPostProcessor.PlayerLikelyEndedConversation(playerText)
                || ConversationTextPostProcessor.NpcLikelyEndedConversation(theLine.FirstOrDefault() ?? string.Empty);
            string formattedLine = FormatLine(theLine, forceNoResponses, allowTypedFallback: true);
            return new GeneratedResponse(
                $"{(dontSkipNext ? "" : "skip#")}{formattedLine}",
                theLine
            );
        }

        internal async Task<Dialogue> GenerateGift(NPC instance, StardewValley.Object gift, int taste)
        {
            if (!CanGenerateForNpc(instance))
            {
                return null;
            }

            var character = GetCharacter(instance);
            DialogueContext context = GetBasicContext(instance);
            context.Accept = gift;
            context.GiftTaste = taste;
            context.LivingNpcExtraPrompt = LivingNpcConversationBridge.GetGiftResponseContext(instance, gift, taste);
            LastContext = context;
            var theLine = await character.CreateBasicDialogue(context);
            string formattedLine = FormatLine(theLine);
            string giftName = string.IsNullOrWhiteSpace(gift.DisplayName) ? gift.Name : gift.DisplayName;
            LivingNpcConversationBridge.RecordExchange(
                instance,
                $"The farmer offered {giftName}.",
                formattedLine,
                character.LastConversationAnalysis.ToJson()
            );
            if (!string.IsNullOrWhiteSpace(formattedLine))
            {
                character.AddConversation(
                    new List<ConversationElement>
                    {
                        new(FormatGiftTranscriptPlayerLine(instance, giftName), true),
                        new(formattedLine, false)
                    },
                    Game1.year,
                    Game1.season,
                    Game1.dayOfMonth,
                    Game1.timeOfDay
                );
            }

            var newDialogue = new Dialogue(instance, $"Accept_{gift.Name}", formattedLine);
            return newDialogue;
        }

        private static string FormatGiftTranscriptPlayerLine(NPC instance, string giftName)
        {
            string result = Util.GetString(
                "transcriptGiftPlayerLine",
                new { npcName = instance.displayName, giftName },
                returnNull: true
            );
            return string.IsNullOrWhiteSpace(result)
                ? $"The farmer gave {instance.displayName} {giftName}."
                : result;
        }

        internal async Task<Dialogue> Generate(NPC instance, string dialogueKey, string originalLine = "")
        {
            if (!CanGenerateForNpc(instance))
            {
                return null;
            }

            var character = GetCharacter(instance);
            DialogueContext context = GetBasicContext(instance);
            context.LivingNpcExtraPrompt = LivingNpcConversationBridge.GetConversationContext(instance);
            var splitKey = dialogueKey.Split('_');
            var firstElement = splitKey.Any() ? splitKey[0] : "";
            if (Enum.TryParse<RandomAction>(firstElement, true, out var randomAction))
            {
                context.RandomAct = randomAction;
            }
            if (Enum.TryParse<SpouseAction>(firstElement, true, out var spouseAction))
            {
                context.SpouseAct = spouseAction;
            }
            context.CanGiveGift = string.IsNullOrWhiteSpace(originalLine);
            LastContext = context;
            context.ScheduleLine = originalLine;
            var theLine = await character.CreateBasicDialogue(context);
            string formattedLine = FormatLine(theLine);
            return new Dialogue(instance, dialogueKey, formattedLine);
        }

        private string FormatLine(string[] theLine, bool forceNoResponses = false, bool allowTypedFallback = false)
        {
            if (theLine == null || theLine.Length == 0)
            {
                return string.Empty;
            }
            bool canAddTypedFallback = allowTypedFallback && ModEntry.Config.TypedResponses != "Never";
            if (forceNoResponses || (theLine.Length == 1 && ModEntry.Config.TypedResponses != "Always" && !canAddTypedFallback))
            {
                return theLine[0];
            }
            var sb = new StringBuilder();
            sb.Append(theLine[0]);
            //sb.Append("#$b#Respond:");
            sb.Append($"#$q {responseIndex++} {SldConstants.DialogueKeyPrefix}Default#{Util.GetString("outputRespond")}");
            sb.Append($"#$r -999999 0 {SldConstants.DialogueKeyPrefix}Silent#{Util.GetString("outputStaySilent")}");

            for (int i = 1; i < theLine.Length; i++)
            {
                sb.Append($"#$r -999998 0 {SldConstants.DialogueKeyPrefix}Next#");
                sb.Append(theLine[i]);
            }
            if (ModEntry.Config.TypedResponses != "Never")
            {
                sb.Append($"#$r -999997 0 {SldConstants.DialogueKeyPrefix}TypedResponse#{Util.GetString("uiTypeYourResponse")}");
            }
            return sb.ToString();
        }

        internal string BuildResponseOnlyLine(string[] theLine, bool forceNoResponses = false)
        {
            if (forceNoResponses || theLine == null || theLine.Length == 0)
            {
                return string.Empty;
            }

            if (theLine.Length == 1 && ModEntry.Config.TypedResponses != "Always")
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append($"#$q {responseIndex++} {SldConstants.DialogueKeyPrefix}Default#{Util.GetString("outputRespond")}");
            sb.Append($"#$r -999999 0 {SldConstants.DialogueKeyPrefix}Silent#{Util.GetString("outputStaySilent")}");

            for (int i = 1; i < theLine.Length; i++)
            {
                sb.Append($"#$r -999998 0 {SldConstants.DialogueKeyPrefix}Next#");
                sb.Append(theLine[i]);
            }
            if (ModEntry.Config.TypedResponses != "Never")
            {
                sb.Append($"#$r -999997 0 {SldConstants.DialogueKeyPrefix}TypedResponse#{Util.GetString("uiTypeYourResponse")}");
            }

            return sb.ToString();
        }

        internal IEnumerable<StreamingResponseOption> BuildStreamingResponseOptions(string[] theLine)
        {
            if (theLine == null || theLine.Length == 0)
            {
                yield break;
            }

            if (theLine.Length == 1 && ModEntry.Config.TypedResponses != "Always")
            {
                yield break;
            }

            yield return new StreamingResponseOption(
                Util.GetString("outputStaySilent", returnNull: true) ?? "Stay silent",
                StreamingResponseOptionKind.Silent);

            foreach (string option in theLine.Skip(1).Where(option => !string.IsNullOrWhiteSpace(option)))
            {
                yield return new StreamingResponseOption(option, StreamingResponseOptionKind.Generated);
            }

            if (ModEntry.Config.TypedResponses != "Never")
            {
                yield return new StreamingResponseOption(
                    Util.GetString("uiTypeYourResponse", returnNull: true) ?? "Type your response",
                    StreamingResponseOptionKind.Typed);
            }
        }

        private DialogueContext GetBasicContext(NPC instance)
        {
            var farmer = Game1.getPlayerOrEventFarmer();
            ValleyTalk.Season season;
            switch (Game1.currentSeason)
            {
                case "spring":
                    season = ValleyTalk.Season.Spring;
                    break;
                case "summer":
                    season = ValleyTalk.Season.Summer;
                    break;
                case "fall":
                    season = ValleyTalk.Season.Fall;
                    break;
                case "winter":
                    season = ValleyTalk.Season.Winter;
                    break;
                default:
                    throw new Exception("Invalid season");
            }
            string timeOfDay;
            switch (Game1.timeOfDay)
            {
                case <= 800:
                    timeOfDay = Util.GetString("generalEarlyMorning");
                    break;
                case <= 1130:
                    timeOfDay = Util.GetString("generalLateMorning");
                    break;
                case <= 1400:
                    timeOfDay = Util.GetString("generalMidday");
                    break;
                case <= 1700:
                    timeOfDay = Util.GetString("generalAfternoon");
                    break;
                case <= 2200:
                    timeOfDay = Util.GetString("generalEvening");
                    break;
                default:
                    timeOfDay = Util.GetString("generalLateNight");
                    break;
            }
            timeOfDay += $" ({(Game1.timeOfDay / 100) % 24}:{Game1.timeOfDay % 100:00})";
            ValleyTalk.Weekday day;
            switch (Game1.dayOfMonth % 7)
            {
                case 0:
                    day = ValleyTalk.Weekday.Sun;
                    break;
                case 1:
                    day = ValleyTalk.Weekday.Mon;
                    break;
                case 2:
                    day = ValleyTalk.Weekday.Tue;
                    break;
                case 3:
                    day = ValleyTalk.Weekday.Wed;
                    break;
                case 4:
                    day = ValleyTalk.Weekday.Thu;
                    break;
                case 5:
                    day = ValleyTalk.Weekday.Fri;
                    break;
                case 6:
                    day = ValleyTalk.Weekday.Sat;
                    break;
                default:
                    throw new Exception("Invalid day");
            }
            var children = ConvertChildren(farmer.getChildren());
            var weather = new List<string>();
            if (Game1.IsRainingHere()) weather.Add("rain");
            if (Game1.IsSnowingHere()) weather.Add("snow");
            if (Game1.IsLightningHere()) weather.Add("lightning");
            if (Game1.IsGreenRainingHere()) weather.Add("green rain");
            
            var hearts = farmer.friendshipData.ContainsKey(instance.Name) ? 
                    (
                        farmer.friendshipData[instance.Name].Points == 0 ? 
                                -1 : 
                                farmer.friendshipData[instance.Name].Points / 250
                    ) 
                    : -1;
            var currentSchedule = GetCurrentScheduleEntry(instance);
            var nextSchedule = GetNextScheduleEntry(instance);
            var context = new ValleyTalk.DialogueContext()
            {
                Season = season,
                DayOfSeason = Game1.dayOfMonth,
                TimeOfDay = timeOfDay,
                Hearts = hearts,
                Location = instance.currentLocation?.Name,
                CurrentActivity = DescribeCurrentActivity(instance, currentSchedule),
                CurrentScheduleLocation = currentSchedule?.targetLocationName,
                CurrentScheduleTime = currentSchedule?.time,
                NextScheduleLocation = nextSchedule?.Entry.targetLocationName,
                MinutesUntilNextSchedule = nextSchedule.HasValue
                    ? Math.Max(0, ToDayMinutes(nextSchedule.Value.Time) - ToDayMinutes(Game1.timeOfDay))
                    : null,
                Year = Game1.year,
                Day = day,
                MaleFarmer = farmer.IsMale,
                Inlaw = farmer.getSpouse()?.Name,
                Children = children,
                Married = farmer.getSpouse() != null,
                Spouse = farmer.getSpouse()?.Name,
                Weather = weather
            };
            return context;
        }

        private static SchedulePathDescription GetCurrentScheduleEntry(NPC instance)
        {
            if (instance?.Schedule == null)
            {
                return null;
            }

            return instance.Schedule
                .Where(schedule => schedule.Key <= Game1.timeOfDay)
                .OrderByDescending(schedule => schedule.Key)
                .Select(schedule => schedule.Value)
                .FirstOrDefault();
        }

        private static (SchedulePathDescription Entry, int Time)? GetNextScheduleEntry(NPC instance)
        {
            if (instance?.Schedule == null)
            {
                return null;
            }

            var next = instance.Schedule
                .Where(schedule => schedule.Key > Game1.timeOfDay)
                .OrderBy(schedule => schedule.Key)
                .FirstOrDefault();
            return next.Value == null ? null : (next.Value, next.Key);
        }

        private static int ToDayMinutes(int timeOfDay)
        {
            int hours = timeOfDay / 100;
            int minutes = timeOfDay % 100;
            return (hours * 60) + minutes;
        }

        private static string DescribeCurrentActivity(NPC instance, SchedulePathDescription currentSchedule)
        {
            if (instance?.DirectionsToNewLocation != null)
            {
                return "walking to the next scheduled destination";
            }

            string scheduleCue = currentSchedule?.endOfRouteBehavior;
            if (string.IsNullOrWhiteSpace(scheduleCue))
            {
                scheduleCue = currentSchedule?.endOfRouteMessage;
            }

            if (!string.IsNullOrWhiteSpace(scheduleCue))
            {
                return NormalizeScheduleCue(scheduleCue);
            }

            return currentSchedule != null
                ? "standing or waiting at the current scheduled spot"
                : "standing nearby with no specific schedule activity visible";
        }

        private static string NormalizeScheduleCue(string cue)
        {
            string normalized = cue
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();
            string lower = normalized.ToLowerInvariant();

            if (lower.Contains("read") || lower.Contains("book"))
            {
                return "reading or studying at the current spot";
            }

            if (lower.Contains("fish"))
            {
                return "fishing";
            }

            if (lower.Contains("drink") || lower.Contains("beer") || lower.Contains("bar"))
            {
                return "relaxing with a drink or spending time at the bar";
            }

            if (lower.Contains("shop") || lower.Contains("work"))
            {
                return "working or tending to daily business";
            }

            if (lower.Contains("sit") || lower.Contains("bench"))
            {
                return "sitting and resting";
            }

            if (lower.Contains("exercise") || lower.Contains("workout"))
            {
                return "exercising";
            }

            if (lower.Contains("music") || lower.Contains("guitar"))
            {
                return "playing or listening to music";
            }

            if (lower.Contains("dance"))
            {
                return "dancing or practicing movement";
            }

            if (lower.Contains("sleep") || lower.Contains("bed"))
            {
                return "resting";
            }

            return $"doing the current schedule activity ({normalized})";
        }

        private List<ChildDescription> ConvertChildren(List<Child> children)
        {
            var result = new List<ChildDescription>();
            foreach (var child in children)
            {
                result.Add(new ChildDescription(
                    child.Name,
                    child.Gender == Gender.Male,
                    child.Age
                ));
            }
            return result;
        }

        internal bool AddDialogueLine(NPC instance, List<StardewValley.DialogueLine> dialogues)
        {
            var character = GetCharacter(instance);
            var filteredDialogues = FilterForHistory(dialogues, character);
            if (!filteredDialogues.Any())
            {
                return false;
            }
            character.AddDialogue(filteredDialogues, Game1.year, Game1.season, Game1.dayOfMonth, Game1.timeOfDay);
            return true;
        }

        private static List<StardewValley.DialogueLine> FilterForHistory(List<StardewValley.DialogueLine> dialogues, ValleyTalk.Character character)
        {
            if (character.MatchLastDialogue(dialogues))
            {
                return new();
            }
            // Remove the synthetic response prompt from persisted history.
            string respondPrompt = Util.GetString("outputRespond");
            return dialogues.Where(d =>
                !d.Text.StartsWith("Respond:", StringComparison.Ordinal)
                && !(!string.IsNullOrWhiteSpace(respondPrompt)
                    && d.Text.StartsWith(respondPrompt, StringComparison.Ordinal))
            ).ToList();
        }

        internal void AddEventLine(NPC instance, IEnumerable<NPC> actors, string festivalName, List<StardewValley.DialogueLine> dialogues)
        {
            var character = GetCharacter(instance);
            var filteredDialogues = FilterForHistory(dialogues, character);
            if (!filteredDialogues.Any()) return;
            character.AddEventDialogue(filteredDialogues,actors,festivalName,Game1.year,Game1.season,Game1.dayOfMonth,Game1.timeOfDay);
        }

        internal void AddOverheardLine(NPC otherNpc, NPC instance, List<StardewValley.DialogueLine> theLine)
        {
            var character = GetCharacter(otherNpc);
            var filteredDialogues = FilterForHistory(theLine, character);
            
            character.AddOverheardDialogue(instance, filteredDialogues, Game1.year, Game1.season, Game1.dayOfMonth, Game1.timeOfDay);
        }

        internal void AddConversation(NPC otherNpc, string newDialogue, bool isPlayerLine = false)
        {
            var character = GetCharacter(otherNpc);
            DialogueContext context = LastContext ?? GetBasicContext(otherNpc);
            var fullHistory = context.ChatHistory.ToList();
            if (!string.IsNullOrEmpty(newDialogue))
            {
                fullHistory.Add(new ConversationElement(newDialogue, isPlayerLine));
            }
            context.ChatHistory = fullHistory;
            // Store whether the last line was from the player to help the LLM format responses appropriately
            context.LastLineIsPlayerInput = isPlayerLine;
            character.AddConversation(fullHistory, Game1.year, Game1.season, Game1.dayOfMonth, Game1.timeOfDay);
        }

        internal bool PatchNpc(NPC n,int probability=4,bool retainResult=false)
        {
            if (LlmDisabled || !ModEntry.Config.EnableMod || probability == 0 || !CanGenerateForNpc(n))
            {
                return false;
            }
            if (ModEntry.Config.DisabledCharactersList.Contains(n.Name))
            {
                return false;
            }
            if (ModEntry.BlockModdedContent)
            {
                if (_characters.Count == 0)
                {
                    PopulateCharacters();
                }
                var character = GetCharacter(n);
                if (string.IsNullOrWhiteSpace(character?.Bio?.Biography ?? ""))
                {
                    return false;
                }
            }
            if (probability < 4)
            {
                if (retainResult)
                {
                    if (_patchDate != Game1.Date.TotalDays || _patchCharacters == null)
                    {
                        _patchDate = Game1.Date.TotalDays;
                        _patchCharacters = new Dictionary<string, bool>();
                    }
                    if (_patchCharacters.ContainsKey(n.Name))
                    {
                        return _patchCharacters[n.Name];
                    }
                }
                if (probability == -1)
                {
                    // To do - ask for interaction type
                }
                else if (_random.Next(4) >= probability)
                {
                    if (retainResult)
                    {
                        _patchCharacters.Add(n.Name, false);
                    }
                    return false;
                }
                else if (retainResult)
                {
                    _patchCharacters.Add(n.Name, true);
                }
            }

            return true;
        }

        internal static bool CanGenerateForNpc(NPC n)
        {
            return n != null && !RsvAiPolicy.IsBlockedNpc(n);
        }

        internal bool PatchPassiveNpc(NPC n, int probability = 4, bool retainResult = false)
        {
            return ModEntry.Config.GenerateAiForNormalRightClick
                && PatchNpc(n, probability, retainResult);
        }

        internal void ClearContext()
        {
            LastContext = null;
        }
    }
}
