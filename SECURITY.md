# Security Policy

## Supported Versions

| Version | Supported |
|---|---|
| Latest release on `main` | ✅ |
| Older releases | ❌ — please update |

---

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Please report vulnerabilities privately via GitHub's built-in security advisory system:

1. Go to the [Security tab](https://github.com/skynrlabs/SemaBuzz-Relay/security/advisories) of this repository.
2. Click **"Report a vulnerability"**.
3. Fill in the details — include steps to reproduce, affected component, and potential impact.

You will receive an acknowledgement within **5 business days**. We aim to triage and respond with a remediation plan within **14 days** of receiving a valid report.

---

## Scope

The following are in scope for security reports:

- **Relay confidentiality** — any scenario where the relay could read or log message content
- **Denial of service** — abuse of rate limits or room exhaustion
- **Room isolation** — a client gaining access to a room they did not join

The following are **out of scope**:

- Vulnerabilities in third-party dependencies (report those upstream)
- Issues requiring physical access to the server

---

## Security Design

SemaBuzz Relay is designed with the following guarantees:

- **Blind pass-through.** The relay never reads, parses, logs, or stores message content. It forwards raw binary frames between paired peers only.
- **No persistence.** IP addresses and room tokens are held in memory only for the duration of an active session.
- **Rate limiting.** Connections per IP and rooms per IP are capped to limit abuse.
