using System.Text.Json;

namespace Trowser.Core.Helpers;

public static class Json
{
    public static async Task<T?> ToObjectAsync<T>(string value)
    {
        return await Task.Run(() => JsonSerializer.Deserialize<T>(value));
    }

    public static async Task<string> StringifyAsync<T>(T value)
    {
        return await Task.Run(() => JsonSerializer.Serialize(value));
    }
}
