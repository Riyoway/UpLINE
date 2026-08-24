using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using UpLINE.Line.Models;

namespace UpLINE.Line.Auth;

public sealed class WindowsCredentialStore
{
    private readonly string _credentialPath;

    public WindowsCredentialStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpLINE");
        Directory.CreateDirectory(root);
        _credentialPath = Path.Combine(root, "credentials.bin");
    }

    public async Task SaveAsync(AuthCredentials credentials, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedPayload = ProtectedData.Protect(payload, null);
        CryptographicOperations.ZeroMemory(payload);

        var temporaryPath = _credentialPath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, protectedPayload, cancellationToken);
        CryptographicOperations.ZeroMemory(protectedPayload);
        File.Move(temporaryPath, _credentialPath, true);
    }

    public async Task<AuthCredentials?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_credentialPath)) return null;
        var protectedPayload = await File.ReadAllBytesAsync(_credentialPath, cancellationToken);
        try
        {
            var payload = ProtectedData.Unprotect(protectedPayload, null);
            try
            {
                return JsonSerializer.Deserialize<AuthCredentials>(payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    public void Delete()
    {
        if (File.Exists(_credentialPath)) File.Delete(_credentialPath);
    }
}

internal static class ProtectedData
{
    public static byte[] Protect(byte[] data, byte[]? entropy) =>
        CryptProtect(data, entropy, protect: true);

    public static byte[] Unprotect(byte[] data, byte[]? entropy) =>
        CryptProtect(data, entropy, protect: false);

    private static byte[] CryptProtect(byte[] data, byte[]? entropy, bool protect)
    {
        var input = new DataBlob(data);
        var optionalEntropy = new DataBlob(entropy ?? Array.Empty<byte>());
        var output = new DATA_BLOB();
        try
        {
            var success = protect
                ? CryptProtectData(ref input, "UpLINE credentials", ref optionalEntropy, IntPtr.Zero, IntPtr.Zero, 0, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref optionalEntropy, IntPtr.Zero, IntPtr.Zero, 0, ref output);
            if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, result.Length);
            return result;
        }
        finally
        {
            input.Dispose();
            optionalEntropy.Dispose();
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;

        public DataBlob(byte[] bytes)
        {
            cbData = bytes.Length;
            pbData = bytes.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(bytes.Length);
            if (pbData != IntPtr.Zero) Marshal.Copy(bytes, 0, pbData, bytes.Length);
        }

        public void Dispose()
        {
            if (pbData == IntPtr.Zero) return;
            Marshal.FreeHGlobal(pbData);
            pbData = IntPtr.Zero;
            cbData = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string szDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("Crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
