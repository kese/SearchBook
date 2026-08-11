using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SearchBook.Models;
using SearchBook.Services;

namespace SearchBook;

public partial class MainWindow
{
    private string _inputDisplayName = "도서목록";
    private bool _loadedFromCheckpoint;

    private bool CanStartLookup =>
        Results.Count > 0 &&
        (!_loadedFromCheckpoint || Results.Any(LookupResultClassifier.ShouldRetry));

    private void ApplyFreshInput(RegistrationReadResult summary, string? sourcePath, string displayName)
    {
        Results.Clear();
        for (var index = 0; index < summary.Numbers.Count; index++)
        {
            Results.Add(new BookResult
            {
                Sequence = index + 1,
                RegistrationNumber = summary.Numbers[index]
            });
        }

        _inputPath = sourcePath;
        _inputDisplayName = displayName;
        _lastResultPath = null;
        _resumePendingOnly = false;
        _loadedFromCheckpoint = false;
        SelectedFileNameText.Text = displayName;
        LoadedCountText.Text =
            $"{summary.Numbers.Count:N0}건 · 중복 {summary.DuplicatesRemoved:N0}건 · 값 {summary.ValuesScanned:N0}개";
        SelectedFilePanel.Visibility = Visibility.Visible;
        CurrentStatusText.Text = $"{summary.Numbers.Count:N0}건을 불러왔습니다. 조회 시작을 눌러 주세요.";
        FooterStatusText.Text = sourcePath is null
            ? "클립보드 입력 준비 완료"
            : $"입력 준비 완료 · {sourcePath}";
        ResetProgressSurface(summary.Numbers.Count, success: 0, failed: 0);
        StartButton.IsEnabled = CanStartLookup;
        ExportButton.IsEnabled = false;
        OpenCurrentResultButton.IsEnabled = false;
        RetryUnconfirmedButton.IsEnabled = false;
        _resultsView.Refresh();
    }

    private void ApplyCheckpoint(IReadOnlyList<BookResult> rows, string path)
    {
        Results.Clear();
        foreach (var row in rows) Results.Add(row);

        _inputPath = path;
        _inputDisplayName = Path.GetFileNameWithoutExtension(path);
        _lastResultPath = path;
        _loadedFromCheckpoint = true;
        var retryCount = Results.Count(LookupResultClassifier.ShouldRetry);
        var confirmedCount = Results.Count - retryCount;
        _resumePendingOnly = retryCount > 0;
        SelectedFileNameText.Text = Path.GetFileName(path);
        LoadedCountText.Text = $"체크포인트 {Results.Count:N0}건 · 완료 {confirmedCount:N0}건 · 재조회 {retryCount:N0}건";
        SelectedFilePanel.Visibility = Visibility.Visible;
        CurrentStatusText.Text = retryCount > 0
            ? $"{retryCount:N0}건을 이어서 조회할 수 있습니다."
            : "모든 행이 완료된 결과 파일입니다.";
        FooterStatusText.Text = $"작업 재개 준비 · {path}";
        ResetProgressSurface(Results.Count, confirmedCount, retryCount);
        StartButton.IsEnabled = CanStartLookup;
        ExportButton.IsEnabled = true;
        OpenCurrentResultButton.IsEnabled = true;
        RetryUnconfirmedButton.IsEnabled = retryCount > 0;
        _resultsView.Refresh();
    }

    private void ResetProgressSurface(int total, int success, int failed)
    {
        ProgressMetricText.Text = $"0 / {total:N0}";
        SuccessMetricText.Text = success.ToString("N0");
        FailureMetricText.Text = failed.ToString("N0");
        RemainingMetricText.Text = "—";
        SpeedMetricText.Text = "— 건/분";
        SpeedGraphLine.Points.Clear();
        TransferStatusText.Text = "조회 대기 중";
        MainProgressBar.Value = 0;
    }

    private bool ConfirmReplaceCurrentResults()
    {
        if (!Results.Any(row => row.QueryState != LookupResultClassifier.Pending || row.CheckedAt is not null))
            return true;
        return MessageBox.Show(
            this,
            "현재 조회 결과를 새 입력으로 바꾸면 화면의 기존 결과가 사라집니다. 저장된 Excel 파일은 유지됩니다. 계속할까요?",
            "현재 결과 바꾸기",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private async void PasteInput_Click(object sender, RoutedEventArgs e) => await PasteInputFromClipboardAsync();

    private async Task PasteInputFromClipboardAsync()
    {
        if (_isRunning || !ConfirmReplaceCurrentResults()) return;
        if (!Clipboard.ContainsText())
        {
            MessageBox.Show(this, "클립보드에 텍스트가 없습니다.", "붙여넣기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetBusyForLoad(true);
        CurrentStatusText.Text = "클립보드에서 등록번호를 읽는 중입니다…";
        try
        {
            var content = Clipboard.GetText();
            var summary = await Task.Run(() => RegistrationNumberReader.ReadText(content));
            ApplyFreshInput(summary, sourcePath: null, "클립보드 입력");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "붙여넣기 입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusyForLoad(false);
        }
    }

    private async void Resume_Click(object sender, RoutedEventArgs e) => await ChooseCheckpointAsync();

    private async Task ChooseCheckpointAsync()
    {
        if (_isRunning || !ConfirmReplaceCurrentResults()) return;
        var dialog = new OpenFileDialog
        {
            Title = "SearchBook 작업 재개",
            Filter = "SearchBook Excel 결과 (*.xlsx)|*.xlsx",
            Multiselect = false,
            CheckFileExists = true,
            InitialDirectory = _appDataPaths.ResultsDirectory
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusyForLoad(true);
        CurrentStatusText.Text = "자동저장 결과를 읽는 중입니다…";
        try
        {
            var rows = await Task.Run(() => ResultWorkbookReader.Read(dialog.FileName));
            ApplyCheckpoint(rows, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "작업을 재개할 수 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusyForLoad(false);
        }
    }

    private bool FilterResult(object value)
    {
        if (value is not BookResult row) return false;
        var tag = (ResultFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var matchesState = tag switch
        {
            "success" => row.QueryState == LookupResultClassifier.Success,
            "failed" => row.QueryState == LookupResultClassifier.Failed,
            "pending" => row.QueryState == LookupResultClassifier.Pending,
            "running" => row.QueryState == LookupResultClassifier.Running,
            _ => true
        };
        if (!matchesState) return false;

        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0) return true;
        return Contains(row.RegistrationNumber, query) ||
               Contains(row.QueryState, query) ||
               Contains(row.BookState, query) ||
               Contains(row.Location, query) ||
               Contains(row.CallNumber, query) ||
               Contains(row.Title, query) ||
               Contains(row.Author, query) ||
               Contains(row.Publisher, query) ||
               Contains(row.Isbn, query) ||
               Contains(row.DataSource, query) ||
               Contains(row.Message, query);
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => _resultsView.Refresh();

    private void ResultFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized) _resultsView.Refresh();
    }

    private async void RetryUnconfirmed_Click(object sender, RoutedEventArgs e) => await BeginLookupAsync(retryOnly: true);

    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void Tools_Click(object sender, RoutedEventArgs e)
    {
        if (ToolsButton.ContextMenu is null) return;
        ToolsButton.ContextMenu.PlacementTarget = ToolsButton;
        ToolsButton.ContextMenu.IsOpen = true;
    }

    private void OpenResultsFolder_Click(object sender, RoutedEventArgs e)
    {
        _appDataPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo(_appDataPaths.ResultsDirectory) { UseShellExecute = true });
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            await app.CheckForUpdatesAsync(this);
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
            var path = DiagnosticReportWriter.Write(_appDataPaths, version, _inputDisplayName, Results.ToList());
            FooterStatusText.Text = $"진단 정보 저장 완료 · {path}";
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add($"/select,{path}");
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "진단 정보 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isRunning)
        {
            Cancel_Click(CancelButton, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            ChooseFile();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.O)
        {
            _ = ChooseCheckpointAsync();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.V)
        {
            _ = PasteInputFromClipboardAsync();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.R)
        {
            _ = BeginLookupAsync(retryOnly: true);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            e.Handled = true;
        }
    }

    private void UpdateActionAvailability()
    {
        var hasRows = Results.Count > 0;
        StartButton.IsEnabled = !_isRunning && CanStartLookup;
        ExportButton.IsEnabled = !_isRunning && hasRows;
        OpenCurrentResultButton.IsEnabled = hasRows;
        RetryUnconfirmedButton.IsEnabled = !_isRunning && Results.Any(LookupResultClassifier.ShouldRetry);
        OpenCurrentResultText.Text = _isRunning ? "작업 중 결과 Excel 열기" : "현재 결과 Excel 열기";
    }
}
