using Newtonsoft.Json.Serialization;
using ValleyTalk;

internal class LlmResponse
{
    public string Text { get; set; }
    public string ErrorMessage { get; set; }
    public int ResponseCode { get; set; }
    public TokenUsage Usage { get; set; } = new();
    public bool IsSuccess { get; set; } = false; // Default to true for non-streaming responses
    public LlmResponse(string text, bool IsSuccess = true, TokenUsage usage = null)
    {
        Text = text;
        this.IsSuccess = IsSuccess;
        Usage = usage ?? new TokenUsage();
    }

    public LlmResponse(string errorMessage, int responseCode, bool IsSuccess = false, TokenUsage usage = null)
    {
        ErrorMessage = errorMessage;
        ResponseCode = responseCode;
        this.IsSuccess = IsSuccess;
        Usage = usage ?? new TokenUsage();
    }
}
