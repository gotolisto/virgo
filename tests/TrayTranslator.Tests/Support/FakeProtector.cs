using System.Security.Cryptography;
using TrayTranslator.Core.Secrets;

namespace TrayTranslator.Tests.Support;

/// <summary>Reversible stand-in for DPAPI: XOR plus a marker, so tests can tell plaintext never hits disk.</summary>
public sealed class FakeProtector : IBytesProtector
{
    private const byte Key = 0x5A;
    private static readonly byte[] Marker = "FAKE"u8.ToArray();

    public bool FailUnprotect { get; set; }

    public byte[] Protect(byte[] plaintext) => [.. Marker, .. plaintext.Select(b => (byte)(b ^ Key))];

    public byte[] Unprotect(byte[] protectedData)
    {
        if (FailUnprotect || protectedData.Length < Marker.Length || !protectedData.AsSpan(0, Marker.Length).SequenceEqual(Marker))
        {
            throw new CryptographicException("cannot decrypt");
        }

        return protectedData.Skip(Marker.Length).Select(b => (byte)(b ^ Key)).ToArray();
    }
}
