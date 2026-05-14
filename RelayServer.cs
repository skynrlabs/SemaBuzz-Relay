using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace SemaBuzz.Relay;

/// <summary>
/// WebSocket relay server. Each client connects to /relay, sends a JoinHost or
/// JoinDial control frame, and the relay pairs them and forwards all subsequent
/// binary frames transparently  no parsing of the SemaBuzz wire protocol.
/// TLS is handled by the hosting platform's reverse proxy (Railway, Fly.io, etc.).
/// </summary>
internal sealed class RelayServer
{
    private static readonly TimeSpan RoomTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(10);
    private readonly int _maxRooms;
    private const int MaxPerIp = 5;     // concurrent sockets per IP
    private const int MaxRoomsPerIp = 2;     // rooms a single IP may host at once (C-2)
    private const long BwCapBytesPerSec = 2 * 1024 * 1024; // 2 MB/s per session (H-1)

    private readonly ConcurrentDictionary<string, RelayRoom> _rooms =
        new(StringComparer.OrdinalIgnoreCase);

    // IP → number of currently-open WebSocket connections from that IP.
    private readonly ConcurrentDictionary<string, int> _connByIp = new();
    // IP → number of rooms currently hosted by that IP.
    private readonly ConcurrentDictionary<string, int> _roomsByIp = new();

    private readonly System.Timers.Timer _sweepTimer;

    public RelayServer(int maxRooms = 500)
    {
        _maxRooms = maxRooms;
        // M-4: sweep every 30 s so stale rooms are reaped within one TTL period, not two.
        _sweepTimer = new System.Timers.Timer(TimeSpan.FromSeconds(30)) { AutoReset = true };
        _sweepTimer.Elapsed += (_, _) => Sweep();
        _sweepTimer.Start();
    }

    // Entry point  one call per accepted WebSocket connection

    /// <summary>
    /// Entry point for each accepted WebSocket connection. Enforces per-IP connection limits,
    /// reads the JoinHost or JoinDial control frame, pairs the room, and relays all subsequent
    /// binary frames transparently until the socket closes or the cancellation token fires.
    /// </summary>
    public async Task HandleClientAsync(WebSocket ws, string remoteIp, CancellationToken ct)
    {
        // --- Per-IP connection cap ---
        var count = _connByIp.AddOrUpdate(remoteIp, 1, (_, c) => c + 1);
        if (count > MaxPerIp)
        {
            _connByIp.AddOrUpdate(remoteIp, 0, (_, c) => Math.Max(0, c - 1));
            await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Too many connections", ct);
            return;
        }

        try
        {
            await HandleInnerAsync(ws, remoteIp, ct);
        }
        finally
        {
            _connByIp.AddOrUpdate(remoteIp, 0, (_, c) => Math.Max(0, c - 1));
        }
    }

    private async Task HandleInnerAsync(WebSocket ws, string remoteIp, CancellationToken ct)
    {
        // --- Join phase: client must send a valid join frame within JoinTimeout ---
        var buf = new byte[64];
        WebSocketReceiveResult recv;
        using var joinCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        joinCts.CancelAfter(JoinTimeout);
        try { recv = await ws.ReceiveAsync(buf, joinCts.Token); }
        catch { await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Join timeout", ct); return; }

        if (recv.MessageType == WebSocketMessageType.Close || recv.Count < SemaBuzzRelayPacket.Size)
        {
            await CloseAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Expected join frame", ct);
            return;
        }

        var parsed = SemaBuzzRelayPacket.Parse(buf);
        if (parsed == null)
        {
            await CloseAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Bad join packet", ct);
            return;
        }

        var (type, token) = parsed.Value;

        if (type == SemaBuzzRelayPacketType.JoinHost)
        {
            // --- Global room cap ---
            if (_rooms.Count >= _maxRooms)
            {
                await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Server busy", ct);
                return;
            }

            // --- C-2: Per-IP room creation cap ---
            var ipRoomCount = _roomsByIp.AddOrUpdate(remoteIp, 1, (_, c) => c + 1);
            if (ipRoomCount > MaxRoomsPerIp)
            {
                _roomsByIp.AddOrUpdate(remoteIp, 0, (_, c) => Math.Max(0, c - 1));
                await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Too many rooms from this IP", ct);
                return;
            }

            var room = new RelayRoom(token, ws);
            _rooms[token] = room;
            try
            {
                // Block here until the WebSocket closes (or the dialer arrives and the session ends).
                await ForwardLoopAsync(ws, room, ct);
            }
            finally
            {
                _rooms.TryRemove(token, out _);
                _roomsByIp.AddOrUpdate(remoteIp, 0, (_, c) => Math.Max(0, c - 1));
            }
        }
        else if (type == SemaBuzzRelayPacketType.JoinDial)
        {
            if (!_rooms.TryGetValue(token, out var room) || room.HostWs.State != WebSocketState.Open)
            {
                var err = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.RelayError, token);
                try { await ws.SendAsync(err, WebSocketMessageType.Binary, true, ct); } catch { }
                await CloseAsync(ws, WebSocketCloseStatus.NormalClosure, "Token not found", ct);
                return;
            }

            room.SetDialer(ws);

            var paired = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.Paired, token);
            await room.SendToHostAsync(paired, ct);
            await room.SendToDialerAsync(paired, ct);

            await ForwardLoopAsync(ws, room, ct);
        }
        else
        {
            await CloseAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Unknown join type", ct);
        }
    }

    // Forward loop — read from this peer, send to the other
    // PunchReady frames (0x06) are intercepted and used to exchange external endpoints;
    // all other frames are forwarded transparently.

    private static async Task ForwardLoopAsync(WebSocket ws, RelayRoom room, CancellationToken ct)
    {
        var buf = new byte[65_536];
        var msgStream = new System.IO.MemoryStream(65_536);
        // H-1: sliding 1-second window to cap sustained bandwidth per session.
        long windowBytes = 0;
        var windowStart = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // Reassemble a complete WebSocket message — a single logical message may
                // span multiple frames when the OS delivers it in separate TCP segments.
                msgStream.SetLength(0);
                bool gotClose = false;
                while (true)
                {
                    WebSocketReceiveResult recv;
                    try { recv = await ws.ReceiveAsync(buf, ct); }
                    catch (OperationCanceledException) { return; }
                    catch { return; }

                    if (recv.MessageType == WebSocketMessageType.Close) { gotClose = true; break; }

                    // H-1: reset window each second; terminate session if sustained rate exceeds cap.
                    // Bandwidth is tracked per-frame so partial-message bytes count toward the window.
                    var now = DateTime.UtcNow;
                    if ((now - windowStart).TotalSeconds >= 1.0) { windowBytes = 0; windowStart = now; }
                    windowBytes += recv.Count;
                    if (windowBytes > BwCapBytesPerSec)
                    {
                        await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Bandwidth limit exceeded", ct);
                        return;
                    }

                    msgStream.Write(buf, 0, recv.Count);
                    if (recv.EndOfMessage) break;
                }

                if (gotClose) break;

                room.Touch();
                var msgBytes = msgStream.GetBuffer();
                var msgLen   = (int)msgStream.Length;
                var frame    = new ReadOnlyMemory<byte>(msgBytes, 0, msgLen);

                // Intercept PunchReady — do NOT forward to peer.
                if (msgLen == SemaBuzzRelayPacket.PunchPacketSize
                    && SemaBuzzRelayPacket.IsRelayPacket(msgBytes)
                    && (SemaBuzzRelayPacketType)msgBytes[3] == SemaBuzzRelayPacketType.PunchReady)
                {
                    var ep = SemaBuzzRelayPacket.ParseEndpoint(msgBytes[..msgLen]);
                    if (ep != null)
                    {
                        bool isHost = ReferenceEquals(ws, room.HostWs);
                        if (isHost) room.SetHostExternalEp(ep);
                        else room.SetDialerExternalEp(ep);

                        // Once both endpoints are known, tell each peer about the other.
                        if (room.HostExternalEp != null && room.DialerExternalEp != null)
                        {
                            var toHost = SemaBuzzRelayPacket.BuildPeerAddress(room.Token, room.DialerExternalEp);
                            var toDialer = SemaBuzzRelayPacket.BuildPeerAddress(room.Token, room.HostExternalEp);
                            await room.SendToHostAsync(toHost, ct);
                            await room.SendToDialerAsync(toDialer, ct);
                        }
                    }
                    continue; // never forward PunchReady frames
                }

                await room.ForwardToAsync(ws, frame, ct);
            }
        }
        finally
        {
            msgStream.Dispose();
            await CloseAsync(ws, WebSocketCloseStatus.NormalClosure, "Session ended", ct);
        }
    }

    // Expiry sweep

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - RoomTtl;
        foreach (var kvp in _rooms.ToArray())
        {
            if (kvp.Value.LastActive < cutoff)
                _rooms.TryRemove(kvp.Key, out _);
        }
    }

    // Helper

    private static async Task CloseAsync(WebSocket ws, WebSocketCloseStatus status, string desc, CancellationToken ct)
    {
        if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            try { await ws.CloseAsync(status, desc, CancellationToken.None); } catch { }
    }
}
