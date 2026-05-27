using System.Drawing;
using ZXing;
using ZXing.Windows.Compatibility;

namespace RegistroPontosSSG.Core.Security;

/// <summary>
/// Decodifica QR codes do tipo otpauth://totp/...?secret=XYZ&issuer=...
/// </summary>
public static class QrCodeReader
{
    public sealed record TotpInfo(string Secret, string Issuer, string Label);

    public static TotpInfo? ReadOtpAuthFromImage(string imagePath)
    {
        if (!File.Exists(imagePath)) return null;

        using var bitmap = (Bitmap)Image.FromFile(imagePath);
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };

        var result = reader.Decode(bitmap);
        if (result is null || string.IsNullOrWhiteSpace(result.Text)) return null;

        return ParseOtpAuthUri(result.Text);
    }

    public static TotpInfo? ParseOtpAuthUri(string uri)
    {
        // Formato esperado: otpauth://totp/Issuer:label?secret=XYZ&issuer=Issuer&...
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var parsed = new Uri(uri);
            var query = ParseQuery(parsed.Query);
            query.TryGetValue("secret", out var secret);
            query.TryGetValue("issuer", out var issuer);
            var label = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
            if (string.IsNullOrWhiteSpace(secret)) return null;
            return new TotpInfo(secret!, issuer ?? string.Empty, label);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        if (query.StartsWith('?')) query = query[1..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0) continue;
            var key = Uri.UnescapeDataString(pair[..idx]);
            var val = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = val;
        }
        return result;
    }
}
