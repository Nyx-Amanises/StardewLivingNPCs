using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StardewModdingAPI.Events;
using StardewValley;
using ValleyTalk.Platform;

namespace ValleyTalk;
public class AsyncBuilder
{
    private static AsyncBuilder _instance = new AsyncBuilder();
    public static AsyncBuilder Instance => _instance;
    private AsyncBuilder()
    { 
        ModEntry.SHelper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }

    private bool _awaitingGeneration = false;
    private GenerationType _awaitedType = GenerationType.None;
    private NPC _speakingNpc = null;
    private string _currentDialogueKey = "";
    private string _originalLine = null;
    private IEnumerable<ConversationElement> _currentConversation = null;
    private StardewValley.Object _currentGift = null;
    private int _currentTaste = 0;

    public bool AwaitingGeneration => _awaitingGeneration;
    public NPC SpeakingNpc => _speakingNpc;
    
    private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        ThinkingWindow thinkingWindow;
        // Only perform generation if we are awaiting it
        if (_awaitingGeneration && Game1.activeClickableMenu == null)
        {
            _awaitingGeneration = false;
            if (_awaitedType == GenerationType.conversation && Llm.Instance is IStreamingLlm)
            {
                var streamingWindow = new StreamingDialogueWindow(_speakingNpc);
                Game1.activeClickableMenu = streamingWindow;
                _ = PerformStreamingConversation(streamingWindow);
                return;
            }

            var character = DialogueBuilder.Instance.GetCharacter(_speakingNpc);
            var display = Util.GetString(character, "uiThinking", new { Name = _speakingNpc.displayName }) ?? $"{_speakingNpc.displayName} is thinking";
            // Show "Thinking..." window
            thinkingWindow = new ThinkingWindow(display);
            Game1.activeClickableMenu = thinkingWindow;

            _ = PerformGeneration(thinkingWindow);
        }
    }

    private async Task PerformGeneration(ThinkingWindow thinkingWindow)
    {
        try
        {
            var npc = _speakingNpc;

            Task<Dialogue> dialogueTask = null;
            _awaitingGeneration = false;
            switch (_awaitedType)
            {
                case GenerationType.Basic:
                    dialogueTask = GenerateNpc();
                    break;
                case GenerationType.conversation:
                    dialogueTask = GenerateNpcResponse();
                    break;
                case GenerationType.Gift:
                    dialogueTask = GenerateNpcGift();
                    break;
                default:
                    ModEntry.SMonitor?.Log("No valid generation type specified.", StardewModdingAPI.LogLevel.Error);
                    return; // Should not happen, but just in case
            }

            var newDialogue = await dialogueTask;
            
            // Ensure UI updates happen on main thread for Android compatibility
            if (AndroidHelper.IsAndroid)
            {
                // Schedule UI update for next game tick on main thread
                EventHandler<UpdateTickedEventArgs> updateHandler = null;
                updateHandler = (sender, e) =>
                {
                    UpdateUI();
                    ModEntry.SHelper.Events.GameLoop.UpdateTicked -= updateHandler;
                };
                ModEntry.SHelper.Events.GameLoop.UpdateTicked += updateHandler;
            }
            else
            {
                UpdateUI();
            }

            void UpdateUI()
            {
                // Hide thinking window
                if (Game1.activeClickableMenu == thinkingWindow)
                {
                    Game1.exitActiveMenu();
                }

                if (newDialogue != null && newDialogue.dialogues.Count > 0)
                {
                    npc.CurrentDialogue.Push(newDialogue);
                    Game1.DrawDialogue(newDialogue);
                    npc.CurrentDialogue.TryPop(out var _);
                }
            }
        }
        catch (Exception ex)
        {
            var npc = _speakingNpc;
            ModEntry.SMonitor?.Log($"Error generating NPC response for {npc?.Name ?? "unknown NPC"}: {ex}", StardewModdingAPI.LogLevel.Error);

            // Make sure to hide thinking window even if there's an error
            if (AndroidHelper.IsAndroid)
            {
                EventHandler<UpdateTickedEventArgs> errorHandler = null;
                errorHandler = (sender, e) =>
                {
                    if (thinkingWindow != null && Game1.activeClickableMenu == thinkingWindow)
                    {
                        Game1.exitActiveMenu();
                    }
                    ShowFallbackDialogue(npc);
                    ModEntry.SHelper.Events.GameLoop.UpdateTicked -= errorHandler;
                };
                ModEntry.SHelper.Events.GameLoop.UpdateTicked += errorHandler;
            }
            else
            {
                if (thinkingWindow != null && Game1.activeClickableMenu == thinkingWindow)
                {
                    Game1.exitActiveMenu();
                }
                ShowFallbackDialogue(npc);
            }
        }
        finally
        {
            // Reset state
            _awaitingGeneration = false;
            _speakingNpc = null;
            _currentDialogueKey = "";
            _originalLine = null;
            _currentConversation = null;
            _currentGift = null;
            _currentTaste = 0;
            _awaitedType = GenerationType.None;
        }

        void ShowFallbackDialogue(NPC npc)
        {
            if (npc == null)
            {
                return;
            }

            try
            {
                var fallback = new Dialogue(npc, $"{SldConstants.DialogueKeyPrefix}Error", "...");
                npc.CurrentDialogue.Push(fallback);
                Game1.DrawDialogue(fallback);
                npc.CurrentDialogue.TryPop(out _);
            }
            catch (Exception fallbackException)
            {
                ModEntry.SMonitor?.Log($"Error showing fallback NPC response for {npc.Name}: {fallbackException}", StardewModdingAPI.LogLevel.Error);
            }
        }
    }

    private async Task PerformStreamingConversation(StreamingDialogueWindow streamingWindow)
    {
        try
        {
            var npc = _speakingNpc;
            if (npc == null)
            {
                throw new InvalidOperationException("No NPC available for streaming response generation.");
            }

            var conversation = _currentConversation?.ToList() ?? new List<ConversationElement>();
            var generated = await DialogueBuilder.Instance.GenerateResponseDetailed(
                npc,
                conversation,
                dontSkipNext: true,
                onToken: streamingWindow.AppendToken
            );

            string playerText = conversation.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
            string newDialogue = ConversationTextPostProcessor.NormalizeImmediateNicknameReply(generated.FormattedLine, playerText);
            string[] parsedLines = generated.ParsedLines.ToArray();
            if (parsedLines.Length > 0)
            {
                parsedLines[0] = ConversationTextPostProcessor.NormalizeImmediateNicknameReply(parsedLines[0], playerText);
            }

            if (string.IsNullOrWhiteSpace(newDialogue))
            {
                ModEntry.SMonitor?.Log("Generated streaming dialogue is empty. Returning fallback dialogue.", StardewModdingAPI.LogLevel.Warn);
                newDialogue = "...";
                parsedLines = new[] { "..." };
            }

            DialogueBuilder.Instance.AddConversation(npc, newDialogue);
            var analysis = DialogueBuilder.Instance.GetCharacter(npc).LastConversationAnalysis;
            LivingNpcConversationBridge.RecordExchange(npc, playerText, newDialogue, analysis.ToJson());

            string responseOnlyLine = analysis.EndConversation
                ? string.Empty
                : DialogueBuilder.Instance.BuildResponseOnlyLine(parsedLines);
            string dialogueKey = string.IsNullOrWhiteSpace(_currentDialogueKey)
                ? $"{SldConstants.DialogueKeyPrefix}Conversation"
                : _currentDialogueKey;
            string displayLine = parsedLines.FirstOrDefault() ?? "...";

            streamingWindow.Complete(displayLine, () =>
            {
                if (Game1.activeClickableMenu == streamingWindow)
                {
                    Game1.exitActiveMenu();
                }

                if (!string.IsNullOrWhiteSpace(responseOnlyLine))
                {
                    Game1.DrawDialogue(new Dialogue(npc, dialogueKey, responseOnlyLine));
                }
            });
        }
        catch (Exception ex)
        {
            var npc = _speakingNpc;
            ModEntry.SMonitor?.Log($"Error generating streaming NPC response for {npc?.Name ?? "unknown NPC"}: {ex}", StardewModdingAPI.LogLevel.Error);
            streamingWindow.Complete("...", () =>
            {
                if (Game1.activeClickableMenu == streamingWindow)
                {
                    Game1.exitActiveMenu();
                }
            });
        }
        finally
        {
            _awaitingGeneration = false;
            _speakingNpc = null;
            _currentDialogueKey = "";
            _originalLine = null;
            _currentConversation = null;
            _currentGift = null;
            _currentTaste = 0;
            _awaitedType = GenerationType.None;
        }
    }

    internal void RequestNpcResponse(NPC currentNpc, IEnumerable<ConversationElement> currentConversation)
    {
        if (_awaitingGeneration)
        {
            ModEntry.SMonitor?.Log("Already awaiting NPC response generation. Ignoring new request.", StardewModdingAPI.LogLevel.Warn);
            return;
        }

        _speakingNpc = currentNpc;
        _currentConversation = currentConversation?.ToArray() ?? Array.Empty<ConversationElement>();
        _awaitedType = GenerationType.conversation;
        _awaitingGeneration = true;
    }

    internal void RequestNpcGiftResponse(NPC currentNpc, StardewValley.Object gift, int taste)
    {
        if (_awaitingGeneration)
        {
            ModEntry.SMonitor?.Log("Already awaiting NPC response generation. Ignoring new request.", StardewModdingAPI.LogLevel.Warn);
            return;
        }

        _speakingNpc = currentNpc;
        _currentGift = gift;
        _currentTaste = taste;
        _awaitedType = GenerationType.Gift;
        _awaitingGeneration = true;
    }

    internal void RequestNpcBasic(NPC currentNpc, string dialogueKey, string originalLine)
    {
        if (_awaitingGeneration)
        {
            ModEntry.SMonitor?.Log("Already awaiting NPC response generation. Ignoring new request.", StardewModdingAPI.LogLevel.Warn);
            return;
        }

        _speakingNpc = currentNpc;
        _currentDialogueKey = dialogueKey;
        _originalLine = originalLine;
        _awaitedType = GenerationType.Basic;
        _awaitingGeneration = true;
    }

    private async Task<Dialogue> GenerateNpcGift()
    {
        if (_currentGift == null)
        {
            ModEntry.SMonitor?.Log("No gift object available for NPC gift generation.", StardewModdingAPI.LogLevel.Warn);
            return null;
        }

        var newDialogueTask = DialogueBuilder.Instance.GenerateGift(_speakingNpc, _currentGift, _currentTaste);
        return await newDialogueTask;
    }

    private async Task<Dialogue> GenerateNpc()
    {
        var newDialogueTask = DialogueBuilder.Instance.Generate(_speakingNpc, _currentDialogueKey, _originalLine);
        return await newDialogueTask;
    }

    private async Task<Dialogue> GenerateNpcResponse()
    {
        var npc = _speakingNpc;
        if (npc == null)
        {
            ModEntry.SMonitor?.Log("No NPC available for response generation.", StardewModdingAPI.LogLevel.Warn);
            return null;
        }

        var conversation = _currentConversation?.ToList() ?? new List<ConversationElement>();
        var newDialogueTask = DialogueBuilder.Instance.GenerateResponse(npc, conversation, true);
        var newDialogue = await newDialogueTask;
        string playerText = conversation.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
        newDialogue = ConversationTextPostProcessor.NormalizeImmediateNicknameReply(newDialogue, playerText);
        if (string.IsNullOrWhiteSpace(newDialogue))
        {
            ModEntry.SMonitor?.Log("Generated dialogue is empty. Returning fallback dialogue.", StardewModdingAPI.LogLevel.Warn);
            newDialogue = "...";
        }
        DialogueBuilder.Instance.AddConversation(npc, newDialogue);
        var analysis = DialogueBuilder.Instance.GetCharacter(npc).LastConversationAnalysis;
        LivingNpcConversationBridge.RecordExchange(npc, playerText, newDialogue, analysis.ToJson());

        // Create a new dialogue with the response and add it to the NPC's dialogue stack
        var dialogueKey = string.IsNullOrWhiteSpace(_currentDialogueKey)
            ? $"{SldConstants.DialogueKeyPrefix}Conversation"
            : _currentDialogueKey;
        var dialogue = new Dialogue(npc, dialogueKey, newDialogue);
        return dialogue;
    }
}

internal enum GenerationType
{
    None,
    Basic,
    conversation,
    Gift
}
