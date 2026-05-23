using System.Text.Json.Serialization;

namespace ArayCode.Services.DirectLlm.Models;

public sealed class AnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
