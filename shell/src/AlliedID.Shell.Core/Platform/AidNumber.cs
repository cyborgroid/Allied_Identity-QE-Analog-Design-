namespace AlliedID.Shell.Core.Platform;

/// <summary>
/// AlliedID 번호 (국가번호 없는 12자리 + Luhn). Rust <c>aid-identity::AidNumber</c> 와 동일 규칙.
/// </summary>
public static class AidNumber
{
    public static string Digits(string input) =>
        new((input.StartsWith("AID", StringComparison.OrdinalIgnoreCase) ? input[3..] : input).Where(char.IsAsciiDigit).ToArray());

    public static bool IsValid(string input)
    {
        if (input.Contains('+')) return false; // 국가번호 개념 없음
        var d = Digits(input);
        return d.Length == 12 && CheckDigit(d[..11]) == d[11] - '0';
    }

    public static int CheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var d = body[body.Length - 1 - i] - '0';
            if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>입력 중인 숫자를 <c>4821-0937-5512</c> 형태로.</summary>
    public static string FormatPartial(string input)
    {
        var d = Digits(input);
        if (d.Length > 12) d = d[..12];
        var parts = Enumerable.Range(0, (d.Length + 3) / 4).Select(i => d.Substring(i * 4, Math.Min(4, d.Length - i * 4)));
        return string.Join('-', parts);
    }

    public static string Display(string digits) => "AID " + FormatPartial(digits);
}
