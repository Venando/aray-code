using System.Text.Json.Serialization;

namespace ArayCode.Services.DirectLlm.Models;

public sealed class AnthropicRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";
    
    [JsonPropertyName("messages")]
    public AnthropicMessage[] Messages { get; set; } = Array.Empty<AnthropicMessage>();
    
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }
    
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}
