using System.IO;
using System.Net;
using System.Net.Http;
using UpLINE.Line.Models;

namespace UpLINE.Line.Transport;

public sealed class LineRpcClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly LineServerSettings _settings;
    public LineRpcClient(LineServerSettings settings)
    {
        _settings = settings;
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("LINE API base URL must be an HTTPS URL.", nameof(settings));

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 8,
            UseCookies = false,
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUri, "."),
            // Login long-polling can be advertised as 60 seconds by LINE.
            // Keep a margin so HttpClient does not cancel a valid poll first.
            Timeout = TimeSpan.FromSeconds(180)
        };
        if (settings.UserAgent.Contains('\r') || settings.UserAgent.Contains('\n'))
            throw new ArgumentException("User-Agent cannot contain CR/LF.", nameof(settings));
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Line-Application", settings.ApplicationDescriptor);
        // LINE's desktop identifier contains ':' and parentheses. The strict
        // ProductHeaderValue parser rejects that legacy-but-valid identifier.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", settings.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/x-thrift");
    }

    public Task<ThriftStruct> CreateSessionAsync(CancellationToken cancellationToken = default) =>
        CallAsync("createSession", "/acct/lgn/sq/v1", writer => WriteRequest(writer, Array.Empty<(short, ThriftType, Action<ThriftWriter>)>()), null, cancellationToken);

    public async Task<ThriftStruct> CreateQrCodeForSecureAsync(string authSessionId, CancellationToken cancellationToken = default) =>
        await CallAsync("createQrCodeForSecure", "/acct/lgn/sq/v1", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(authSessionId)))
        }), null, cancellationToken);

    public Task<ThriftStruct> CheckQrCodeVerifiedAsync(string authSessionId, int timeoutMs, CancellationToken cancellationToken = default) =>
        CallAsync("checkQrCodeVerified", "/acct/lp/lgn/sq/v1", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(authSessionId)))
        }), new RequestHeaders(authSessionId, timeoutMs, IsLoginSession: true), cancellationToken);

    public Task<ThriftStruct> VerifyCertificateAsync(string authSessionId, string certificate, CancellationToken cancellationToken = default) =>
        CallAsync("verifyCertificate", "/acct/lgn/sq/v1", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(authSessionId))),
            ((short)2, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(certificate)))
        }), null, cancellationToken);

    public async Task<ThriftStruct> CreatePinCodeAsync(string authSessionId, CancellationToken cancellationToken = default) =>
        await CallAsync("createPinCode", "/acct/lgn/sq/v1", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(authSessionId)))
        }), null, cancellationToken);

    public Task<ThriftStruct> CheckPinCodeVerifiedAsync(string authSessionId, int timeoutMs, CancellationToken cancellationToken = default) =>
        CallAsync("checkPinCodeVerified", "/acct/lp/lgn/sq/v1", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(authSessionId)))
        }), new RequestHeaders(authSessionId, timeoutMs, IsLoginSession: true), cancellationToken);

    public Task<ThriftStruct> QrCodeLoginV2ForSecureAsync(
        string authSessionId,
        string systemName,
        string modelName,
        bool autoLoginIsRequired,
        string nonce,
        CancellationToken cancellationToken = default) =>
        CallAsync("qrCodeLoginV2ForSecure", "/acct/lgn/sq/v1", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(authSessionId))),
            ((short)2, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(systemName))),
            ((short)3, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(modelName))),
            ((short)4, ThriftType.Bool, (Action<ThriftWriter>)(w => w.WriteBool(autoLoginIsRequired))),
            ((short)5, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(nonce)))
        }), null, cancellationToken);

    public Task<ThriftStruct> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default) =>
        CallAsync("getProfile", "/S4", null, new RequestHeaders(accessToken, null), cancellationToken);

    public Task<ThriftStruct> GetAllContactAsync(string accessToken, CancellationToken cancellationToken = default) =>
        CallAsync("getAllContact", "/S4", null, new RequestHeaders(accessToken, null), cancellationToken);

    public Task<ThriftStruct> GetRecentChatsAsync(string accessToken, int count = 50, CancellationToken cancellationToken = default) =>
        CallAsync("getRecentMessagesV2", "/S4", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.I32, (Action<ThriftWriter>)(w => w.WriteI32(count)))
        }), new RequestHeaders(accessToken, null), cancellationToken);

    public Task<ThriftStruct> GetMessagesAsync(string accessToken, string chatId, long fromRevision, int count = 50, CancellationToken cancellationToken = default) =>
        CallAsync("fetchMessages", "/S4", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(chatId))),
            ((short)2, ThriftType.I64, (Action<ThriftWriter>)(w => w.WriteI64(fromRevision))),
            ((short)3, ThriftType.I32, (Action<ThriftWriter>)(w => w.WriteI32(count)))
        }), new RequestHeaders(accessToken, null), cancellationToken);

    public Task<ThriftStruct> SendMessageAsync(string accessToken, string chatId, string text, CancellationToken cancellationToken = default) =>
        CallAsync("sendMessage", "/S4", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(chatId))),
            ((short)2, ThriftType.String, (Action<ThriftWriter>)(w => w.WriteString(text)))
        }), new RequestHeaders(accessToken, null), cancellationToken);

    public Task<ThriftStruct> FetchOperationsAsync(string accessToken, long revision, CancellationToken cancellationToken = default) =>
        CallAsync("fetchOperations", "/P4", writer => WriteRequest(writer, new[] {
            ((short)1, ThriftType.I64, (Action<ThriftWriter>)(w => w.WriteI64(revision))),
            ((short)2, ThriftType.I32, (Action<ThriftWriter>)(w => w.WriteI32(100)))
        }), new RequestHeaders(accessToken, 30000), cancellationToken);

    public async Task<byte[]> UploadMediaAsync(string accessToken, Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_settings.MediaBaseUrl), "/obs"));
        request.Headers.TryAddWithoutValidation("X-Line-Access", accessToken);
        request.Headers.TryAddWithoutValidation("X-Line-Application", _settings.ApplicationDescriptor);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        request.Headers.TryAddWithoutValidation("X-Line-FileName", fileName);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LineRpcException($"Media upload failed with HTTP {(int)response.StatusCode}.", "uploadMedia", isLongPollTimeout: response.StatusCode == HttpStatusCode.RequestTimeout);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<ThriftStruct> CallAsync(
        string rpcName,
        string path,
        Action<ThriftWriter>? writeArgs,
        RequestHeaders? headers,
        CancellationToken cancellationToken)
    {
        var writer = new ThriftWriter(compact: true);
        writer.WriteMessageBegin(rpcName);
        writeArgs?.Invoke(writer);
        if (writeArgs is null) writer.WriteEmptyArgs();

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new ByteArrayContent(writer.ToArray());
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-thrift");
        request.Headers.TryAddWithoutValidation("X-Line-Application", _settings.ApplicationDescriptor);
        if (headers?.Access is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Line-Access", headers.Access);
        }
        if (headers?.IsLoginSession == true)
        {
            request.Headers.TryAddWithoutValidation("X-Line-Session-ID", headers.Access);
            request.Headers.TryAddWithoutValidation("Referer", string.Empty);
        }
        if (headers?.LongPollingTimeoutMs is not null)
            request.Headers.TryAddWithoutValidation("X-LST", headers.LongPollingTimeoutMs.Value.ToString());

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The direct login long-poll endpoint uses 410 Gone when the
            // advertised wait window ends without a state change. It is not
            // an expired QR session unless the session itself has exceeded
            // its retry budget; the caller should issue the next poll.
            var timeout = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout
                || (response.StatusCode == HttpStatusCode.Gone && headers?.IsLoginSession == true);
            throw new LineRpcException($"LINE RPC {rpcName} failed with HTTP {(int)response.StatusCode}.", rpcName, isLongPollTimeout: timeout, httpStatusCode: response.StatusCode);
        }

        try
        {
            var reader = new ThriftReader(body, compact: body.Length > 0 && body[0] == 0x82);
            var message = reader.ReadMessageBegin();
            if (message.MessageType == 3)
            {
                var applicationException = reader.ReadStruct();
                var messageText = applicationException.String(1);
                var exceptionType = applicationException.Int32(2);
                throw new LineRpcException(
                    string.IsNullOrWhiteSpace(messageText)
                        ? $"LINE RPC {rpcName} returned an application exception."
                        : messageText,
                    rpcName,
                    exceptionType);
            }
            var result = reader.ReadStruct();
            if (result.Struct(1) is { } exception)
            {
                var errorCode = exception.Int32(1);
                var alertMessage = exception.String(2);
                var messageText = string.IsNullOrWhiteSpace(alertMessage)
                    ? $"LINE RPC {rpcName} returned an error."
                    : alertMessage;
                throw new LineRpcException(messageText, rpcName, errorCode);
            }
            return result;
        }
        catch (LineRpcException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            throw new LineRpcException($"LINE RPC {rpcName} returned an invalid Thrift response.", rpcName, innerException: exception);
        }
    }

    public void Dispose() => _http.Dispose();

    private static void WriteRequest(
        ThriftWriter argsWriter,
        IEnumerable<(short Id, ThriftType Type, Action<ThriftWriter> Write)> fields)
    {
        argsWriter.WriteStruct(new[] {
            ((short)1, ThriftType.Struct, (Action<ThriftWriter>)(requestWriter => requestWriter.WriteStruct(fields)))
        });
    }

    private sealed record RequestHeaders(string Access, int? LongPollingTimeoutMs, bool IsLoginSession = false);
}

public sealed record LineServerSettings(
    string BaseUrl,
    string MediaBaseUrl,
    string ApplicationDescriptor,
    string UserAgent)
{
    public static LineServerSettings Default => new(
        Environment.GetEnvironmentVariable("UPLINE_LINE_BASE_URL") ?? "https://ga2.line.naver.jp",
        Environment.GetEnvironmentVariable("UPLINE_LINE_MEDIA_URL") ?? "https://obs.line-scdn.net",
        Environment.GetEnvironmentVariable("UPLINE_LINE_APPLICATION") ?? "DESKTOPWIN\t26.11.0\tWINDOWS\t10",
        Environment.GetEnvironmentVariable("UPLINE_LINE_USER_AGENT") ?? "DESKTOP:WIN:10(26.11.0)");
}
