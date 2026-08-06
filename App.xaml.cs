using System.Text;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using SearchBook.Services;
using DrawingIcon = System.Drawing.Icon;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace SearchBook;

public partial class App : System.Windows.Application
{
    private FormsNotifyIcon? _trayIcon;
    private FormsContextMenuStrip? _trayMenu;
    private FormsToolStripMenuItem? _updateItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        InitializeTrayIcon(window);

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

    private void InitializeTrayIcon(Window window)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SearchBookTray.ico");
        if (!File.Exists(iconPath)) return;

        var openItem = new FormsToolStripMenuItem("SearchBook 열기");
        openItem.Click += (_, _) => RestoreWindow(window);
        _updateItem = new FormsToolStripMenuItem("업데이트 확인");
        _updateItem.Click += async (_, _) => await CheckForUpdatesAsync(window);
        var exitItem = new FormsToolStripMenuItem("종료");
        exitItem.Click += (_, _) => Dispatcher.Invoke(Shutdown);
        _trayMenu = new FormsContextMenuStrip();
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(_updateItem);
        _trayMenu.Items.Add(new FormsToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _trayIcon = new FormsNotifyIcon
        {
            Icon = new DrawingIcon(iconPath),
            Text = "SearchBook · 도서상태 자동 조회",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow(window);
    }

    private async Task CheckForUpdatesAsync(Window owner)
    {
        if (_updateItem is null || !_updateItem.Enabled) return;
        _updateItem.Enabled = false;
        _updateItem.Text = "업데이트 확인 중…";

        try
        {
            var token = Environment.GetEnvironmentVariable("SEARCHBOOK_GITHUB_TOKEN");
            using var updateService = new GitHubUpdateService(token);
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
            var result = await updateService.CheckAsync(currentVersion);

            if (result.Status == UpdateCheckStatus.UpToDate)
            {
                System.Windows.MessageBox.Show(
                    owner,
                    $"현재 버전 {FormatVersion(currentVersion)}이 최신입니다.",
                    "업데이트 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Asset is not null)
            {
                var answer = System.Windows.MessageBox.Show(
                    owner,
                    $"새 버전 {result.TagName}을 사용할 수 있습니다.\n\n" +
                    $"{result.Asset.Name} ({FormatBytes(result.Asset.Size)})을 다운로드할까요?\n\n" +
                    "다운로드 후 ZIP을 직접 압축 해제해야 하며, 실행 중인 프로그램은 자동으로 덮어쓰지 않습니다.",
                    "SearchBook 업데이트",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                _updateItem.Text = "업데이트 다운로드 중…";
                var safeTag = string.Concat(result.TagName.Select(character =>
                    char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_'));
                var destination = Path.Combine(AppContext.BaseDirectory, "updates", safeTag, result.Asset.Name);
                var downloadedPath = await updateService.DownloadAsync(result.Asset, destination);
                System.Windows.MessageBox.Show(
                    owner,
                    $"업데이트를 다운로드하고 SHA-256 검증을 완료했습니다.\n\n{downloadedPath}",
                    "업데이트 다운로드 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{downloadedPath}\"") { UseShellExecute = true });
                return;
            }

            var openReleasePage = System.Windows.MessageBox.Show(
                owner,
                result.Message + "\n\nGitHub 릴리즈 페이지를 열까요?",
                "업데이트 확인",
                MessageBoxButton.YesNo,
                result.Status == UpdateCheckStatus.AuthenticationRequired ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (openReleasePage == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(result.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                owner,
                $"업데이트 확인 중 오류가 발생했습니다.\n\n{ex.GetBaseException().Message}",
                "업데이트 확인 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (_updateItem is not null)
            {
                _updateItem.Text = "업데이트 확인";
                _updateItem.Enabled = true;
            }
        }
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.0} MB"
        : $"{bytes / 1024d:0.0} KB";

    private void RestoreWindow(Window window)
    {
        Dispatcher.Invoke(() =>
        {
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayMenu?.Dispose();
        _updateItem = null;
        base.OnExit(e);
    }
}
