using System.Text.Json;

namespace Asynkron.Profiler;

internal static class SpeedscopeJsonElementHelper
{
    public static JsonElement? GetOptionalArray(JsonElement element, string propertyName)
    {
        return TryGetArray(element, propertyName, out var arrayElement)
            ? arrayElement
            : null;
    }

    public static bool TryGetArray(JsonElement element, string propertyName, out JsonElement arrayElement)
    {
        if (element.TryGetProperty(propertyName, out arrayElement) &&
            arrayElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        arrayElement = default;
        return false;
    }
}
