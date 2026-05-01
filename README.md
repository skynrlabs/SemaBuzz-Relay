# SemaBuzz Relay

Self-hosted WebSocket relay server for [SemaBuzz](https://semabuzz.com).

The relay is a blind pass-through — it pairs two peers by token and forwards raw binary frames between them. Message content is never read, parsed, logged, or stored. IPs are held in memory only for the duration of an active session.

## Requirements

- .NET 9 SDK (to build from source), **or** use a pre-built binary from [Releases](../../releases)
- A reverse proxy (nginx, Caddy) for HTTPS/WSS in production — the relay itself serves plain HTTP/WS

## Quick Start

```bash
# Run with defaults (port 7171)
dotnet run

# Run on a custom port
dotnet run -- --port 8080

# Pre-built binary
SemaBuzz-Relay-Windows.exe --port 8080
./SemaBuzz-Relay-Linux --port 8080
```

The relay listens on:
- `ws://host:PORT/relay` — WebSocket endpoint for SemaBuzz clients
- `http://host:PORT/` — Health check (returns `200 OK`)

## Environment Variables

| Variable | Description | Default |
|---|---|---|
| `PORT` | Listening port | `7171` |
| `TRUST_PROXY` | Set `true` to honour `X-Forwarded-For` for real client IPs behind a reverse proxy | `false` |

## Deploying to a PaaS

The relay works out of the box on Railway, Render, Fly.io, and similar platforms. TLS is terminated by the platform — no extra configuration needed.

1. Fork this repo
2. Connect it to your Railway / Render / Fly.io project
3. Set `TRUST_PROXY=true` if the platform injects `X-Forwarded-For`
4. Point SemaBuzz at your relay by changing `DefaultRelayUri` in the app

## Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["./SemaBuzz-Relay-Linux"]
```

Build and publish:
```bash
dotnet publish -c Release -r linux-x64 --self-contained -o publish
docker build -t semabuzz-relay .
docker run -p 7171:7171 semabuzz-relay
```

## Rate Limits

| Limit | Value |
|---|---|
| Global room cap | 500 rooms |
| Rooms per IP | 2 concurrent |
| Connections per IP | 5 concurrent |
| Bandwidth cap | 2 MB/s per session |
| Room TTL (idle) | 10 minutes |

## License

Proprietary — see [LICENSE](LICENSE).

Copyright © 2026 Skynr Labs. All rights reserved.
