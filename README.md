# UpLINE

[日本語](README.md) | [English](README.en.md)

Windows向けの、非公式LINEサードパーティークライアントです。WPF/.NET 8で動作し、QRログインとDiscord系の4ペインUIを備えています。

> [!WARNING]
> UpLINEはLINE株式会社およびDiscordとは無関係の独立したOSSです。LINEの非公開・バージョン依存APIを使用するため、動作やログイン可否はサーバー・アプリの更新で変わる可能性があります。利用するアカウント、ネットワーク、サービスの規約を確認したうえで使用してください。

## ステータス

実験的な開発中プロジェクトです。現在の実装範囲は次のとおりです。

- QRログイン、PIN確認、証明書の再利用、Windows DPAPIによる認証情報保存
- プロフィール、連絡先、最近のトーク、メッセージ取得、テキスト送信
- `/P4`の操作取得と`/obs`メディアアップロードのtransport境界
- Thrift Compact/Binary応答の読み取りと、未知フィールドを保ったデコード境界
- QRログイン時のX25519鍵生成と、ログイン後の一時的なE2EE鍵境界

Talk APIのfield map、E2EEの完全なメッセージ復号、メディア形式、通知の常時同期はサーバー仕様の確定後に拡張する領域です。詳細な調査メモは [LINE_API.md](LINE_API.md) を参照してください。これはLINE公式ドキュメントではありません。

## 必要環境

- Windows 10/11 x64
- 開発・ビルド: .NET 8 SDK
- framework-dependent版の実行: .NET 8 Desktop Runtime

## セットアップ

```powershell
# GitHubのCode > Cloneで取得したリポジトリのディレクトリに移動
Set-Location UpLINE
dotnet restore
dotnet run -c Release
```

既定のAPIホストは `https://ga2.line.naver.jp`、メディアホストは `https://obs.line-scdn.net` です。ホストやクライアント識別子は環境変数で上書きできます。

```powershell
$env:UPLINE_LINE_BASE_URL = "https://your-line-gateway.example"
$env:UPLINE_LINE_MEDIA_URL = "https://your-media-gateway.example"
$env:UPLINE_LINE_APPLICATION = "DESKTOPWIN`t26.11.0`tWINDOWS`t10"
$env:UPLINE_LINE_USER_AGENT = "DESKTOP:WIN:10(26.11.0)"
dotnet run -c Release
```

### Windows向けに公開用ビルドを作る

framework-dependent版:

```powershell
dotnet publish UpLINE.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -o artifacts/UpLINE
```

.NET 8 Desktop Runtimeを含める自己完結版:

```powershell
dotnet publish UpLINE.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o artifacts/UpLINE
```

## QRログインについて

1. LINEアプリを最新版に更新します。
2. メイン端末の「設定 → アカウント → ログイン許可」を有効にします。
3. UpLINEで新しいQRコードを作成し、LINEアプリで読み取ります。
4. PINが表示された場合は、メイン端末のLINEアプリで確認します。

スマホ側に「Verification is temporarily unavailable」などが表示された場合は、同じQRを繰り返し試さず、LINEアプリの更新・ログイン許可・ネットワークを確認して時間を空けてください。QRセッションは短時間で失効します。

## セキュリティとプライバシー

- アクセストークン、リフレッシュトークン、証明書、X25519秘密鍵をログへ出力しません。
- 認証情報は `%LOCALAPPDATA%\\UpLINE\\credentials.bin` にWindows DPAPI(CurrentUser)で暗号化して保存します。
- HTTPSのみを許可し、証明書検証の無効化、自動リダイレクト、Cookie共有を行いません。
- QRセッションは長時間ポーリングを使用し、短周期のGETループを避けます。
- 課題報告やログ共有に、トークン・証明書・QR URL・秘密鍵を含めないでください。

## 開発

```powershell
dotnet restore
dotnet build UpLINE.csproj -c Release --no-restore
dotnet publish UpLINE.csproj -c Release -r win-x64 --self-contained false --no-restore
```

QRログインと実サーバーのTalk APIはユーザーアカウントを必要とするため、自動統合テストは同梱していません。変更時は少なくともReleaseビルドと、テスト用アカウントでのログイン確認を行ってください。

貢献方法は [CONTRIBUTING.md](CONTRIBUTING.md)、脆弱性の報告は [SECURITY.md](SECURITY.md) を参照してください。

## ライセンス

UpLINE本体はMIT Licenseです。QRCoderと.NETの直接・推移依存もMIT Licenseです。詳細は [LICENSE](LICENSE) と [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。
