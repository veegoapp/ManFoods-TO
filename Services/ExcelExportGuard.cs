using ClosedXML.Excel;

namespace MvcApp.Services;

/// <summary>
/// Spreadsheet formula-injection protection for every workbook the app generates and hands out (reports, the
/// "Default Passwords" file). A text cell whose first character is =, +, -, @, tab or carriage return — typically an
/// employee/store name or comment taken from an uploaded file, or a generated temporary password that happens to start
/// with one of those symbols — is turned into a formula when someone edits it in Excel (F2 + Enter), and can be
/// misread by other tools. Such cells are marked with Excel's "quote prefix" instead: the cell keeps exactly the same
/// text (nothing is added, removed or altered — a password stays a valid password), is displayed unchanged, and Excel
/// treats it as plain text even when edited.
///
/// Real formulas the app writes on purpose (FormulaA1 in the reports) and numeric/date cells are never touched.
/// </summary>
public static class ExcelExportGuard
{
    private static readonly char[] Triggers = { '=', '+', '-', '@', '\t', '\r' };

    public static XLWorkbook Neutralize(XLWorkbook workbook)
    {
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var cell in sheet.CellsUsed())
            {
                if (cell.HasFormula || cell.DataType != XLDataType.Text) continue;
                var text = cell.GetString();
                if (text.Length > 0 && Array.IndexOf(Triggers, text[0]) >= 0)
                    cell.Style.IncludeQuotePrefix = true;
            }
        }
        return workbook;
    }
}
