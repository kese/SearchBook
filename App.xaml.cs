using System.Text;
using System.IO;
using System.Windows;

namespace SearchBook;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var inputPath = e.Args.FirstOrDefault(File.Exists);
        if (inputPath is not null)
        {
            EventHandler? handler = null;
            handler = async (_, _) =>
            {
                window.ContentRendered -= handler;
                await window.LoadInputFromPathAsync(inputPath);
            };
            window.ContentRendered += handler;
        }
    }
}
