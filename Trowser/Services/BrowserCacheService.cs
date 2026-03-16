using System.Collections.Concurrent;

using Trowser.Core.Models;
using Trowser.Views;

namespace Trowser.Services;

public sealed class BrowserCacheService
{
    private readonly ConcurrentDictionary<Guid, BrowserPage> _activePages = new();

    // Caching disabled — always creates a fresh page and closes the previous one.
    public BrowserPage GetOrCreate(TrayBrowserConfig config)
    {
        var newPage = new BrowserPage();

        if (_activePages.TryRemove(config.Id, out BrowserPage? oldPage))
        {
            oldPage.CloseWebView();
        }

        _activePages[config.Id] = newPage;
        return newPage;
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
