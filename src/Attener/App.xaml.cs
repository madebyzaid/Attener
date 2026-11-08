using System.IO;
using Attener.Infrastructure.Database;

namespace Attener;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        new DatabaseInitializer(DatabaseLocation.GetPath()).Initialize();
        base.OnStartup(e);
    }
}

