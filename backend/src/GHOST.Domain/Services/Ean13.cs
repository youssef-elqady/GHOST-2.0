namespace GHOST.Domain.Services;

/// <summary>Internal barcodes for products without a manufacturer code (EAN-13, in-store prefix 200).</summary>
public static class Ean13
{
    public const string InStorePrefix = "200";

    /// <summary>Builds prefix + 9-digit running number + check digit.</summary>
    public static string FromSequence(long number)
    {
        if (number is < 1 or > 999_999_999)
            throw new ArgumentOutOfRangeException(nameof(number), "Barcode sequence must be between 1 and 999999999.");

        var body = InStorePrefix + number.ToString("D9");
        return body + CheckDigit(body);
    }

    public static int CheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || twelveDigits.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Exactly 12 digits are required.", nameof(twelveDigits));

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = twelveDigits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return (10 - sum % 10) % 10;
    }

    public static bool IsValid(string code)
        => code.Length == 13 && code.All(char.IsAsciiDigit) && CheckDigit(code[..12]) == code[12] - '0';
}
