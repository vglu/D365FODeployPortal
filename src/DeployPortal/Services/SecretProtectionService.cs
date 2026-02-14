using Microsoft.AspNetCore.DataProtection;

namespace DeployPortal.Services;

public class SecretProtectionService : ISecretProtectionService
{
    private readonly IDataProtector _protector;

    public SecretProtectionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("DeployPortal.Secrets.v1");
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        return _protector.Protect(plainText);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        return _protector.Unprotect(cipherText);
    }

    public string MaskSecret(string secret) => MaskSecretCore(secret);

    /// <summary>Static helper for callers that only need masking (e.g. UI).</summary>
    public static string MaskSecretCore(string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length <= 4)
            return "****";

        return $"****{secret[^4..]}";
    }
}
