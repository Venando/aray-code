using System.Text;
using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class SessionsListActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "sessions_list";

    public string Render(JsonElement args)
    {
        var sb = new StringBuilder("Listing sessions");

        if (args.TryGetProperty("kinds", out var kindsProp))
        {
            sb.Append($" ({kindsProp.GetString()})");
        }
        else if (args.TryGetProperty("limit", out var limitProp))
        {
            sb.Append($" (limit {limitProp.GetInt32()})");
        }

        return sb.ToString();
    }
}
