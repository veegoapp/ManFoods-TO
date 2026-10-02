using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using MvcApp.Resources;

namespace MvcApp.Tests.TestHelpers;

/// <summary>Returns the resource key as the text (so tests can assert which message was chosen).</summary>
public sealed class KeyLocalizer : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
}

/// <summary>Builders for uploaded-file test data.</summary>
public static class TestFiles
{
    public static IFormFile Form(byte[] bytes, string fileName) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);

    public static byte[] Workbook(string[] headers, IEnumerable<string[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++) ws.Cell(r, c + 1).Value = row[c];
            r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static readonly string[] UserHeaders = { "Email", "Phone", "Assigned Name", "Role" };

    /// <summary>A zip that looks like a workbook (has the two required parts) plus a highly compressible part declaring
    /// <paramref name="bombBytes"/> bytes once unpacked — a few KB on disk.</summary>
    public static byte[] ZipWithPayload(long bombBytes, int extraEntries = 0, bool workbookParts = true)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (workbookParts)
            {
                foreach (var name in new[] { "[Content_Types].xml", "xl/workbook.xml" })
                    using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write("<x/>");
            }
            if (bombBytes > 0)
            {
                using var s = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize).Open();
                var chunk = new byte[1024 * 1024];
                for (long written = 0; written < bombBytes; written += chunk.Length)
                    s.Write(chunk, 0, (int)Math.Min(chunk.Length, bombBytes - written));
            }
            for (var i = 0; i < extraEntries; i++)
                using (var w = new StreamWriter(zip.CreateEntry($"xl/extra/part{i}.xml").Open())) w.Write("x");
        }
        return ms.ToArray();
    }
}

/// <summary>Collects the formatted text of every log entry (test-only).</summary>
public sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider, Microsoft.Extensions.Logging.ILogger
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();
    public IReadOnlyCollection<string> Lines => _lines.ToArray();
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _lines.Enqueue($"{logLevel}: {formatter(state, exception)}");
    public void Dispose() { }
}
