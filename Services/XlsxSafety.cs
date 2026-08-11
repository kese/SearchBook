using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace SearchBook.Services;

public static class XlsxSafety
{
    private const long MaximumArchiveBytes = 128L * 1024 * 1024;
    private const long MaximumEntryBytes = 64L * 1024 * 1024;
    private const long MaximumExpandedBytes = 256L * 1024 * 1024;
    private const int MaximumEntries = 2048;
    private const double MaximumCompressionRatio = 200;

    public static void ValidateFile(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaximumArchiveBytes)
            throw new InvalidDataException("Excel 파일이 허용된 128MB 크기를 초과합니다.");
    }

    public static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException("Excel 파일에 항목이 지나치게 많습니다.");

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumEntryBytes)
                throw new InvalidDataException($"Excel 내부 파일이 허용된 64MB 크기를 초과합니다: {entry.FullName}");

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
                throw new InvalidDataException("Excel 압축 해제 크기가 허용된 256MB를 초과합니다.");

            if (entry.Length < 1024 * 1024) continue;
            var ratio = entry.Length / (double)Math.Max(entry.CompressedLength, 1);
            if (ratio > MaximumCompressionRatio)
                throw new InvalidDataException($"Excel 압축률이 비정상적으로 높습니다: {entry.FullName}");
        }
    }

    public static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumEntryBytes,
            IgnoreComments = true
        });
        return XDocument.Load(reader, LoadOptions.None);
    }
}
