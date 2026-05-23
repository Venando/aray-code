using System.Text.Json;

namespace ArayCode.Services;

/// <summary>
/// Shared helpers for <see cref="IAgentActivityRenderer"/> implementations.
/// </summary>
internal static class AgentActivityRendererHelpers
{
    public static string? GetString(JsonElement? el, string key)
    {
        if (el is not { } e) return null;
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    public static string ShortenPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length <= 2) return path;
        return "…/" + string.Join("/", parts[^2..]);
    }

    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 1)] + "…";
    }

    public static string FormatDisplayName(string toolName)
    {
        return string.Join(" ", toolName.Split('_').Select(w => char.ToUpper(w[0]) + w[1..]));
    }
}
