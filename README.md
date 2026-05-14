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
# Run with defaults (port 7171, max 500 rooms)
dotnet run

# Run on a custom port
dotnet run -- --port 8080

# Limit concurrent rooms (recommended for home servers)
dotnet run -- --max-rooms 20

# Combined
dotnet run -- --port 8080 --max-rooms 20

# Pre-built binary
SemaBuzz-Relay-Windows.exe --port 8080 --max-rooms 20
./SemaBuzz-Relay-Linux --port 8080 --max-rooms 20
```

## 🖥️ Startup Banner

When launched, the relay prints a summary showing all available connection URIs:

```
  Version       1.2.2
  Port          7171
  Relay URI     ws://localhost:7171/relay
  Ethernet      ws://192.168.1.x:7171/relay
  WiFi          ws://192.168.0.y:7171/relay
  Public IP     ws://203.0.113.z:7171/relay  ← share this (requires port forwarding)
  Health        http://localhost:7171/
  Max rooms     20  (global)
```

Only Ethernet and WiFi adapters are shown (virtual adapters and tunnels are excluded). The public IP is discovered automatically via STUN — no external API key needed.

> **Self-hosting behind a home router?** Port forwarding on port 7171 (TCP) is required before the Public IP URI will work for external peers — similar to Plex or game servers. Client machines connecting *to* a relay do not need any special firewall configuration.

## 🔌 Endpoints

| Endpoint | Protocol | Description |
|---|---|---|
| `/relay` | WebSocket | Primary peer-pairing endpoint. Two clients connecting with the same 6-character room token are bridged together and raw binary frames are forwarded blindly between them. |
| `/file` | HTTP POST | Stage a binary blob (up to 10 MB) for out-of-band transfer. Returns a short-lived token the receiving peer can use to fetch the file. Staged files expire after 10 minutes. |
| `/file/{token}` | HTTP GET | Retrieve a previously staged file by its token. Once fetched or expired, the file is removed from memory. |
| `/` | HTTP GET | Health check. Returns HTTP 200 OK. Useful for uptime monitors and PaaS health probes. |

> **Note:** All HTTP endpoints (`/file`, `/`) support CORS from any origin, so browser-based apps can interact with them directly.

## 🔗 Wire Protocol

To pair two peers via `/relay`, each client must open a WebSocket connection and send a **10-byte control packet** as its very first message:

| Bytes | Field   | Value |
|---|---|---|
| 0–1   | Magic   | `0x52 0x4C` (ASCII `RL`) |
| 2     | Version | `0x01` |
| 3     | Type    | See table below |
| 4–9   | Token   | 6-byte uppercase ASCII room token |

**Packet types:**

| Value | Name | Direction | Description |
|---|---|---|---|
| `0x01` | `JoinHost` | Client → Relay | First peer registers the room with a token |
| `0x02` | `JoinDial` | Client → Relay | Second peer joins the same room by token |
| `0x03` | `Paired` | Relay → Client | Both peers connected — begin streaming |
| `0x04` | `RelayError` | Relay → Client | Token not found, room full, or capacity exceeded |
| `0x05` | `Ping` | Bidirectional | Keep-alive to maintain NAT mappings |

**Typical pairing flow:**
1. Peer A opens `ws://host/relay` and sends `JoinHost` with token `ABC123`
2. Peer B opens `ws://host/relay` and sends `JoinDial` with token `ABC123`
3. The relay sends `Paired` to both clients
4. Both peers may now stream any binary payload — the relay forwards it blindly

After pairing, **no relay framing is applied to data frames** — whatever bytes you send are forwarded as-is to the other peer.

## ⚙️ Environment Variables & CLI Arguments

All options can be set via CLI argument or environment variable. CLI takes precedence.

| CLI Argument | Env Variable | Description | Default |
|---|---|---|---|
| `--port` / `-p` | `PORT` | Listening port | `7171` |
| `--max-rooms` | `MAX_ROOMS` | Maximum concurrent relay rooms | `500` |
| _(none)_ | `TRUST_PROXY` | Set `true` to honour `X-Forwarded-For` behind a reverse proxy | `false` |

**Recommended values by deployment type:**

| Deployment | `--max-rooms` |
|---|---|
| Home server | `10` – `50` |
| Small VPS (1 vCPU / 1 GB) | `100` – `200` |
| Mid-tier VPS (2 vCPU / 4 GB) | `500` (default) |

## ☁️ Deploying to a PaaS

The relay works out of the box on Railway, Render, Fly.io, and similar platforms. TLS is terminated by the platform — no extra configuration needed!

1. Fork this repo
2. Connect it to your Railway / Render / Fly.io project
3. Set `TRUST_PROXY=true` if the platform injects `X-Forwarded-For`
4. Optionally set `MAX_ROOMS` for your expected load
5. Point your application at your relay by updating your WebSocket connection URIs.

## 📦 Building Standalone Binaries

You can easily compile the relay into a single, self-contained executable for any platform—meaning the host machine won't even need .NET installed to run it!

```bash
# Build for Windows (x64)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win

# Build for Linux (x64)
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux

# Build for macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/mac

# Build for macOS (Intel)
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true -o publish/mac-intel
```

## 🐳 Docker

You can easily run the relay using Docker. The following multi-stage `Dockerfile` is platform-agnostic and will build and run the relay universally (including on Windows hosts via Docker Desktop).

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish SemaBuzz.Relay.csproj -c Release -r linux-x64 --self-contained false -o /app/publish

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SemaBuzz.Relay.dll"]
```

Build and run the container:
```bash
docker build -t semabuzz-relay .
docker run -p 7171:7171 -e MAX_ROOMS=50 semabuzz-relay
```

## 🚥 Rate Limits

| Limit | Value | Configurable |
|---|---|---|
| Global room cap | 500 rooms (default) | ✅ `--max-rooms` / `MAX_ROOMS` |
| Rooms per IP | 2 hosted rooms per IP | No |
| Connections per IP | 5 concurrent | No |
| Bandwidth cap | 2 MB/s per session | No |
| Room TTL (idle) | 10 minutes | No |

## 🤝 Contributing

Since this is a read-only release, we are **not** accepting upstream contributions (Issues or Pull Requests) at this time. However, you are completely free to fork the project and build upon it under the terms of the MIT license. Please review our [Contributing Guidelines](CONTRIBUTING.md).

## ⚖️ License

**SemaBuzz Relay** is open-source software licensed under the **MIT License**. See the LICENSE file for full details.


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

## 🔌 Endpoints

| Endpoint | Protocol | Description |
|---|---|---|
| `/relay` | WebSocket | Primary peer-pairing endpoint. Two clients connecting with the same 6-character room token are bridged together and raw binary frames are forwarded blindly between them. |
| `/file` | HTTP POST | Stage a binary blob (up to 10 MB) for out-of-band transfer. Returns a short-lived token the receiving peer can use to fetch the file. Staged files expire after 10 minutes. |
| `/file/{token}` | HTTP GET | Retrieve a previously staged file by its token. Once fetched or expired, the file is removed from memory. |
| `/` | HTTP GET | Health check. Returns HTTP 200 OK. Useful for uptime monitors and PaaS health probes. |

> **Note:** All HTTP endpoints (`/file`, `/`) support CORS from any origin, so browser-based apps can interact with them directly.

## 🔗 Wire Protocol

To pair two peers via `/relay`, each client must open a WebSocket connection and send a **10-byte control packet** as its very first message:

| Bytes | Field   | Value |
|---|---|---|
| 0–1   | Magic   | `0x52 0x4C` (ASCII `RL`) |
| 2     | Version | `0x01` |
| 3     | Type    | See table below |
| 4–9   | Token   | 6-byte uppercase ASCII room token |

**Packet types:**

| Value | Name | Direction | Description |
|---|---|---|---|
| `0x01` | `JoinHost` | Client → Relay | First peer registers the room with a token |
| `0x02` | `JoinDial` | Client → Relay | Second peer joins the same room by token |
| `0x03` | `Paired` | Relay → Client | Both peers connected — begin streaming |
| `0x04` | `RelayError` | Relay → Client | Token not found, room full, or capacity exceeded |
| `0x05` | `Ping` | Bidirectional | Keep-alive to maintain NAT mappings |

**Typical pairing flow:**
1. Peer A opens `ws://host/relay` and sends `JoinHost` with token `ABC123`
2. Peer B opens `ws://host/relay` and sends `JoinDial` with token `ABC123`
3. The relay sends `Paired` to both clients
4. Both peers may now stream any binary payload — the relay forwards it blindly

After pairing, **no relay framing is applied to data frames** — whatever bytes you send are forwarded as-is to the other peer.

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
4. Point your application at your relay by updating your WebSocket connection URIs.

## 📦 Building Standalone Binaries

You can easily compile the relay into a single, self-contained executable for any platform—meaning the host machine won't even need .NET installed to run it!

```bash
# Build for Windows (x64)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win

# Build for Linux (x64)
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux

# Build for macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/mac

# Build for macOS (Intel)
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true -o publish/mac-intel
```

## 🐳 Docker

You can easily run the relay using Docker. The following multi-stage `Dockerfile` is platform-agnostic and will build and run the relay universally (including on Windows hosts via Docker Desktop).

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish SemaBuzz.Relay.csproj -c Release -r linux-x64 --self-contained false -o /app/publish

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SemaBuzz.Relay.dll"]
```

Build and run the container:
```bash
docker build -t semabuzz-relay .
docker run -p 7171:7171 semabuzz-relay
```

## 🚥 Rate Limits

To prevent abuse, the relay currently enforces the following hardcoded limits:

| Limit | Value |
|---|---|
| Global room cap | 500 rooms |
| Rooms per IP | 2 hosted rooms per IP |
| Connections per IP | 5 concurrent |
| Bandwidth cap | 2 MB/s per session |
| Room TTL (idle) | 10 minutes |

## 🤝 Contributing

Since this is a read-only release, we are **not** accepting upstream contributions (Issues or Pull Requests) at this time. However, you are completely free to fork the project and build upon it under the terms of the MIT license. Please review our [Contributing Guidelines](CONTRIBUTING.md).

## ⚖️ License

**SemaBuzz Relay** is open-source software licensed under the **MIT License**. See the LICENSE file for full details.

