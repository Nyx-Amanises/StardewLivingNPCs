using System;
using System.Collections.Generic;
using System.Linq;

using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Dialogue.Ui;

/// <summary>
/// Thread-safe handoff between asynchronous LLM callbacks and the game-thread dialogue window.
/// This class stores data only; all SpriteText, viewport, and DialogueBox work stays in update().
/// </summary>
internal sealed class StreamingDialogueUpdateQueue
{
    private readonly object gate = new();
    private string rawText = string.Empty;
    private bool previewDirty;
    private bool completionQueued;
    private Completion? pendingCompletion;

    internal sealed record Completion(
        GenerationResult Result,
        IReadOnlyList<StreamingResponseOption> ResponseOptions,
        Action<StreamingResponseOption>? OnResponseSelected,
        Action? OnFinished);

    internal sealed record PendingUpdate(string? PreviewRawText, Completion? Final)
    {
        public static PendingUpdate None { get; } = new(null, null);

        public bool HasValue => this.PreviewRawText != null || this.Final != null;
    }

    public void AppendToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        lock (this.gate)
        {
            if (this.completionQueued)
            {
                return;
            }

            this.rawText += token;
            this.previewDirty = true;
        }
    }

    public void Complete(
        GenerationResult? result,
        IEnumerable<StreamingResponseOption>? responseOptions,
        Action<StreamingResponseOption>? onResponseSelected,
        Action? onFinished)
    {
        var stableResult = result ?? new GenerationResult();
        var stableOptions = (responseOptions ?? Enumerable.Empty<StreamingResponseOption>())
            .Where(option => option != null && !string.IsNullOrWhiteSpace(option.Text))
            .ToArray();

        lock (this.gate)
        {
            if (this.completionQueued)
            {
                return;
            }

            this.completionQueued = true;
            this.previewDirty = false;
            this.pendingCompletion = new Completion(
                stableResult,
                stableOptions,
                onResponseSelected,
                onFinished);
        }
    }

    public PendingUpdate Drain()
    {
        lock (this.gate)
        {
            if (this.pendingCompletion != null)
            {
                Completion final = this.pendingCompletion;
                this.pendingCompletion = null;
                return new PendingUpdate(null, final);
            }

            if (!this.previewDirty)
            {
                return PendingUpdate.None;
            }

            this.previewDirty = false;
            return new PendingUpdate(this.rawText, null);
        }
    }
}
