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
    private int _generationId = 0;
    private bool _generationCancelled = false;

    public bool AwaitingGeneration => _awaitingGeneration;
    public NPC SpeakingNpc => _speakingNpc;
    
    private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        // Only perform generation if we are awaiting it
        if (_awaitingGeneration && Game1.activeClickableMenu == null)
        {
            _awaitingGeneration = false;
            _generationCancelled = false;
            int generationId = ++_generationId;
            ThinkingDialogueController.Start(_speakingNpc);

            _ = PerformGeneration(generationId);
        }
    }

    private async Task PerformGeneration(int generationId)
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
                if (!IsCurrentGeneration(generationId))
                {
                    // The player cancelled (Esc) or a newer request superseded this one.
                    return;
                }

                ThinkingDialogueController.Close();

                if (newDialogue != null && newDialogue.dialogues.Count > 0)
                {
                    ShowNativeDialogue(npc, newDialogue);
                }
            }
        }
        catch (Exception ex)
        {
            var npc = _speakingNpc;
            ModEntry.SMonitor?.Log($"Error generating NPC response for {npc?.Name ?? "unknown NPC"}: {ex}", StardewModdingAPI.LogLevel.Error);

            if (!IsCurrentGeneration(generationId))
            {
                // The player cancelled or a newer request superseded this one; nothing to show.
                return;
            }

            // Make sure to hide thinking window even if there's an error
            if (AndroidHelper.IsAndroid)
            {
                EventHandler<UpdateTickedEventArgs> errorHandler = null;
                errorHandler = (sender, e) =>
                {
                    ThinkingDialogueController.Close();
                    ShowFallbackDialogue(npc);
                    ModEntry.SHelper.Events.GameLoop.UpdateTicked -= errorHandler;
                };
                ModEntry.SHelper.Events.GameLoop.UpdateTicked += errorHandler;
            }
            else
            {
                ThinkingDialogueController.Close();
                ShowFallbackDialogue(npc);
            }
        }
        finally
        {
            // Only reset shared state if this is still the active generation; a cancelled or
            // superseded run must not clobber a newer request's state.
            if (generationId == _generationId)
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

        void ShowFallbackDialogue(NPC npc)
        {
            if (npc == null)
            {
                return;
            }

            try
            {
                var fallback = new Dialogue(npc, $"{SldConstants.DialogueKeyPrefix}Error", "...");
                ShowNativeDialogue(npc, fallback);
            }
            catch (Exception fallbackException)
            {
                ModEntry.SMonitor?.Log($"Error showing fallback NPC response for {npc.Name}: {fallbackException}", StardewModdingAPI.LogLevel.Error);
            }
        }
    }

    private bool IsCurrentGeneration(int generationId)
    {
        return generationId == _generationId && !_generationCancelled;
    }

    /// <summary>
    /// Cancels the in-flight "thinking" generation (e.g. when the player presses Esc).
    /// The thinking box is closed immediately and any late result is discarded; the
    /// underlying request still ends on its own query timeout in the background.
    /// </summary>
    internal void CancelActiveGeneration()
    {
        _generationCancelled = true;
        _generationId++;
        _awaitingGeneration = false;
        ThinkingDialogueController.Close();
    }

    private static void ShowNativeDialogue(NPC npc, Dialogue dialogue)
    {
        if (npc == null || dialogue == null || dialogue.dialogues.Count == 0)
        {
            return;
        }

        Game1.currentSpeaker = npc;
        Game1.DrawDialogue(dialogue);
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

            bool shouldEndConversation = analysis.EndConversation
                || ConversationTextPostProcessor.PlayerLikelyEndedConversation(playerText)
                || ConversationTextPostProcessor.NpcLikelyEndedConversation(parsedLines.FirstOrDefault() ?? string.Empty);
            string displayLine = parsedLines.FirstOrDefault() ?? "...";
            var responseOptions = shouldEndConversation
                ? Array.Empty<StreamingResponseOption>()
                : DialogueBuilder.Instance.BuildStreamingResponseOptions(parsedLines).ToArray();

            streamingWindow.Complete(
                displayLine,
                responseOptions,
                selected =>
                {
                    if (Game1.activeClickableMenu == streamingWindow)
                    {
                        Game1.exitActiveMenu();
                    }

                    if (selected.Kind == StreamingResponseOptionKind.Silent)
                    {
                        DialogueBuilder.Instance.AddConversation(npc, string.Empty, isPlayerLine: true);
                        return;
                    }

                    if (selected.Kind == StreamingResponseOptionKind.Typed)
                    {
                        TextInputManager.RequestTextInput(
                            Util.GetString("uiYourResponse", returnNull: true) ?? "Your response",
                            npc,
                            npc.LoadedDialogueKey ?? "default",
                            DialogueBuilder.Instance.LastContext?.ChatHistory?.ToList() ?? new List<ConversationElement>());
                        return;
                    }

                    var playerLine = new ConversationElement(selected.Text, true);
                    AsyncBuilder.Instance.RequestNpcResponse(npc, new[] { playerLine });
                },
                () =>
                {
                    if (Game1.activeClickableMenu == streamingWindow)
                    {
                        Game1.exitActiveMenu();
                    }
                });
        }
        catch (Exception ex)
        {
            var npc = _speakingNpc;
            ModEntry.SMonitor?.Log($"Error generating streaming NPC response for {npc?.Name ?? "unknown NPC"}: {ex}", StardewModdingAPI.LogLevel.Error);
            streamingWindow.Complete(
                "...",
                Array.Empty<StreamingResponseOption>(),
                null,
                () =>
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
        var generated = await DialogueBuilder.Instance.GenerateResponseDetailed(npc, conversation, true);
        string playerText = conversation.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
        bool shouldEndConversation = DialogueBuilder.Instance.GetCharacter(npc).LastConversationAnalysis.EndConversation
            || ConversationTextPostProcessor.PlayerLikelyEndedConversation(playerText)
            || ConversationTextPostProcessor.NpcLikelyEndedConversation(generated.ParsedLines.FirstOrDefault() ?? string.Empty);
        var newDialogue = shouldEndConversation
            ? generated.ParsedLines.FirstOrDefault() ?? string.Empty
            : generated.FormattedLine;
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
