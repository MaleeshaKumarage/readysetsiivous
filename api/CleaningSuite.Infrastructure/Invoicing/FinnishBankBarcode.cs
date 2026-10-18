using System.Globalization;
using System.Text;
using CleaningSuite.Domain.Invoicing;

namespace CleaningSuite.Infrastructure.Invoicing;

/// <summary>Finance Finland Bank Bar Code Guide v5.3, symbol version 4 (national reference).
/// https://www.finanssiala.fi/wp-content/uploads/2021/03/Bank_bar_code_guide.pdf
/// Fixed Code 128 Set C. No provider, network request or invoice data mutation.</summary>
public static class FinnishBankBarcode
{
    public const decimal BarWidthMm = 105, BarHeightMm = 12, QuietZoneMm = 20, BottomBlankMm = 5;
    // ISO/IEC 15417 symbol run widths, alternating bar/space (stop has seven runs).
    private static readonly string[] Patterns = "212222,222122,222221,121223,121322,131222,122213,122312,132212,221213,221312,231212,112232,122132,122231,113222,123122,123221,223211,221132,221231,213212,223112,312131,311222,321122,321221,312212,322112,322211,212123,212321,232121,111323,131123,131321,112313,132113,132311,211313,231113,231311,112133,112331,132131,113123,113321,133121,313121,211331,231131,213113,213311,213131,311123,311321,331121,312113,312311,332111,314111,221411,431111,111224,111422,121124,121421,141122,141221,112214,112412,122114,122411,142112,142211,241211,221114,413111,241112,134111,111242,121142,121241,114212,124112,124211,411212,421112,421211,212141,214121,412121,111143,111341,131141,114113,114311,411113,411311,113141,114131,311141,411131,211412,211214,211232,2331112".Split(',');

    public static string? ForInvoice(Invoice invoice) => invoice.IssuerIsSample || invoice.Status != Invoice.StatusIssued || invoice.Currency != "EUR"
        ? null : CreatePayload(invoice.Issuer.BankAccountIBAN, invoice.PaymentReference, invoice.Total.Gross, invoice.DueDate);

    public static string? CreatePayload(string? iban, string? reference, decimal amount, DateTime due)
    {
        var account = (iban ?? "").Replace(" ", "").ToUpperInvariant();
        var number = (reference ?? "").Replace(" ", "");
        if (account.Length != 18 || !account.StartsWith("FI", StringComparison.Ordinal) || !Digits(account[2..]) || account[4..].All(c => c == '0')) return null;
        var check = account[4..] + "1518" + account.Substring(2, 2); var remainder = 0;
        foreach (var digit in check) remainder = (remainder * 10 + digit - '0') % 97;
        if (remainder != 1 || number.Length is < 4 or > 20 || !Digits(number) || number[0] == '0') return null;
        var sum = 0; var weights = new[] { 7, 3, 1 }; var position = 0;
        foreach (var digit in number[..^1].Reverse()) sum += (digit - '0') * weights[position++ % 3];
        if ((10 - sum % 10) % 10 != number[^1] - '0') return null;
        if (amount <= 0 || amount > 999999.99m || amount != decimal.Round(amount, 2) || due.Year is < 2000 or > 2099) return null;
        return "4" + account[2..] + (amount * 100).ToString("00000000", CultureInfo.InvariantCulture) + "000" + number.PadLeft(20, '0') + due.ToString("yyMMdd", CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<int> Codewords(string payload)
    {
        if (payload.Length != 54 || !Digits(payload)) throw new ArgumentException("A bank barcode must contain exactly 54 ASCII digits.", nameof(payload));
        var words = new List<int> { 105 }; var checksum = 105;
        for (var i = 0; i < 27; i++) { var value = int.Parse(payload.AsSpan(i * 2, 2), CultureInfo.InvariantCulture); words.Add(value); checksum += value * (i + 1); }
        words.Add(checksum % 103); words.Add(106); return words;
    }

    /// <summary>145×17 mm including 20 mm side quiet zones and 5 mm blank below. No human-readable payload.</summary>
    public static string Svg(string payload)
    {
        var words = Codewords(payload);
        var modules = words.Sum(w => Patterns[w].Sum(c => c - '0'));
        var scale = BarWidthMm / modules; decimal x = QuietZoneMm;
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"145mm\" height=\"17mm\" viewBox=\"0 0 145 17\"><rect width=\"145\" height=\"17\" fill=\"white\"/>");
        foreach (var word in words)
        {
            var bar = true;
            foreach (var run in Patterns[word])
            {
                var width = (run - '0') * scale;
                if (bar) svg.Append(FormattableString.Invariant($"<rect x=\"{x:0.########}\" y=\"0\" width=\"{width:0.########}\" height=\"12\" fill=\"black\"/>"));
                x += width; bar = !bar;
            }
        }
        return svg.Append("</svg>").ToString();
    }
    private static bool Digits(string value) => value.All(c => c is >= '0' and <= '9');
}
