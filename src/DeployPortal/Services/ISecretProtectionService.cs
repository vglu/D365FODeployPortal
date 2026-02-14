namespace DeployPortal.Services;

/// <summary>
/// Encrypt/decrypt and mask secrets. Abstraction for testability (DIP).
/// </summary>
public interface ISecretProtectionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
    string MaskSecret(string secret);
}
