using System.Security.Cryptography;
using UpLINE.Line.Models;

namespace UpLINE.Line.E2ee;

/// <summary>
/// Holds the transient X25519 material created for a secure QR session.
/// The exact e2eeInfo/Letter Sealing payload format is not present in LINE_API.md,
/// so this boundary deliberately does not invent a wire format.
/// </summary>
public sealed class E2eeSession : IDisposable
{
    private byte[]? _privateKey;

    public bool IsInitialized => _privateKey is not null;
    public bool HasTransferredMetadata { get; private set; }

    public void Initialize(QrLoginSession loginSession, IReadOnlyDictionary<string, string>? metadata)
    {
        Reset();
        _privateKey = loginSession.E2ee.PrivateKey.ToArray();
        HasTransferredMetadata = metadata?.ContainsKey("e2eeInfo") == true;
    }

    public byte[] DeriveSharedSecret(ReadOnlySpan<byte> peerPublicKey)
    {
        if (_privateKey is null) throw new InvalidOperationException("E2EE is not initialized.");
        return Auth.X25519.ScalarMultiply(_privateKey, peerPublicKey);
    }

    public void Reset()
    {
        if (_privateKey is not null)
        {
            CryptographicOperations.ZeroMemory(_privateKey);
            _privateKey = null;
        }
        HasTransferredMetadata = false;
    }

    public void Dispose() => Reset();
}
