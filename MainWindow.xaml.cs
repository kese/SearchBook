using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SearchBook.Models;
using SearchBook.Services;

namespace SearchBook;

public partial class MainWindow : Window
{
    private static readonly string[] SupportedExtensions = [".xlsx", ".txt", ".csv", ".tsv"];
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _inputPath;
    private string? _lastResultPath;
    private bool _isRunning;

    public ObservableCollection<BookResult> Results { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Window_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = HasSupportedFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        DropZoneBorder.BorderBrush = e.Effects == DragDropEffects.Copy
            ? new SolidColorBrush(Color.FromRgb(49, 107, 255))
            : new SolidColorBrush(Color.FromRgb(185, 200, 232));
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(185, 200, 232));
        if (!HasSupportedFile(e.Data)) return;
        var path = ((string[])e.Data.GetData(DataFormats.FileDrop)!)[0];
        await LoadInputFileAsync(path);
    }

    private static bool HasSupportedFile(System.Windows.IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
        var paths = data.GetData(DataFormats.FileDrop) as string[];
        return paths is { Length: > 0 } && File.Exists(paths[0]) &&
               SupportedExtensions.Contains(Path.GetExtension(paths[0]), StringComparer.OrdinalIgnoreCase);
    }

    private void DropZone_Click(object sender, MouseButtonEventArgs e) => ChooseFile();
    private void ChooseFile_Click(object sender, RoutedEventArgs e) => ChooseFile();

    private void ChooseFile()
    {
        if (_isRunning) return;
        var dialog = new OpenFileDialog
        {
            Title = "등록번호 파일 선택",
            Filter = "지원 파일 (*.xlsx;*.txt;*.csv;*.tsv)|*.xlsx;*.txt;*.csv;*.tsv|Excel 파일 (*.xlsx)|*.xlsx|텍스트 파일 (*.txt;*.csv;*.tsv)|*.txt;*.csv;*.tsv",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) _ = LoadInputFileAsync(dialog.FileName);
    }

    private async Task LoadInputFileAsync(string path)
    {
        if (_isRunning) return;
        SetBusyForLoad(true);
        CurrentStatusText.Text = "등록번호를 읽는 중입니다…";
        try
        {
            var numbers = await Task.Run(() => RegistrationNumberReader.Read(path));
            Results.Clear();
            for (var i = 0; i < numbers.Count; i++)
                Results.Add(new BookResult { Sequence = i + 1, RegistrationNumber = numbers[i] });

            _inputPath = path;
            _lastResultPath = null;
            SelectedFileNameText.Text = Path.GetFileName(path);
            LoadedCountText.Text = $"중복을 제외한 등록번호 {numbers.Count:N0}건";
            SelectedFilePanel.Visibility = Visibility.Visible;
            CurrentStatusText.Text = $"{numbers.Count:N0}건을 불러왔습니다. 조회 시작을 눌러 주세요.";
            FooterStatusText.Text = $"입력 준비 완료 · {path}";
            ProgressMetricText.Text = $"0 / {numbers.Count:N0}";
            SuccessMetricText.Text = "0";
            FailureMetricText.Text = "0";
            RemainingMetricText.Text = "—";
            MainProgressBar.Value = 0;
            StartButton.IsEnabled = true;
            ExportButton.IsEnabled = false;
            OpenResultsButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "파일을 읽을 수 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
            CurrentStatusText.Text = "지원되는 입력 파일을 선택해 주세요.";
        }
        finally
        {
            SetBusyForLoad(false);
        }
    }

    public Task LoadInputFromPathAsync(string path) => LoadInputFileAsync(path);

    private void SetBusyForLoad(bool busy)
    {
        DropZoneBorder.IsEnabled = !busy;
        StartButton.IsEnabled = !busy && Results.Count > 0;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || Results.Count == 0 || string.IsNullOrWhiteSpace(_inputPath)) return;

        var selectedItem = DelayComboBox.SelectedItem as ComboBoxItem;
        var delay = int.TryParse(selectedItem?.Tag?.ToString(), out var parsedDelay) ? parsedDelay : 700;
        var settings = new LookupSettings(delay, MaxRetries: 2, AutosaveEvery: 50);
        var resultsDirectory = Path.Combine(AppContext.BaseDirectory, "results");
        Directory.CreateDirectory(resultsDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var stem = MakeSafeFileName(Path.GetFileNameWithoutExtension(_inputPath));
        _lastResultPath = Path.Combine(resultsDirectory, $"{stem}_도서상태_{stamp}.xlsx");

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        _isRunning = true;
        ToggleRunningState(true);
        ResetRowsForRun();

        var success = 0;
        var failed = 0;
        var completed = 0;
        var stopwatch = Stopwatch.StartNew();

        using var client = new LibraryClient(settings);
        var localStatusCatalog = new LocalBookStatusCatalog(Path.Combine(AppContext.BaseDirectory, "docs"));
        try
        {
            foreach (var row in Results)
            {
                token.ThrowIfCancellationRequested();
                row.QueryState = "조회 중";
                row.Message = "도서관 서버 조회 중";
                CurrentStatusText.Text = $"{row.RegistrationNumber} 조회 중…";

                try
                {
                    var data = await client.LookupAsync(row.RegistrationNumber, token);
                    data = LookupFallbackResolver.Resolve(row.RegistrationNumber, data, localStatusCatalog);
                    ApplyLookup(row, data);
                    if (data.Success) success++; else failed++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    row.QueryState = "중지됨";
                    row.Message = "사용자가 조회를 중지했습니다.";
                    throw;
                }
                catch (Exception ex)
                {
                    var data = LookupFallbackResolver.Resolve(
                        row.RegistrationNumber,
                        LookupData.NotFound($"도서관 조회 실패: {CompactError(ex)}"),
                        localStatusCatalog);
                    ApplyLookup(row, data);
                    if (data.Success) success++; else failed++;
                }

                completed++;
                UpdateProgress(completed, Results.Count, success, failed, stopwatch.Elapsed, row.RegistrationNumber);

                if (completed % settings.AutosaveEvery == 0)
                {
                    CurrentStatusText.Text = $"{completed:N0}건 완료 · 중간 결과 저장 중…";
                    await SaveResultsAsync(_lastResultPath);
                }

                if (completed % 10 == 0) ResultsDataGrid.ScrollIntoView(row);
            }

            await SaveResultsAsync(_lastResultPath);
            CurrentStatusText.Text = $"조회가 완료되었습니다. 성공 {success:N0}건, 미확인/실패 {failed:N0}건";
            FooterStatusText.Text = $"완료 · {_lastResultPath}";
            MessageBox.Show(this,
                $"조회가 완료되었습니다.\n\n성공: {success:N0}건\n미확인/실패: {failed:N0}건\n\n결과: {_lastResultPath}",
                "조회 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            await SaveResultsAsync(_lastResultPath);
            CurrentStatusText.Text = $"조회가 중지되었습니다. 완료된 {completed:N0}건까지 저장했습니다.";
            FooterStatusText.Text = $"중지됨 · 중간 결과 보존 · {_lastResultPath}";
        }
        catch (Exception ex)
        {
            try { await SaveResultsAsync(_lastResultPath); } catch { }
            CurrentStatusText.Text = "예기치 않은 오류로 중단되었습니다. 처리된 결과는 보존했습니다.";
            MessageBox.Show(this, ex.Message, "조회 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            stopwatch.Stop();
            _isRunning = false;
            ToggleRunningState(false);
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            ExportButton.IsEnabled = Results.Count > 0;
            OpenResultsButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastResultPath) && File.Exists(_lastResultPath);
        }
    }

    private static void ApplyLookup(BookResult row, LookupData data)
    {
        row.QueryState = data.Success ? "성공" : "미확인";
        row.BookState = data.BookState;
        row.ReturnDue = data.ReturnDue;
        row.Location = data.Location;
        row.CallNumber = data.CallNumber;
        row.Title = data.Title;
        row.Author = data.Author;
        row.Publisher = data.Publisher;
        row.PublicationYear = data.PublicationYear;
        row.Isbn = data.Isbn;
        row.CatalogLastChanged = data.CatalogLastChanged;
        row.BibliographicInfo = data.BibliographicInfo;
        row.DetailUrl = data.DetailUrl;
        row.DataSource = data.DataSource;
        row.Message = data.Message;
        row.CheckedAt = DateTime.Now;
    }

    private void ResetRowsForRun()
    {
        foreach (var row in Results)
        {
            row.QueryState = "대기";
            row.BookState = "";
            row.ReturnDue = "";
            row.Location = "";
            row.CallNumber = "";
            row.Title = "";
            row.Author = "";
            row.Publisher = "";
            row.PublicationYear = "";
            row.Isbn = "";
            row.CatalogLastChanged = "";
            row.BibliographicInfo = "";
            row.DetailUrl = "";
            row.DataSource = "";
            row.Message = "";
            row.CheckedAt = null;
        }
    }

    private void UpdateProgress(int completed, int total, int success, int failed, TimeSpan elapsed, string current)
    {
        ProgressMetricText.Text = $"{completed:N0} / {total:N0}";
        SuccessMetricText.Text = success.ToString("N0");
        FailureMetricText.Text = failed.ToString("N0");
        MainProgressBar.Value = total == 0 ? 0 : completed * 100d / total;
        CurrentStatusText.Text = $"{current} 완료 · 전체 {completed * 100d / total:0.0}%";

        if (completed > 0)
        {
            var remaining = TimeSpan.FromTicks((long)(elapsed.Ticks / (double)completed * (total - completed)));
            RemainingMetricText.Text = FormatDuration(remaining);
        }
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}시간 {value.Minutes}분";
        if (value.TotalMinutes >= 1) return $"{(int)value.TotalMinutes}분 {value.Seconds}초";
        return $"{Math.Max(0, value.Seconds)}초";
    }

    private void ToggleRunningState(bool running)
    {
        StartButton.IsEnabled = !running && Results.Count > 0;
        CancelButton.IsEnabled = running;
        DelayComboBox.IsEnabled = !running;
        DropZoneBorder.IsEnabled = !running;
        SelectedFilePanel.IsEnabled = !running;
        ExportButton.IsEnabled = !running && Results.Count > 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRunning || _cancellationTokenSource is null) return;
        CancelButton.IsEnabled = false;
        CurrentStatusText.Text = "현재 요청을 마치는 대로 중지하고 결과를 저장합니다…";
        _cancellationTokenSource.Cancel();
    }

    private async Task SaveResultsAsync(string path)
    {
        var snapshot = Results.ToList();
        await Task.Run(() => XlsxExporter.Write(path, snapshot));
        ExportButton.IsEnabled = true;
        OpenResultsButton.IsEnabled = true;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Results.Count == 0) return;
        var defaultName = _lastResultPath is null
            ? $"도서상태_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            : Path.GetFileName(_lastResultPath);
        var dialog = new SaveFileDialog
        {
            Title = "조회 결과 저장",
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = defaultName
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await SaveResultsAsync(dialog.FileName);
            _lastResultPath = dialog.FileName;
            FooterStatusText.Text = $"결과 저장 완료 · {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "입력 템플릿 저장",
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = "SearchBook_입력템플릿.xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            XlsxExporter.WriteTemplate(dialog.FileName);
            FooterStatusText.Text = $"입력 템플릿 저장 완료 · {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "템플릿 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenResults_Click(object sender, RoutedEventArgs e)
    {
        var directory = _lastResultPath is null ? null : Path.GetDirectoryName(_lastResultPath);
        if (directory is null || !Directory.Exists(directory)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "도서목록" : value;
    }

    private static string CompactError(Exception exception)
    {
        var message = exception.GetBaseException().Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 180 ? message : message[..180] + "…";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isRunning)
        {
            var answer = MessageBox.Show(this,
                "조회가 진행 중입니다. 종료하면 현재 요청을 취소하고 마지막 자동저장 지점까지 결과가 남습니다. 종료할까요?",
                "조회 중 종료", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cancellationTokenSource?.Cancel();
        }
        base.OnClosing(e);
    }
}
