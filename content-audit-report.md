# UpLINE pre-release content audit

Date: 2026-08-25

Scope: `README.md`, `MainWindow.xaml`, and user-visible messages in `MainWindow.xaml.cs`.
The repository currently ships Japanese UI copy with a small amount of English copy and no separate English README.

## Scores before this pass

- AI-likeness: 72/100
- Release readiness: 56/100

The main release blockers are documentation and expectation-setting rather than prose grammar.

## Critical findings

### C1 — README has no language switch or English counterpart

Location: `README.md:1`

The default README is Japanese, but English-speaking contributors have no entry point and the repository does not state which language is canonical.

Suggested fix: keep Japanese in `README.md`, add `README.en.md`, and add a language switch at the top of both files.

### C2 — Experimental/private API status is not prominent enough

Locations: `README.md:3`, `README.md:21-31`

“初期実装” is easy to miss, while the API list can read like a complete supported client. The implementation depends on undocumented LINE endpoints and server-version-specific Thrift field maps.

Suggested fix: add an “Experimental / unofficial” notice near the title and list the known limitations before the setup instructions.

### C3 — No clear legal/service-affiliation disclaimer

Location: `README.md:1-42`

The documentation does not explicitly say that UpLINE is not affiliated with LINE or Discord, or that users are responsible for complying with the services they use.

Suggested fix: add a short disclaimer without making legal conclusions about the service terms.

## Warnings

### W1 — Setup does not document the publish/runtime path

Location: `README.md:5-10`

The README only shows `dotnet run`; a Windows contributor cannot tell how to produce the Release artifact or whether the published app is self-contained.

Suggested fix: document the `win-x64` publish command and the .NET 8 Desktop Runtime requirement for framework-dependent builds.

### W2 — User-facing copy mixes Japanese and English

Locations: `MainWindow.xaml:24-25`, `MainWindow.xaml.cs:317`

“LINE for your desktop”, the English marketing sentence, and “contacts” break the otherwise Japanese default experience.

Suggested fix: use Japanese in the default UI and keep English localization as a separate future surface.

### W3 — Settings exposes transport implementation details

Location: `MainWindow.xaml.cs:326-329`

Showing the API host and “Thrift Compact” is useful for debugging but reads like internal diagnostics in a consumer-facing settings page.

Suggested fix: label the section as advanced diagnostics, or keep those details in a developer build.

## Positive checks

- No `TODO`, `FIXME`, prompt leakage, or placeholder copy was found in the scanned user-facing surfaces.
- The UI does not display access tokens, refresh tokens, certificates, or private keys.
- `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `.gitignore` are present.

## Recommended release gate

Apply C1-C3 and W1-W2, then run `dotnet build -c Release --no-restore` and a clean `win-x64` publish. Re-audit the final README and verify that no credential files or build outputs are committed.

## Applied in this pass

- Added `README.en.md` and made `README.md` the Japanese default with language links.
- Added the experimental/unofficial API disclaimer, setup and publish commands, QR troubleshooting, and secret-handling guidance.
- Added bilingual contribution guidance and a security policy.
- Replaced mixed English UI copy with Japanese defaults and labeled transport details as detailed information.
- Added `artifacts/` to `.gitignore` and expanded third-party package notices.

Remaining release work is operational: replace the repository URL in your GitHub project metadata if needed, run the live-account QR check after LINE-side verification limits clear, and create a tagged release artifact.
