using System.Text.Json.Serialization;

namespace ArayCode.Services.DirectLlm.Models;

public sealed class AnthropicContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}
