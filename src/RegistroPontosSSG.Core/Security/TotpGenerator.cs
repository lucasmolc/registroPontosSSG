using OtpNet;

namespace RegistroPontosSSG.Core.Security;

/// <summary>
/// Gera códigos TOTP compatíveis com Microsoft/Google Authenticator.
/// </summary>
public static class TotpGenerator
{
    public static string GenerateCode(string secretBase32)
    {
        if (string.IsNullOrWhiteSpace(secretBase32))
            throw new ArgumentException("Secret TOTP vazia.", nameof(secretBase32));

        var secretBytes = Base32Encoding.ToBytes(secretBase32.Trim().Replace(" ", ""));
        var totp = new Totp(secretBytes);
        return totp.ComputeTotp();
    }

    public static int RemainingSeconds()
    {
        var totp = new Totp(new byte[10]);
        return totp.RemainingSeconds();
    }

    public static bool IsValidSecret(string secretBase32)
    {
        if (string.IsNullOrWhiteSpace(secretBase32)) return false;
        try
        {
            _ = Base32Encoding.ToBytes(secretBase32.Trim().Replace(" ", ""));
            return true;
        }
        catch { return false; }
    }
}
