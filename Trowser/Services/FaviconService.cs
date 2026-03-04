using Trowser.Core.Models;

namespace Trowser.Services;

public class FaviconService
{
    private static readonly HttpClient _httpClient = new();
    private readonly string _cacheFolder;

    public FaviconService()
    {
        _cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Trowser", "Icons");

        if (!Directory.Exists(_cacheFolder))
        {
            Directory.CreateDirectory(_cacheFolder);
        }
    }

    public async Task<string?> GetIconPathAsync(TrayBrowserConfig config)
    {
        if (config.IconMode == IconMode.CustomFile && !string.IsNullOrEmpty(config.IconPath) && File.Exists(config.IconPath))
        {
            return config.IconPath;
        }

        // Try to fetch favicon
        if (!string.IsNullOrWhiteSpace(config.Url))
        {
            try
            {
                var uri = new Uri(config.Url);
                var faviconUrl = $"{uri.Scheme}://{uri.Host}/favicon.ico";
                var cachedPath = Path.Combine(_cacheFolder, $"{config.Id}.ico");

                if (File.Exists(cachedPath))
                {
                    return cachedPath;
                }

                var response = await _httpClient.GetAsync(faviconUrl);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length > 0)
                    {
                        await File.WriteAllBytesAsync(cachedPath, bytes);
                        return cachedPath;
                    }
                }
            }
            catch
            {
                // Favicon fetch failed, fall through to default
            }
        }

        return null;
    }

    public void ClearCache(Guid configId)
    {
        var cachedPath = Path.Combine(_cacheFolder, $"{configId}.ico");
        if (File.Exists(cachedPath))
        {
            File.Delete(cachedPath);
        }
    }
}
