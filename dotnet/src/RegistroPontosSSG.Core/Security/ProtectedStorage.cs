using System.Security.Cryptography;
using System.Text;

namespace RegistroPontosSSG.Core.Security;

/// <summary>
/// Criptografia DPAPI por usuário Windows — só descriptografa na conta que criptografou.
/// </summary>
public static class ProtectedStorage
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RegistroPontosSSG.v1");

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var data = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string base64Cipher)
    {
        if (string.IsNullOrEmpty(base64Cipher)) return string.Empty;
        try
        {
            var encrypted = Convert.FromBase64String(base64Cipher);
            var data = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return string.Empty;
        }
    }
}
