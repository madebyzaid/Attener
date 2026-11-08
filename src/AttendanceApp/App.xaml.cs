using System.IO;
using AttendanceApp.Infrastructure.Database;

namespace AttendanceApp;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        new DatabaseInitializer(DatabaseLocation.GetPath()).Initialize();
        base.OnStartup(e);
    }
}
