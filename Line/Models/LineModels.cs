using System.Collections.ObjectModel;

namespace UpLINE.Line.Models;

public sealed record AuthCredentials(
    string Mid,
    string AccessToken,
    string? RefreshToken,
    string? Certificate,
    DateTimeOffset? ExpiresAt);

public sealed record X25519KeyPair(byte[] PrivateKey, byte[] PublicKey);

public sealed record QrLoginSession(
    string AuthSessionId,
    string CallbackUrl,
    string QrUrl,
    string Nonce,
    int LongPollingMaxCount,
    int LongPollingIntervalSec,
    X25519KeyPair E2ee);

public sealed record QrCodeResult(
    string CallbackUrl,
    int LongPollingMaxCount,
    int LongPollingIntervalSec,
    string Nonce);

public sealed record QrLoginResult(
    string Certificate,
    string? AccessTokenV2,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string Mid,
    IReadOnlyDictionary<string, string> MetaData);

public sealed record LineProfile(
    string Mid,
    string DisplayName,
    string? PictureUrl,
    string? StatusMessage);

public sealed record LineContact(
    string Mid,
    string DisplayName,
    string? PictureUrl,
    bool IsFriend = false);

public sealed record LineChat(
    string Id,
    string Name,
    string? PictureUrl,
    string? LastMessage,
    DateTimeOffset? LastActivity,
    int UnreadCount = 0,
    bool IsGroup = false);

public sealed record LineMessage(
    string Id,
    string ChatId,
    string SenderMid,
    string SenderName,
    string Text,
    DateTimeOffset CreatedAt,
    bool IsOutgoing = false,
    string? ContentType = null);

public sealed record LineEvent(
    string Type,
    string? ChatId,
    string? MessageId,
    string? Text,
    DateTimeOffset ReceivedAt);

public sealed record LineChatPage(IReadOnlyList<LineChat> Chats, string? NextToken);

public sealed record LoginProgress(string State, string Message, string? PinCode = null);

public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}
