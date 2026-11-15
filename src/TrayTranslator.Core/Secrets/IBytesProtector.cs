namespace TrayTranslator.Core.Secrets;

/// <summary>
/// Encrypts bytes so only the current OS user can decrypt them (DPAPI CurrentUser on Windows).
/// </summary>
public interface IBytesProtector
{
    byte[] Protect(byte[] plaintext);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> when the data cannot be decrypted.</summary>
    byte[] Unprotect(byte[] protectedData);
}
