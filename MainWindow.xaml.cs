using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using UpLINE.Line.Auth;
using UpLINE.Line.E2ee;
using UpLINE.Line.Models;
using UpLINE.Line.Talk;
using UpLINE.Line.Transport;

namespace UpLINE;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LineChat> _chats = new();
    private readonly ObservableCollection<LineMessage> _messages = new();
    private readonly ObservableCollection<LineContact> _contacts = new();
    private readonly WindowsCredentialStore _credentialStore = new();
    private readonly LineServerSettings _settings = LineServerSettings.Default;
    private readonly E2eeSession _e2ee = new();
    private LineRpcClient? _rpc;
    private QrLoginService? _auth;
    private AuthCredentials? _credentials;
    private QrLoginSession? _qrSession;
    private LineProfile? _profile;
    private CancellationTokenSource? _loginCancellation;
    private string? _selectedChatId;

    public MainWindow()
    {
        InitializeComponent();
        ChatList.ItemsSource = _chats;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfigureClient();
            _credentials = await _auth!.RestoreSessionAsync();
            if (_credentials is not null)
            {
                await OpenMainViewAsync();
                return;
            }
        }
        catch (Exception exception)
        {
            SetLoginStatus(ToUserMessage(exception));
        }

        await StartLoginAsync();
    }

    private void ConfigureClient()
    {
        _rpc?.Dispose();
        _rpc = null;
        _auth = null;
        var rpc = new LineRpcClient(_settings);
        _rpc = rpc;
        _auth = new QrLoginService(rpc, _credentialStore);
    }

    private async Task StartLoginAsync()
    {
        _loginCancellation?.Cancel();
        _loginCancellation?.Dispose();
        _loginCancellation = new CancellationTokenSource();
        var cancellationToken = _loginCancellation.Token;
        RetryLoginButton.IsEnabled = false;
        PinPanel.Visibility = Visibility.Collapsed;
        QrImage.Source = null;
        SetLoginStatus("安全なQRセッションを準備しています…");

        try
        {
            if (_auth is null) ConfigureClient();
            _qrSession = await _auth!.StartQrLoginAsync(cancellationToken);
            QrImage.Source = BuildQrImage(_qrSession.QrUrl);
            SetLoginStatus("LINEアプリでQRコードを読み取ってください。\nQRセッションは一時的なものです。");

            var progress = new Progress<LoginProgress>(value =>
            {
                SetLoginStatus(value.Message);
                if (value.PinCode is not null) ShowPin(value.PinCode);
            });
            await _auth.WaitForQrScanAsync(_qrSession, progress, cancellationToken);
            SetLoginStatus("QRコードを確認しました。端末認証を確認しています…");

            if (!await _auth.TryVerifySavedCertificateAsync(_qrSession, cancellationToken))
            {
                var pin = await _auth.CreatePinCodeAsync(_qrSession, cancellationToken);
                ShowPin(pin);
                await _auth.WaitForPinVerificationAsync(_qrSession, progress, cancellationToken);
            }

            _credentials = await _auth.CompleteQrLoginAsync(_qrSession, progress, cancellationToken);
            _e2ee.Initialize(_qrSession, _auth.LastLoginMetaData);
            await OpenMainViewAsync();
        }
        catch (OperationCanceledException)
        {
            SetLoginStatus("ログインをキャンセルしました。");
        }
        catch (Exception exception)
        {
            SetLoginStatus(ToUserMessage(exception));
        }
        finally
        {
            ClearQrSessionSecrets();
            RetryLoginButton.IsEnabled = true;
        }
    }

    private async Task OpenMainViewAsync()
    {
        if (_credentials is null || _rpc is null) return;

        try
        {
            var profileResponse = await _rpc.GetProfileAsync(_credentials.AccessToken);
            _profile = LineDataMapper.ToProfile(profileResponse, _credentials.Mid);
            AccountNameText.Text = _profile.DisplayName;
        }
        catch (LineRpcException exception) when (IsInvalidSession(exception))
        {
            await ReturnToLoginAsync("LINE側でログインセッションが無効になりました。新しいQRコードで再ログインしてください。");
            return;
        }
        catch (Exception exception)
        {
            SetLoginStatus($"プロフィール取得に失敗しました。{ToUserMessage(exception)}");
            return;
        }

        LoginView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
        RetryLoginButton.IsEnabled = true;

        var initialLoadErrors = new List<string>();
        try
        {
            await LoadChatsAsync();
        }
        catch (LineRpcException exception) when (IsInvalidSession(exception))
        {
            await ReturnToLoginAsync("LINE側でログインセッションが無効になりました。新しいQRコードで再ログインしてください。");
            return;
        }
        catch (Exception exception)
        {
            initialLoadErrors.Add($"トーク一覧: {ToUserMessage(exception)}");
        }

        try
        {
            await LoadContactsAsync();
        }
        catch (LineRpcException exception) when (IsInvalidSession(exception))
        {
            await ReturnToLoginAsync("LINE側でログインセッションが無効になりました。新しいQRコードで再ログインしてください。");
            return;
        }
        catch (Exception exception)
        {
            initialLoadErrors.Add($"連絡先: {ToUserMessage(exception)}");
        }

        if (initialLoadErrors.Count > 0)
        {
            ConversationSubtitle.Text = "一部のTalk APIを読み込めませんでした";
            AddSystemMessage(string.Join(Environment.NewLine + Environment.NewLine, initialLoadErrors));
        }
    }

    private async Task ReturnToLoginAsync(string message)
    {
        if (_auth is not null) await _auth.LogoutAsync();
        _credentials = null;
        _profile = null;
        _selectedChatId = null;
        _chats.Clear();
        _messages.Clear();
        _contacts.Clear();
        RenderMembers();
        MainView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        QrImage.Source = null;
        PinPanel.Visibility = Visibility.Collapsed;
        SetLoginStatus(message);
        RetryLoginButton.IsEnabled = true;
    }

    private async Task LoadChatsAsync()
    {
        if (_rpc is null || _credentials is null) return;
        var response = await _rpc.GetRecentChatsAsync(_credentials.AccessToken);
        var chats = LineDataMapper.ToChats(response);
        _chats.Clear();
        foreach (var chat in chats) _chats.Add(chat);
        if (_chats.Count == 0)
        {
            _chats.Add(new LineChat("welcome", "UpLINE", null, "トークを選択してください", DateTimeOffset.UtcNow));
        }
    }

    private async Task LoadContactsAsync()
    {
        if (_rpc is null || _credentials is null) return;
        var response = await _rpc.GetAllContactAsync(_credentials.AccessToken);
        var contacts = LineDataMapper.ToContacts(response);
        _contacts.Clear();
        foreach (var contact in contacts.Take(12)) _contacts.Add(contact);
        RenderMembers();
    }

    private async void ChatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatList.SelectedItem is not LineChat chat || _credentials is null || _rpc is null) return;
        _selectedChatId = chat.Id;
        ConversationTitle.Text = chat.Name;
        ConversationSubtitle.Text = chat.IsGroup ? "グループ" : "ダイレクトメッセージ";
        WelcomePanel.Visibility = Visibility.Collapsed;
        try
        {
            var response = await _rpc.GetMessagesAsync(_credentials.AccessToken, chat.Id, 0);
            var messages = LineDataMapper.ToMessages(response, chat.Id, _credentials.Mid);
            _messages.Clear();
            foreach (var message in messages) _messages.Add(message);
            RenderMessages();
        }
        catch (Exception exception)
        {
            AddSystemMessage(ToUserMessage(exception));
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendCurrentMessageAsync();

    private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendCurrentMessageAsync();
        }
    }

    private async Task SendCurrentMessageAsync()
    {
        var text = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || _selectedChatId is null || _credentials is null || _rpc is null) return;
        SendButton.IsEnabled = false;
        try
        {
            await _rpc.SendMessageAsync(_credentials.AccessToken, _selectedChatId, text);
            _messages.Add(new LineMessage(Guid.NewGuid().ToString("N"), _selectedChatId, _credentials.Mid, _profile?.DisplayName ?? "自分", text, DateTimeOffset.Now, true));
            RenderMessages();
            MessageTextBox.Clear();
        }
        catch (Exception exception)
        {
            AddSystemMessage(ToUserMessage(exception));
        }
        finally
        {
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageTextBox.Text);
        }
    }

    private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageTextBox.Text) && _selectedChatId is not null;

    private async void RetryLoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginView.Visibility = Visibility.Visible;
        MainView.Visibility = Visibility.Collapsed;
        await StartLoginAsync();
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_auth is not null) await _auth.LogoutAsync();
        _credentials = null;
        _selectedChatId = null;
        MainView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        await StartLoginAsync();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        ConversationTitle.Text = "ホーム";
        ConversationSubtitle.Text = "安全なデスクトップ接続";
        WelcomePanel.Visibility = Visibility.Visible;
        _selectedChatId = null;
        _messages.Clear();
        MessageTextBox.Clear();
    }

    private void MessagesButton_Click(object sender, RoutedEventArgs e) => ChatList.Focus();

    private void ContactsButton_Click(object sender, RoutedEventArgs e)
    {
        ConversationTitle.Text = "連絡先";
        ConversationSubtitle.Text = $"連絡先 {_contacts.Count}人";
        WelcomePanel.Visibility = Visibility.Visible;
        _selectedChatId = null;
        AddSystemMessage("連絡先は右側のメンバーパネルに表示しています。");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ConversationTitle.Text = "設定";
        ConversationSubtitle.Text = "UpLINE / Windows";
        WelcomePanel.Visibility = Visibility.Visible;
        _selectedChatId = null;
        AddSystemMessage($"詳細情報\nAPIホスト: {_settings.BaseUrl}\n認証情報: Windows DPAPI (CurrentUser)\n通信方式: HTTPS / Thrift Compact");
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _loginCancellation?.Cancel();
        _rpc?.Dispose();
        _e2ee.Dispose();
    }

    private void ClearQrSessionSecrets()
    {
        if (_qrSession is null) return;
        CryptographicOperations.ZeroMemory(_qrSession.E2ee.PrivateKey);
        _qrSession = null;
    }

    private void RenderMessages()
    {
        MessagePanel.Children.Clear();
        foreach (var message in _messages)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var avatar = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = message.IsOutgoing ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush") };
            avatar.Child = new TextBlock { Text = FirstCharacter(message.SenderName), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(avatar, 0);
            row.Children.Add(avatar);
            var content = new StackPanel();
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = message.SenderName, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = $"  {message.CreatedAt:HH:mm}", Foreground = (Brush)FindResource("SubtleTextBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(header);
            content.Children.Add(new TextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            MessagePanel.Children.Add(row);
        }
    }

    private void RenderMembers()
    {
        MemberPanel.Children.Clear();
        foreach (var contact in _contacts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = (Brush)FindResource("BorderBrush"), Child = new TextBlock { Text = FirstCharacter(contact.DisplayName), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            row.Children.Add(new TextBlock { Text = contact.DisplayName, Foreground = (Brush)FindResource("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            MemberPanel.Children.Add(row);
        }
        if (_contacts.Count == 0)
            MemberPanel.Children.Add(new TextBlock { Text = "連絡先を読み込めませんでした", Foreground = (Brush)FindResource("MutedTextBrush"), TextWrapping = TextWrapping.Wrap });
    }

    private void AddSystemMessage(string text)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        MessagePanel.Children.Clear();
        var border = new Border { Background = (Brush)FindResource("PanelBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(18) };
        border.Child = new TextBlock { Text = text, Foreground = (Brush)FindResource("MutedTextBrush"), TextWrapping = TextWrapping.Wrap };
        MessagePanel.Children.Add(border);
    }

    private BitmapImage BuildQrImage(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var bytes = new PngByteQRCode(data).GetGraphic(12, System.Drawing.Color.FromArgb(0x11, 0x12, 0x16), System.Drawing.Color.White, drawQuietZones: true);
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void ShowPin(string pin)
    {
        PinPanel.Visibility = Visibility.Visible;
        PinText.Text = pin;
    }

    private void SetLoginStatus(string message) => LoginStatusText.Text = message;

    private static string FirstCharacter(string? value) => string.IsNullOrWhiteSpace(value) ? "?" : value.Trim()[0].ToString().ToUpperInvariant();

    private static bool IsInvalidSession(LineRpcException exception) =>
        exception.ErrorCode == 8
        || exception.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private static string ToUserMessage(Exception exception) => exception switch
    {
        LineRpcException rpc when rpc.HttpStatusCode == System.Net.HttpStatusCode.BadRequest => "LINEゲートウェイがログイン要求を拒否しました。Thrift形式・アプリ識別子・APIホストの組み合わせを確認してください。",
        LineRpcException rpc when rpc.HttpStatusCode == System.Net.HttpStatusCode.Forbidden => "LINEゲートウェイが現在のクライアント識別子または接続元を拒否しました。X-Line-ApplicationとAPIホストを確認してください。",
        LineRpcException rpc when rpc.IsLongPollTimeout => "接続がタイムアウトしました。QRコードを再作成してください。",
        LineRpcException rpc when rpc.ErrorCode == 8 => "LINE側でこのログインセッションが無効化されています。新しいQRコードで再ログインしてください。",
        LineRpcException rpc when rpc.ErrorCode == 101 => "LINE側でプロトコルの更新が必要です。アプリ設定を確認してください。",
        HttpRequestException => "LINEサーバーへ接続できません。ネットワークとAPIホスト設定を確認してください。",
        UnauthorizedAccessException => "保存領域へアクセスできません。Windowsユーザー権限を確認してください。",
        _ => exception.Message.Length > 180 ? exception.Message[..180] + "…" : exception.Message
    };
}
