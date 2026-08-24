using UpLINE.Line.Models;
using UpLINE.Line.Transport;

namespace UpLINE.Line.Talk;

public static class LineDataMapper
{
    public static LineProfile ToProfile(ThriftStruct response, string fallbackMid)
    {
        var value = Unwrap(response);
        return new LineProfile(
            value.String(1) ?? fallbackMid,
            value.String(2) ?? value.String(1) ?? "LINE User",
            value.String(3),
            value.String(4));
    }

    public static IReadOnlyList<LineChat> ToChats(ThriftStruct response)
    {
        var list = FindStructList(response).ToList();
        return list.Select((item, index) => new LineChat(
                item.String(1) ?? $"chat-{index + 1}",
                item.String(2) ?? item.String(1) ?? "トーク",
                item.String(3),
                item.String(4),
                FromUnixMilliseconds(item.Int64(5)),
                item.Int32(6) ?? 0,
                item.Bool(7) ?? false))
            .ToList();
    }

    public static IReadOnlyList<LineContact> ToContacts(ThriftStruct response)
    {
        return FindStructList(response).Select((item, index) => new LineContact(
            item.String(1) ?? $"contact-{index + 1}",
            item.String(2) ?? "LINE User",
            item.String(3),
            item.Bool(4) ?? false)).ToList();
    }

    public static IReadOnlyList<LineMessage> ToMessages(ThriftStruct response, string chatId, string ownMid)
    {
        return FindStructList(response).Select((item, index) => new LineMessage(
            item.String(1) ?? $"message-{index + 1}",
            item.String(2) ?? chatId,
            item.String(3) ?? string.Empty,
            item.String(4) ?? "LINE User",
            item.String(5) ?? item.String(2) ?? string.Empty,
            FromUnixMilliseconds(item.Int64(6)) ?? DateTimeOffset.UtcNow,
            (item.String(3) ?? string.Empty) == ownMid,
            item.String(7))).ToList();
    }

    private static ThriftStruct Unwrap(ThriftStruct response) =>
        response.Struct(0) ?? response.Struct(1) ?? response;

    private static IEnumerable<ThriftStruct> FindStructList(ThriftStruct response)
    {
        var values = response.Fields.Values.Concat(
            response.Fields.Values.OfType<ThriftStruct>().SelectMany(structure => structure.Fields.Values));
        foreach (var value in values)
        {
            if (value is IEnumerable<object?> list)
            {
                var structs = list.OfType<ThriftStruct>().ToList();
                if (structs.Count > 0) return structs;
            }
        }
        return Array.Empty<ThriftStruct>();
    }

    private static DateTimeOffset? FromUnixMilliseconds(long? value)
    {
        if (value is null or <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(value.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
