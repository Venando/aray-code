using System.Text.Json.Serialization;

namespace ArayCode.Services.DirectLlm.Models;

public sealed class OpenAiResponse
{
    [JsonPropertyName("choices")]
    public OpenAiChoice[]? Choices { get; set; }
}
