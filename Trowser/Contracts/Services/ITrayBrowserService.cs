using Trowser.Core.Models;

namespace Trowser.Contracts.Services;

public interface ITrayBrowserService
{
    Task<List<TrayBrowserConfig>> GetAllAsync();
    Task SaveAsync(TrayBrowserConfig config);
    Task DeleteAsync(Guid id);
    Task SaveAllAsync(List<TrayBrowserConfig> configs);
    event EventHandler? ConfigsChanged;
}
