# Security policy

UpLINE handles LINE session credentials and should be treated as security-sensitive software.

## Do not disclose secrets

Never publish access tokens, refresh tokens, certificates, QR URLs, QR images, X25519 private keys, or unredacted logs. Deleting `%LOCALAPPDATA%\\UpLINE\\credentials.bin` logs the client out and removes the locally stored session.

## Reporting a vulnerability

Do not open a public issue for a security vulnerability. If this repository is hosted on GitHub, use a private Security Advisory. Otherwise, contact the project maintainer privately and include only the minimum reproduction details needed to validate the issue.

Please do not test against accounts or data you do not own.
