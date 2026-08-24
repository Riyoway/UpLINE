# Third-party notices

UpLINE keeps runtime dependencies intentionally small.

## QRCoder 1.8.0

Used for local QR image generation. MIT License.

- Project: https://github.com/Shane32/QRCoder
- Package: https://www.nuget.org/packages/QRCoder/1.8.0

## Transitive packages

The current Windows build resolves these transitive packages through QRCoder:

- `Microsoft.Win32.SystemEvents` 6.0.0 — MIT License
  - https://www.nuget.org/packages/Microsoft.Win32.SystemEvents/6.0.0
- `System.Drawing.Common` 6.0.0 — MIT License
  - https://www.nuget.org/packages/System.Drawing.Common/6.0.0

## .NET and Windows

The .NET runtime and WPF framework are distributed under the MIT License by
the .NET Foundation and Microsoft. Windows itself is a platform dependency,
not redistributed by UpLINE.

No Discord source code, branding assets, or proprietary UI assets are bundled.
