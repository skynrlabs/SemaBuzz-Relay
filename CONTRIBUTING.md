# Contributing to SemaBuzz Relay

Thank you for your interest in contributing. Please read this document before opening issues or pull requests.

---

## Code of Conduct

Be respectful. Harassment, discrimination, or abusive language toward any contributor will not be tolerated and may result in removal from the project.

---

## License

SemaBuzz Relay is open-source under the **MIT** license. By submitting a contribution you agree to license your work under the same terms.

---

## Branch Model

| Branch | Purpose |
|---|---|
| `main` | Stable, release-ready. Never commit directly here. |
| `dev` | Integration target. All PRs merge here first. |
| `feature/*` | New features (`feature/rate-limiting`) |
| `fix/*` | Bug fixes (`fix/room-cleanup-race`) |

**Flow:** `feature/* / fix/*` → PR to `dev` → PR to `main` → tag release

---

## Opening Issues

Before opening an issue:

- Search existing issues to avoid duplicates.
- For bugs, include: OS, .NET version, steps to reproduce, expected vs. actual behaviour.
- For feature requests, describe the problem you are trying to solve.
- For security vulnerabilities, **do not open a public issue** — use GitHub private vulnerability reporting.

---

## Submitting a Pull Request

1. Fork the repo and create your branch from `dev`, not `main`.
2. Name your branch `feature/short-description` or `fix/short-description`.
3. Keep PRs focused — one change per PR.
4. Ensure the project builds: `dotnet build`
5. Write a clear PR description — what changed and why.
6. Link any related issue (`Closes #123`).

PRs targeting `main` directly will be closed.

---

## Security Constraints

The relay is a **blind pass-through** — it must never read, log, or store message content. Any change to connection handling, room lifetime, or rate limiting must be discussed in an issue first.

---

## Building Locally

```bash
git clone https://github.com/skynrlabs/SemaBuzz-Relay.git
cd SemaBuzz-Relay
dotnet build
dotnet run
```

The relay listens on port 7171 by default.

---

## Questions

Open a GitHub Discussion if you have a question that is not a bug or feature request.
