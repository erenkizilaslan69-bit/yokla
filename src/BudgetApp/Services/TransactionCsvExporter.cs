using System.Text;
using BudgetApp.Models;

namespace BudgetApp.Services;

public interface ITransactionCsvExporter
{
    /// <summary>
    /// The user's transactions of one month as a CSV file ready for Turkish Excel: UTF-8 with a byte order mark,
    /// semicolon separated, decimal comma. Columns: Tarih;Tür;Kategori;Ürün;Not;Tutar. Oldest first.
    /// </summary>
    Task<byte[]> ExportMonthAsync(string userId, int year, int month);
}

public class TransactionCsvExporter(ITransactionService transactions) : ITransactionCsvExporter
{
    private const char Separator = ';';
    private const string NewLine = "\r\n";
    private static readonly string[] Header = ["Tarih", "Tür", "Kategori", "Ürün", "Not", "Tutar"];
    private static readonly char[] NeedsQuoting = [Separator, '"', '\r', '\n'];

    // UTF8Encoding(true) writes the BOM; Excel needs it to recognise UTF-8 (otherwise ü, ş, ğ turn into garbage).
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task<byte[]> ExportMonthAsync(string userId, int year, int month)
    {
        // The list screen shows newest first; a spreadsheet reads better oldest first.
        var rows = (await transactions.GetByMonthAsync(userId, year, month))
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Id)
            .ToList();

        var text = new StringBuilder();
        AppendLine(text, Header);
        foreach (var t in rows)
        {
            AppendLine(text,
            [
                t.Date.ToString("dd.MM.yyyy", Tr.Culture),
                t.Type == TransactionType.Income ? "Gelir" : "Gider",
                t.Category?.Name,
                t.ItemName,
                t.Note,
                // Always positive (the "Tür" column gives the direction); fixed two decimals with a decimal comma
                // and no thousands separator so Excel reads it as a number.
                t.Amount.ToString("0.00", Tr.Culture)
            ]);
        }

        return [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(text.ToString())];
    }

    private static void AppendLine(StringBuilder text, IEnumerable<string?> fields)
    {
        text.AppendJoin(Separator, fields.Select(Escape));
        text.Append(NewLine);
    }

    /// <summary>Makes one field safe: no spreadsheet formulas, and quoted when it contains the separator, a quote or a line break.</summary>
    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        // A cell starting with = + - @ (or a tab/CR) is run as a formula by Excel and friends; a leading
        // apostrophe makes it plain text. Matters when the file is shared with someone else.
        if (field[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            field = "'" + field;

        return field.IndexOfAny(NeedsQuoting) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
    }
}
