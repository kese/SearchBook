using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SearchBook.Models;

public sealed class BookResult : INotifyPropertyChanged
{
    private string _queryState = "대기";
    private string _bookState = "";
    private string _returnDue = "";
    private string _location = "";
    private string _callNumber = "";
    private string _title = "";
    private string _bibliographicInfo = "";
    private string _detailUrl = "";
    private string _message = "";
    private DateTime? _checkedAt;

    public int Sequence { get; init; }
    public required string RegistrationNumber { get; init; }
    public string QueryState { get => _queryState; set => Set(ref _queryState, value); }
    public string BookState { get => _bookState; set => Set(ref _bookState, value); }
    public string ReturnDue { get => _returnDue; set => Set(ref _returnDue, value); }
    public string Location { get => _location; set => Set(ref _location, value); }
    public string CallNumber { get => _callNumber; set => Set(ref _callNumber, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string BibliographicInfo { get => _bibliographicInfo; set => Set(ref _bibliographicInfo, value); }
    public string DetailUrl { get => _detailUrl; set => Set(ref _detailUrl, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public DateTime? CheckedAt { get => _checkedAt; set => Set(ref _checkedAt, value); }
    public bool IsSuccess => QueryState == "성공";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(QueryState))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSuccess)));
    }
}

public sealed record LookupProgress(int Completed, int Total, int Success, int Failed, string CurrentNumber);
