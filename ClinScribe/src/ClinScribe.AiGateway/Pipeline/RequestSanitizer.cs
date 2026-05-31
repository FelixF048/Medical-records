using System.Text;

namespace ClinScribe.AiGateway.Pipeline;

/// <summary>第九章 RequestSanitizer：去除控制字元、限制長度。</summary>
public sealed class RequestSanitizer
{
    private const int MaxLen = 20_000;

    public string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t') continue;
            sb.Append(ch);
        }
        var s = sb.ToString();
        return s.Length > MaxLen ? s[..MaxLen] : s;
    }
}
