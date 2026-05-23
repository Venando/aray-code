using System.Text.Json.Serialization;

namespace ArayCode.Services.DirectLlm.Models;

public sealed class OpenAiRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";
    
    [JsonPropertyName("messages")]
    public OpenAiMessage[] Messages { get; set; } = Array.Empty<OpenAiMessage>();
    
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}
