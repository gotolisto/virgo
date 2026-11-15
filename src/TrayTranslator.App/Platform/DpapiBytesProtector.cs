using System.Security.Cryptography;
using System.Text;
using TrayTranslator.Core.Secrets;

namespace TrayTranslator.App.Platform;

/// <summary>DPAPI, scoped to the current Windows user. Another user (or another machine) cannot decrypt.</summary>
internal sealed class DpapiBytesProtector : IBytesProtector
{
    // Not a secret: it only stops other DPAPI users on this account from decrypting our blobs by accident.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TrayTranslator.ApiKeys.v1");

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}
