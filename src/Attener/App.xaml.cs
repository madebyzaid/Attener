using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Attener.Infrastructure.Database;

namespace Attener;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // WPF defaults to US formats (MM/DD/YYYY). Use en-GB so dates are DD/MM/YYYY everywhere.
        var culture = new CultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        new DatabaseInitializer(DatabaseLocation.GetPath()).Initialize();
        base.OnStartup(e);
    }
}