# 📡 SemaBuzz Relay

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Contributions: Read Only](https://img.shields.io/badge/Contributions-Read_Only-red.svg)](CONTRIBUTING.md)

Self-hosted WebSocket blind relay server. Originally built for the [**SemaBuzz Protocol**](https://github.com/skynrlabs/SemaBuzz-Protocol), it is completely protocol-agnostic and can be used by **any application** requiring encrypted peer-to-peer pairing.

The relay is a **blind pass-through** — it pairs two peers by a shared room token and forwards raw binary frames between them. Message content is never read, parsed, logged, or stored by the relay. IP addresses are held in memory only for the duration of an active session.

## 🛠️ Requirements

- **.NET 9 SDK** (to build from source), **or** use a pre-built binary from [Releases](../../releases)
- A reverse proxy (like Nginx or Caddy) for HTTPS/WSS in production — the relay itself serves plain HTTP/WS.

## ⚡ Quick Start

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
- `http://host:PORT/` — Health check (returns HTTP 200 OK)

## ⚙️ Environment Variables

| Variable | Description | Default |
|---|---|---|
| `PORT` | Listening port | `7171` |
| `TRUST_PROXY` | Set `true` to honour `X-Forwarded-For` for real client IPs behind a reverse proxy | `false` |

## ☁️ Deploying to a PaaS

The relay works out of the box on Railway, Render, Fly.io, and similar platforms. TLS is terminated by the platform — no extra configuration needed!

1. Fork this repo
2. Connect it to your Railway / Render / Fly.io project
3. Set `TRUST_PROXY=true` if the platform injects `X-Forwarded-For`
4. Point SemaBuzz at your relay by changing the `DefaultRelayUri` in your app

## 📦 Building Standalone Binaries

You can easily compile the relay into a single, self-contained executable for any platform—meaning the host machine won't even need .NET installed to run it!

```bash
# Build for Windows
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win

# Build for Linux
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux

# Build for macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/mac
```

## 🐳 Docker

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

## 🚥 Rate Limits

To prevent abuse, the relay currently enforces the following hardcoded limits:

| Limit | Value |
|---|---|
| Global room cap | 500 rooms |
| Rooms per IP | 2 concurrent |
| Connections per IP | 5 concurrent |
| Bandwidth cap | 2 MB/s per session |
| Room TTL (idle) | 10 minutes |

## 🤝 Contributing

Since this is a read-only release, we are **not** accepting upstream contributions (Issues or Pull Requests) at this time. However, you are completely free to fork the project and build upon it under the terms of the MIT license. Please review our [Contributing Guidelines](CONTRIBUTING.md).

## ⚖️ License

**SemaBuzz Relay** is open-source software licensed under the **MIT License**. See the LICENSE file for full details.

