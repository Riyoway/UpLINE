# UpLINE

[日本語](README.md) | [English](README.en.md)

An unofficial, community-built LINE client for Windows. UpLINE is built with WPF/.NET 8 and provides QR login plus a Discord-inspired four-pane desktop UI.

> [!WARNING]
> UpLINE is an independent open-source project and is not affiliated with LINE Corporation or Discord. It uses undocumented, version-dependent LINE APIs, so login and messaging behavior may change when LINE services or apps are updated. Review the terms of the services, account, and network you use before running it.

## Status

UpLINE is experimental and under active development. The current implementation includes:

- QR login, PIN confirmation, certificate reuse, and Windows DPAPI credential storage
- Profile, contacts, recent chats, message retrieval, and text sending
- Transport boundaries for `/P4` operations and `/obs` media uploads
- Thrift Compact/Binary response parsing with an isolated unknown-field boundary
- X25519 key generation during QR login and a temporary post-login E2EE key boundary

Talk API field maps, complete E2EE message decryption, media formats, and continuous event synchronization remain version-dependent areas. See the [LINE Android APK analysis notes](docs/line-android-apk-analysis.md); they are not official LINE documentation.

## Requirements

- Windows 10/11 x64
- .NET 8 SDK for development and builds
- .NET 8 Desktop Runtime for the framework-dependent build

## Setup

```powershell
# Change to the directory created from the repository's Code > Clone URL
Set-Location UpLINE
dotnet restore
dotnet run -c Release
```

The default API host is `https://ga2.line.naver.jp` and the default media host is `https://obs.line-scdn.net`. Hosts and client identifiers can be overridden with environment variables.

```powershell
$env:UPLINE_LINE_BASE_URL = "https://your-line-gateway.example"
$env:UPLINE_LINE_MEDIA_URL = "https://your-media-gateway.example"
$env:UPLINE_LINE_APPLICATION = "DESKTOPWIN`t26.11.0`tWINDOWS`t10"
$env:UPLINE_LINE_USER_AGENT = "DESKTOP:WIN:10(26.11.0)"
dotnet run -c Release
```

### Create a Windows release build

Framework-dependent:

```powershell
dotnet publish UpLINE.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -o artifacts/UpLINE
```

Self-contained, including the .NET 8 runtime:

```powershell
dotnet publish UpLINE.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o artifacts/UpLINE
```

## QR login

1. Update the LINE app on the main device.
2. Enable `Settings → Account → Allow login` on the main device.
3. Create a new QR code in UpLINE and scan it in the LINE app.
4. If a PIN appears, confirm it in the LINE app on the main device.

If the phone shows “Verification is temporarily unavailable”, do not repeatedly scan the same QR. Check the LINE app version, login permission, and network, then wait before trying a fresh QR code. QR sessions expire quickly.

## Security and privacy

- Access tokens, refresh tokens, certificates, and X25519 private keys are never logged.
- Credentials are encrypted with Windows DPAPI(CurrentUser) and stored at `%LOCALAPPDATA%\\UpLINE\\credentials.bin`.
- HTTPS is required; certificate validation is not disabled, redirects are not followed, and cookies are not shared.
- QR sessions use long polling instead of a tight GET loop.
- Never include tokens, certificates, QR URLs, or private keys in an issue or log attachment.

## Development

```powershell
dotnet restore
dotnet build UpLINE.csproj -c Release --no-restore
dotnet publish UpLINE.csproj -c Release -r win-x64 --self-contained false --no-restore
```

QR login and the live Talk API require a user account, so automated integration tests are not included. At minimum, run a Release build and verify login with a test account after transport changes.

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and [SECURITY.md](SECURITY.md) for vulnerability reports.

## License

UpLINE is licensed under the MIT License. Its direct and transitive QRCoder/.NET dependencies are also MIT-licensed. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
