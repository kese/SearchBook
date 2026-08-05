namespace SearchBook.Models;

public sealed record LookupSettings(
    int DelayMilliseconds = 700,
    int MaxRetries = 2,
    int AutosaveEvery = 50);
