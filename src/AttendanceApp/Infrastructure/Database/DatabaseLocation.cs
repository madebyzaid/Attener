using System.IO;
namespace AttendanceApp.Infrastructure.Database;

public static class DatabaseLocation
{
    public static string GetPath()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AttendanceApp");
        return Path.Combine(appDataDirectory, "attendance.db");
    }
}

