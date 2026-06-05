using System.Collections.Concurrent;

using Trowser.Core.Models;
using Trowser.Views;

namespace Trowser.Services;

public sealed class BrowserCacheService
{
    private readonly ConcurrentDictionary<Guid, BrowserPage> _activePages = new();

    public BrowserPage GetOrCreate(TrayBrowserConfig config)
    {
        return _activePages.GetOrAdd(config.Id, _ => new BrowserPage());
    }

    public void Remove(Guid configId)
    {
        if (_activePages.TryRemove(configId, out BrowserPage? page))
        {
            page.CloseWebView();
        }
    }

    public void Clear()
    {
        foreach (var kvp in _activePages)
        {
            kvp.Value.CloseWebView();
        }
        _activePages.Clear();
    }
}
