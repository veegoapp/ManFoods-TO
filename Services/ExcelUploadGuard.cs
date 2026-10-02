using System.IO.Compression;
using Microsoft.AspNetCore.Http;

namespace MvcApp.Services;

/// <summary>Why an uploaded workbook was refused.</summary>
public enum ExcelUploadProblem
{
    None,
    /// <summary>Larger than <see cref="ExcelUploadGuard.MaxFileBytes"/>.</summary>
    TooLarge,
    /// <summary>Wrong extension, or not actually an Excel workbook (bad signature, broken or non-workbook zip).</summary>
    NotExcel,
    /// <summary>A valid zip whose declared contents are far larger than the file itself (a "zip bomb"), or that holds an implausible number of parts.</summary>
    ExpandsTooMuch,
}

/// <summary>
/// The pre-parse checks every Excel upload goes through, before ClosedXML ever reads the file:
/// size, extension, file signature, and — for .xlsx, which is a zip archive — that the archive is a
/// real workbook whose declared uncompressed size is sane. A 10 MB .xlsx legitimately expands to a
/// few tens of MB; a crafted one can expand to gigabytes and exhaust the server's memory while being
/// parsed. Only the archive directory is read here; nothing is extracted. Uploaded files are kept in
/// the database (never on disk), and that is unchanged.
/// </summary>
public static class ExcelUploadGuard
{
    public const long MaxFileBytes = 10 * 1024 * 1024; // 10 MB
    /// <summary>Total declared uncompressed size allowed across the archive's parts. Real roster and
    /// projection workbooks are an order of magnitude below this.</summary>
    public const long MaxUncompressedBytes = 200L * 1024 * 1024; // 200 MB
    /// <summary>A workbook has a few dozen parts (one per sheet plus styles etc.); even the 12-month
    /// projection file is far below this.</summary>
    public const int MaxArchiveEntries = 1000;

    // .xlsx is a ZIP archive ("PK\x03\x04" etc.); legacy .xls is an OLE2
    // Compound File ("\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1"). Checking these
    // magic bytes catches a file that's merely been renamed to look like
    // Excel before it ever reaches ClosedXML — defense in depth on top of
    // the extension check, not a replacement for it.
    private static readonly byte[][] ZipSignatures =
    {
        new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        new byte[] { 0x50, 0x4B, 0x05, 0x06 },
        new byte[] { 0x50, 0x4B, 0x07, 0x08 },
    };
    private static readonly byte[] Ole2Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    public static async Task<ExcelUploadProblem> CheckAsync(IFormFile file, long maxUncompressedBytes = MaxUncompressedBytes)
    {
        if (file.Length > MaxFileBytes) return ExcelUploadProblem.TooLarge;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls") return ExcelUploadProblem.NotExcel;

        await using var stream = file.OpenReadStream();
        var header = new byte[8];
        var read = await stream.ReadAsync(header.AsMemory(0, 8));
        var isZip = read >= 4 && ZipSignatures.Any(sig => header.AsSpan(0, 4).SequenceEqual(sig));
        var isOle2 = read == 8 && header.AsSpan(0, 8).SequenceEqual(Ole2Signature);
        if (!isZip && !isOle2) return ExcelUploadProblem.NotExcel;
        if (!isZip) return ExcelUploadProblem.None;

        return CheckZipDirectory(stream, maxUncompressedBytes);
    }

    private static ExcelUploadProblem CheckZipDirectory(Stream stream, long maxUncompressedBytes)
    {
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxArchiveEntries) return ExcelUploadProblem.ExpandsTooMuch;

            long total = 0;
            var hasContentTypes = false;
            var hasWorkbook = false;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > maxUncompressedBytes) return ExcelUploadProblem.ExpandsTooMuch;
                hasContentTypes |= entry.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase);
                hasWorkbook |= entry.FullName.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase);
            }
            // A zip that merely has the right extension is not a workbook.
            return hasContentTypes && hasWorkbook ? ExcelUploadProblem.None : ExcelUploadProblem.NotExcel;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or NotSupportedException or OverflowException)
        {
            // A corrupt or truncated archive: the zip reader reports a damaged directory with several
            // exception types (InvalidData, ArgumentOutOfRange, EndOfStream...).
            return ExcelUploadProblem.NotExcel;
        }
    }
}
