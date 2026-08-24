using System.Net;

namespace UpLINE.Line.Transport;

public sealed class LineRpcException : Exception
{
    public int? ErrorCode { get; }
    public string? RpcName { get; }
    public bool IsLongPollTimeout { get; }
    public HttpStatusCode? HttpStatusCode { get; }

    public LineRpcException(
        string message,
        string? rpcName = null,
        int? errorCode = null,
        bool isLongPollTimeout = false,
        HttpStatusCode? httpStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RpcName = rpcName;
        ErrorCode = errorCode;
        IsLongPollTimeout = isLongPollTimeout;
        HttpStatusCode = httpStatusCode;
    }
}
