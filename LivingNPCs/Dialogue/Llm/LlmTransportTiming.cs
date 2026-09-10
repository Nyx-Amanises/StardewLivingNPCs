using System;
using System.Diagnostics;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// Client-observed milestones for one HTTP attempt, in milliseconds since that attempt started.
/// Null means the transport did not observe that milestone. Contains no request or response data.
/// Non-streaming requests expose completion only; their headers and first content are buffered.
/// </summary>
internal sealed record LlmTransportTiming(
    bool Streaming,
    long? HeadersMilliseconds,
    long? FirstContentMilliseconds,
    long CompleteMilliseconds);

/// <summary>Best-effort diagnostics; reporting must never turn a usable reply into a failure.</summary>
internal sealed class LlmTransportTimingScope : IDisposable
{
    private readonly Action<LlmTransportTiming>? observer;
    private readonly bool streaming;
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private long? headersMilliseconds;
    private long? firstContentMilliseconds;
    private bool reported;

    public LlmTransportTimingScope(Action<LlmTransportTiming>? observer, bool streaming)
    {
        this.observer = observer;
        this.streaming = streaming;
    }

    public void HeadersReceived() => this.headersMilliseconds ??= this.watch.ElapsedMilliseconds;

    public void ContentReceived() => this.firstContentMilliseconds ??= this.watch.ElapsedMilliseconds;

    public void Dispose()
    {
        if (this.reported)
        {
            return;
        }

        this.reported = true;
        this.watch.Stop();
        try
        {
            this.observer?.Invoke(new LlmTransportTiming(
                this.streaming,
                this.headersMilliseconds,
                this.firstContentMilliseconds,
                this.watch.ElapsedMilliseconds));
        }
        catch (Exception)
        {
            // This optional observer is not part of generation, cancellation, or retry decisions.
        }
    }
}
