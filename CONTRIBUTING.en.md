# Contributing to UpLINE

[日本語](CONTRIBUTING.md) | [English](CONTRIBUTING.en.md)

## Before changing code

- Never include LINE access tokens, certificates, QR URLs, or private keys in issues, pull requests, or logs.
- When changing an undocumented API call, record the RPC, request fields, response fields, and reproduction conditions.
- Japanese is the default UI language. Check that new copy explains the next action after an error.

## Build

```powershell
dotnet restore
dotnet build UpLINE.csproj -c Release --no-restore
```

If you change QR login, perform a manual check with a fresh QR and a test account.

## Pull requests

Describe the reason, verification steps, affected areas, and known limitations. Do not present an inferred private API behavior as a confirmed specification; include evidence when available.
