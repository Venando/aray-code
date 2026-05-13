using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace OpenClawPTT.TTS.Providers;

[Obsolete("Use inline JSON parsing in CoquiUvTtsProvider instead.")]
public static class JsonHelper
{
    public static bool TryParseJson(string? json, [NotNullWhen(true)] out JsonDocument? jsonDocument, [NotNullWhen(true)] out string? type)
    {
        jsonDocument = null;
        type = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        byte[]? rentedArray = null;
        int maxByteCount = Encoding.UTF8.GetMaxByteCount(json.Length);
        
        // Use stack memory for small strings (< 1KB), otherwise rent from pool
        Span<byte> buffer = maxByteCount <= 1024 
            ? stackalloc byte[1024] 
            : (rentedArray = ArrayPool<byte>.Shared.Rent(maxByteCount));

        try
        {
            int bytesWritten = Encoding.UTF8.GetBytes(json, buffer);
            var utf8Source = buffer.Slice(0, bytesWritten);

            var reader = new Utf8JsonReader(utf8Source);

            // 1. Attempt to parse the document
            if (!JsonDocument.TryParseValue(ref reader, out jsonDocument))
            {
                return false;
            }

            // 2. Robust Property Extraction — return immediately if we have a valid type.
            // This ignores any trailing content after the JSON object (test case: trailing garbage).
            if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object &&
                jsonDocument.RootElement.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                type = typeElement.GetString();

                if (!string.IsNullOrWhiteSpace(type))
                {
                    return true;
                }
            }

            // 3. Fail-Proof Check: Ensure we parsed the WHOLE string.
            // Only applies when we didn't get a valid type above — catches incomplete JSON
            // like `{"type":` (valid start, missing value) where TryParse succeeds but
            // the reader still has tokens to consume.
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.Comment && reader.TokenType != JsonTokenType.None)
                {
                    jsonDocument.Dispose();
                    jsonDocument = null;
                    return false;
                }
            }

            // If we got here, it's valid JSON but doesn't meet our "type" requirements
            jsonDocument.Dispose();
            jsonDocument = null;
            type = null;
            return false;
        }
        catch
        {
            // Catch-all for unexpected encoding or parsing issues
            jsonDocument?.Dispose();
            jsonDocument = null;
            type = null;
            return false;
        }
        finally
        {
            if (rentedArray != null) ArrayPool<byte>.Shared.Return(rentedArray);
        }
    }
}