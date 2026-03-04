using System.Text;
using System.Text.Json;

using Trowser.Core.Contracts.Services;

namespace Trowser.Core.Services;

public class FileService : IFileService
{
    public T? Read<T>(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json);
        }

        return default;
    }

    public void Save<T>(string folderPath, string fileName, T content)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        var fileContent = JsonSerializer.Serialize(content, options);
        File.WriteAllText(Path.Combine(folderPath, fileName), fileContent, Encoding.UTF8);
    }

    public void Delete(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (fileName != null && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
