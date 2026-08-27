using SearchBook.Models;

namespace SearchBook.Services;

public static class LookupRunState
{
    public static IReadOnlyList<BookResult> SelectRows(
        IEnumerable<BookResult> rows,
        bool retryOnly) =>
        retryOnly
            ? rows.Where(LookupResultClassifier.ShouldRetry).ToList()
            : rows.ToList();

    public static bool ShouldResumePendingOnly(
        bool wasCanceled,
        IEnumerable<BookResult> rows) =>
        wasCanceled && rows.Any(LookupResultClassifier.ShouldRetry);

    public static void PrepareRow(BookResult row)
    {
        row.QueryState = LookupResultClassifier.Pending;
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
        row.ReferenceComparison = ReferenceComparisonClassifier.Pending;
        row.ReferenceComparisonDetails = "";
        row.Message = "";
        row.CheckedAt = null;
    }
}
