using System.Text.Json;

namespace zxavoxpaymentbe.Providers;

public static class ProviderJson
{
    /// <summary>The string (or number, as text) at a dotted path such as "data.transaction.status", or empty.</summary>
    public static string Text(JsonElement element, string path)
    {
        foreach (var name in path.Split('.'))
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                return string.Empty;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.GetRawText(),
            _ => string.Empty,
        };
    }

    /// <summary>The parsed body, or an empty object when it is not JSON (e.g. an HTML error page).</summary>
    public static JsonElement Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}
