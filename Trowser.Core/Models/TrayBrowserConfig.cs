namespace Trowser.Core.Models;

public enum IconMode
{
    FetchFavicon,
    CustomFile
}

public class TrayBrowserConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public IconMode IconMode { get; set; } = IconMode.FetchFavicon;
}
