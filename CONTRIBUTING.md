# Contributing to UpLINE

[日本語](CONTRIBUTING.md) | [English](CONTRIBUTING.en.md)

## 変更前に

- LINEのアクセストークン、証明書、QR URL、秘密鍵をIssue・Pull Request・ログに含めないでください。
- 非公開APIの変更は、対象RPC・リクエストfield・応答field・再現条件を記録してください。
- UIの日本語をデフォルトとして、エラー時に次の操作が分かる文言を確認してください。

## ビルド

```powershell
dotnet restore
dotnet build UpLINE.csproj -c Release --no-restore
```

QRログインを変更した場合は、テスト用アカウントで新しいQRを使って手動確認してください。

## Pull Request

変更理由、確認手順、影響範囲、未解決の制約を記載してください。APIの推測を確定仕様として扱わず、根拠がある場合は参照元を添えてください。
