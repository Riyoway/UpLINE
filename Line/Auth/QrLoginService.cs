using UpLINE.Line.Models;
using UpLINE.Line.Transport;

namespace UpLINE.Line.Auth;

public sealed class QrLoginService
{
    private readonly LineRpcClient _rpc;
    private readonly WindowsCredentialStore _credentialStore;

    public QrLoginService(LineRpcClient rpc, WindowsCredentialStore credentialStore)
    {
        _rpc = rpc;
        _credentialStore = credentialStore;
    }

    public IReadOnlyDictionary<string, string> LastLoginMetaData { get; private set; } = new Dictionary<string, string>();

    public async Task<QrLoginSession> StartQrLoginAsync(CancellationToken cancellationToken = default)
    {
        var created = await _rpc.CreateSessionAsync(cancellationToken);
        created = UnwrapResult(created);
        var authSessionId = RequireString(created, 1, "authSessionId");
        var qr = await _rpc.CreateQrCodeForSecureAsync(authSessionId, cancellationToken);
        qr = UnwrapResult(qr);
        var callbackUrl = RequireString(qr, 1, "callbackUrl");
        var maxCount = qr.Int32(2) ?? 60;
        var intervalSec = Math.Clamp(qr.Int32(3) ?? 30, 1, 60);
        var nonce = RequireString(qr, 4, "nonce");
        var keyPair = X25519.GenerateKeyPair();
        var qrUrl = AppendE2eeSecret(callbackUrl, keyPair.PublicKey);
        return new QrLoginSession(authSessionId, callbackUrl, qrUrl, nonce, maxCount, intervalSec, keyPair);
    }

    public async Task WaitForQrScanAsync(QrLoginSession session, IProgress<LoginProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var timeoutMs = session.LongPollingIntervalSec * 1000;
        for (var attempt = 0; attempt < session.LongPollingMaxCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new LoginProgress("WAITING_QR_SCAN", "スマートフォンでQRコードを読み取るのを待っています…"));
            try
            {
                await _rpc.CheckQrCodeVerifiedAsync(session.AuthSessionId, timeoutMs, cancellationToken);
                return;
            }
            catch (LineRpcException exception) when (exception.IsLongPollTimeout || exception.ErrorCode == 5)
            {
                // Long-poll timeouts are expected. The next request reuses the same session.
            }
        }

        throw new LineRpcException("QRコードの有効期限が切れました。最初からやり直してください。", "checkQrCodeVerified", errorCode: 5);
    }

    public async Task<bool> TryVerifySavedCertificateAsync(QrLoginSession session, CancellationToken cancellationToken = default)
    {
        var saved = await _credentialStore.LoadAsync(cancellationToken);
        // This RPC is also the state transition for a first-time QR login.
        // The server may reject an empty/old certificate, but skipping the
        // call leaves the QR session in the wrong state and prevents PIN login.
        // A non-empty sentinel makes the first-login transition explicit;
        // LINE rejects it and then moves the session to PIN verification.
        var certificate = string.IsNullOrWhiteSpace(saved?.Certificate) ? "dummy" : saved.Certificate;
        try
        {
            await _rpc.VerifyCertificateAsync(session.AuthSessionId, certificate, cancellationToken);
            return true;
        }
        catch (LineRpcException)
        {
            return false;
        }
    }

    public async Task<string> CreatePinCodeAsync(QrLoginSession session, CancellationToken cancellationToken = default)
    {
        var response = await _rpc.CreatePinCodeAsync(session.AuthSessionId, cancellationToken);
        response = UnwrapResult(response);
        return RequireString(response, 1, "pinCode");
    }

    public async Task WaitForPinVerificationAsync(QrLoginSession session, IProgress<LoginProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var timeoutMs = session.LongPollingIntervalSec * 1000;
        for (var attempt = 0; attempt < session.LongPollingMaxCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new LoginProgress("WAITING_PIN", "表示されたPINをメイン端末で確認してください…"));
            try
            {
                await _rpc.CheckPinCodeVerifiedAsync(session.AuthSessionId, timeoutMs, cancellationToken);
                return;
            }
            catch (LineRpcException exception) when (exception.IsLongPollTimeout || exception.ErrorCode == 5)
            {
                // Expected long-poll timeout.
            }
        }

        throw new LineRpcException("PIN確認の有効期限が切れました。最初からやり直してください。", "checkPinCodeVerified", errorCode: 5);
    }

    public async Task<AuthCredentials> CompleteQrLoginAsync(QrLoginSession session, IProgress<LoginProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LoginProgress("COMPLETING", "ログインセッションを確定しています…"));
        var response = await _rpc.QrCodeLoginV2ForSecureAsync(
            session.AuthSessionId,
            // These are the identifiers currently accepted by LINE's
            // secondary-login gateway for the V3 token flow. The transport
            // still identifies this application as Windows via
            // X-Line-Application/User-Agent.
            "CHROMEOS",
            "CHROME",
            autoLoginIsRequired: false,
            session.Nonce,
            cancellationToken);
        response = UnwrapResult(response);
        LastLoginMetaData = response.StringMap(10);
        if (LastLoginMetaData.Count == 0)
            LastLoginMetaData = response.StringMap(6);

        var tokenResult = response.Struct(3);
        var accessToken = FirstNonEmpty(tokenResult?.String(1), response.String(2));
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new LineRpcException("ログイン応答にアクセストークンがありません。", "qrCodeLoginV2ForSecure");

        var refreshToken = FirstNonEmpty(tokenResult?.String(2));
        var expirationValue = tokenResult?.Int64(3);
        DateTimeOffset? expiresAt = ToExpiration(expirationValue);
        var mid = FirstNonEmpty(response.String(4), response.String(5));
        if (string.IsNullOrWhiteSpace(mid))
            throw new LineRpcException("ログイン応答にMIDがありません。", "qrCodeLoginV2ForSecure");
        var credentials = new AuthCredentials(
            mid,
            accessToken,
            refreshToken,
            response.String(1),
            expiresAt);
        await _credentialStore.SaveAsync(credentials, cancellationToken);
        return credentials;
    }

    public async Task<AuthCredentials?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await _credentialStore.LoadAsync(cancellationToken);
        if (credentials is null) return null;
        if (credentials.ExpiresAt is not null && credentials.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;
        try
        {
            await _rpc.GetProfileAsync(credentials.AccessToken, cancellationToken);
            return credentials;
        }
        catch (LineRpcException exception) when (IsInvalidSession(exception))
        {
            // Do not keep a token that LINE has explicitly revoked. Keeping it
            // would make every application start look like a broken QR login.
            _credentialStore.Delete();
            return null;
        }
        catch (LineRpcException)
        {
            return null;
        }
    }

    public Task LogoutAsync()
    {
        _credentialStore.Delete();
        return Task.CompletedTask;
    }

    public static string AppendE2eeSecret(string callbackUrl, ReadOnlySpan<byte> publicKey)
    {
        var separator = callbackUrl.Contains('?') ? '&' : '?';
        // LINE's secure QR flow expects standard Base64, URL-escaped as a
        // query value. Do not use Base64URL or remove the padding: the phone
        // app decodes this exact 32-byte X25519 public key.
        var encodedKey = Uri.EscapeDataString(Convert.ToBase64String(publicKey));
        return $"{callbackUrl}{separator}secret={encodedKey}&e2eeVersion=1";
    }

    private static string RequireString(ThriftStruct value, short fieldId, string fieldName) =>
        value.String(fieldId) is { Length: > 0 } text
            ? text
            : throw new LineRpcException($"LINE RPC response is missing {fieldName}.");

    private static ThriftStruct UnwrapResult(ThriftStruct response) => response.Struct(0) ?? response;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static DateTimeOffset? ToExpiration(long? value)
    {
        if (value is null or <= 0) return null;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return value > now
            ? DateTimeOffset.FromUnixTimeSeconds(value.Value)
            : DateTimeOffset.UtcNow.AddSeconds(value.Value);
    }

    private static bool IsInvalidSession(LineRpcException exception) =>
        exception.ErrorCode == 8
        || exception.HttpStatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;
}
