using System.IO;
namespace Attener.Infrastructure.Database;

public static class DatabaseLocation
{
    public static string GetPath()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Attener");
        return Path.Combine(appDataDirectory, "attendance.db");
    }
}


