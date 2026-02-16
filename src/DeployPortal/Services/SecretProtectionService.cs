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

        try
        {
            return _protector.Unprotect(cipherText);
        }
        catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message?.Contains("was not found in the key ring") == true)
        {
            throw new InvalidOperationException(
                "The encryption key used to store this environment's Client Secret is no longer in the key ring (keys were rotated or the app was restarted with a different key store). " +
                "Please open Environments, edit the environment, and re-enter the Client Secret, then save.", ex);
        }
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
