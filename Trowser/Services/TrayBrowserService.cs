using Trowser.Contracts.Services;
using Trowser.Core.Contracts.Services;
using Trowser.Core.Models;

namespace Trowser.Services;

public class TrayBrowserService : ITrayBrowserService
{
    private const string ConfigFileName = "TrayBrowsers.json";

    private readonly IFileService _fileService;
    private readonly string _dataFolder;
    private List<TrayBrowserConfig>? _configs;

    public event EventHandler? ConfigsChanged;

    public TrayBrowserService(IFileService fileService)
    {
        _fileService = fileService;
        _dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Trowser", "ApplicationData");
    }

    public async Task<List<TrayBrowserConfig>> GetAllAsync()
    {
        if (_configs == null)
        {
            _configs = await Task.Run(() =>
                _fileService.Read<List<TrayBrowserConfig>>(_dataFolder, ConfigFileName))
                ?? [];
        }

        return _configs;
    }

    public async Task SaveAsync(TrayBrowserConfig config)
    {
        var configs = await GetAllAsync();
        var existing = configs.FindIndex(c => c.Id == config.Id);
        if (existing >= 0)
        {
            configs[existing] = config;
        }
        else
        {
            configs.Add(config);
        }

        await PersistAsync();
        ConfigsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(Guid id)
    {
        var configs = await GetAllAsync();
        configs.RemoveAll(c => c.Id == id);
        await PersistAsync();
        ConfigsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAllAsync(List<TrayBrowserConfig> configs)
    {
        _configs = configs;
        await PersistAsync();
        ConfigsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task PersistAsync()
    {
        if (_configs != null)
        {
            await Task.Run(() => _fileService.Save(_dataFolder, ConfigFileName, _configs));
        }
    }
}
