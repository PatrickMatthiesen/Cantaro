using Microsoft.AspNetCore.DataProtection;

namespace Cantaro.Api.Services;

/// <summary>
/// Service for encrypting and decrypting sensitive tokens
/// </summary>
public class TokenEncryptionService
{
    private const string Purpose = "Cantaro.OAuth.Tokens";
    private readonly IDataProtector _protector;

    public TokenEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    /// <summary>
    /// Encrypts a token string
    /// </summary>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Token cannot be null or empty", nameof(plainText));

        return _protector.Protect(plainText);
    }

    /// <summary>
    /// Decrypts an encrypted token string
    /// </summary>
    public string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            throw new ArgumentException("Encrypted token cannot be null or empty", nameof(encryptedText));

        return _protector.Unprotect(encryptedText);
    }
}
