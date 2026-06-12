# Security Policy

## Supported Versions

Security fixes are applied to the latest released minor version, published to NuGet across all
six `RustPlusApi*` packages. Older versions are not patched — please upgrade to the latest
release before reporting.

| Version | Supported          |
| ------- | ------------------ |
| Latest  | Yes                |
| Older   | No                 |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or
pull requests.**

Instead, report them privately through GitHub's
[**Security Advisories**](https://github.com/HandyS11/RustPlusApi/security/advisories/new) —
this opens a private channel with the maintainers and lets us coordinate a fix and disclosure.

If you cannot use GitHub Security Advisories, email **valentin-clergue@orange.fr** with the
details below.

Please include as much of the following as you can:

- The affected package(s) (e.g. `RustPlusApi.Fcm.Registration`).
- The type of issue (e.g. credential leakage, deserialization, TLS/transport, injection).
- A description of the impact and a realistic attack scenario.
- Step-by-step reproduction instructions and/or a proof-of-concept.
- Any relevant logs, stack traces, or configuration (with secrets redacted).

## What to Expect

- **Acknowledgement** within 5 business days.
- An assessment and, where confirmed, a planned fix with a target timeline.
- Credit in the published advisory and release notes, unless you prefer to remain anonymous.

We follow a coordinated-disclosure approach: please give us a reasonable window to release a fix
before any public disclosure.

## Scope

This project handles sensitive material — Steam credentials, FCM/Expo push tokens, and Rust+
companion tokens. Reports touching credential acquisition, persistence
(`CredentialsStore`), the MCS/TLS handshake, or the WebSocket transport are especially welcome.

Out of scope:

- Vulnerabilities in the upstream Rust dedicated server, the Rust+ companion app, or Google/Expo
  push infrastructure (report those to their respective vendors).
- Issues requiring a compromised host or physical access to the machine running the library.
- Reports generated solely by automated scanners without a demonstrable, exploitable impact.
