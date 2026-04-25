// Copied from SemaBuzz.Protocol — kept here so the relay is self-contained
// and can be published as a standalone open-source repo without referencing
// the private app protocol library.

using System.Security.Cryptography;

namespace SemaBuzz.Relay;

/// <summary>
/// Control packets exchanged between SemaBuzz clients and the relay server.
///
/// Wire format:
///   [0x52][0x4C]  2-byte magic ("RL")
///   [0x01]        version
///   [type]        1-byte packet type
///   [token:6]     6-byte ASCII room token (uppercase alphanumeric)
/// Total: 10 bytes.
///
/// After pairing, data packets are forwarded raw (no relay wrapper) so the
/// existing SemaBuzz ECDH handshake and wire protocol run through the relay
/// transparently without modification.
/// </summary>
public static class SemaBuzzRelayPacket
{
    public const byte Magic1 = 0x52; // 'R'
    public const byte Magic2 = 0x4C; // 'L'
    public const byte Version = 0x01;
    public const int Size = 10;   // 2 magic + 1 version + 1 type + 6 token
    public const int TokenLength = 6;
    public const int PunchPacketSize = 16;   // standard 10-byte header + 4-byte IPv4 + 2-byte port

    // Relay server connection details.
    // DefaultRelayUri is the WebSocket endpoint used by both the listener and client.
    // Change this to your deployed relay URL before shipping.
    public const string DefaultRelayUri = "wss://relay.semabuzz.me/relay";
    public const string DefaultRelayHost = "relay.semabuzz.me"; // kept for reference

    public static bool IsRelayPacket(byte[] data) =>
        data.Length >= Size &&
        data[0] == Magic1 &&
        data[1] == Magic2 &&
        data[2] == Version;

    public static byte[] Build(SemaBuzzRelayPacketType type, string token)
    {
        var buf = new byte[Size];
        buf[0] = Magic1;
        buf[1] = Magic2;
        buf[2] = Version;
        buf[3] = (byte)type;
        var tokenBytes = System.Text.Encoding.ASCII.GetBytes(
            token.ToUpperInvariant().PadRight(TokenLength)[..TokenLength]);
        tokenBytes.CopyTo(buf, 4);
        return buf;
    }

    public static (SemaBuzzRelayPacketType Type, string Token)? Parse(byte[] data)
    {
        if (!IsRelayPacket(data)) return null;
        var type = (SemaBuzzRelayPacketType)data[3];
        var token = System.Text.Encoding.ASCII.GetString(data, 4, TokenLength).Trim();
        return (type, token);
    }

    /// <summary>Build a PunchReady packet (client -> relay: my external UDP endpoint).</summary>
    public static byte[] BuildPunchReady(string token, System.Net.IPEndPoint ep)
        => BuildEndpointPacket(SemaBuzzRelayPacketType.PunchReady, token, ep);

    /// <summary>Build a PeerAddress packet (relay -> client: peer's external UDP endpoint).</summary>
    public static byte[] BuildPeerAddress(string token, System.Net.IPEndPoint ep)
        => BuildEndpointPacket(SemaBuzzRelayPacketType.PeerAddress, token, ep);

    private static byte[] BuildEndpointPacket(SemaBuzzRelayPacketType type, string token, System.Net.IPEndPoint ep)
    {
        var buf = new byte[PunchPacketSize];
        buf[0] = Magic1;
        buf[1] = Magic2;
        buf[2] = Version;
        buf[3] = (byte)type;
        var tokenBytes = System.Text.Encoding.ASCII.GetBytes(
            token.ToUpperInvariant().PadRight(TokenLength)[..TokenLength]);
        tokenBytes.CopyTo(buf, 4);
        // IPv4 address bytes 10-13 (big-endian)
        var ipBytes = ep.Address.GetAddressBytes();
        if (ipBytes.Length == 4) ipBytes.CopyTo(buf, 10);
        // Port bytes 14-15 (big-endian)
        buf[14] = (byte)(ep.Port >> 8);
        buf[15] = (byte)(ep.Port & 0xFF);
        return buf;
    }

    /// <summary>
    /// Extract the IP:port payload from a PunchReady or PeerAddress packet.
    /// Returns null if the data is too short or not a relay packet.
    /// </summary>
    public static System.Net.IPEndPoint? ParseEndpoint(byte[] data)
    {
        if (data.Length < PunchPacketSize || !IsRelayPacket(data)) return null;
        var ip = new System.Net.IPAddress(data[10..14]);
        var port = (data[14] << 8) | data[15];
        return new System.Net.IPEndPoint(ip, port);
    }

    /// <summary>
    /// Generate a random 6-character uppercase token.
    /// Omits I, O, 0, 1 to avoid visual confusion when reading aloud.
    /// </summary>
    public static string GenerateToken()
    {
        const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = new byte[TokenLength];
        RandomNumberGenerator.Fill(bytes);
        return new string(bytes.Select(b => Chars[b % Chars.Length]).ToArray());
    }
}

public enum SemaBuzzRelayPacketType : byte
{
    JoinHost = 0x01, // Client -> relay: register as host for this token
    JoinDial = 0x02, // Client -> relay: dial into room with this token
    Paired = 0x03, // Relay -> client: both peers connected, start the wire
    RelayError = 0x04, // Relay -> client: token not found or other error
    Ping = 0x05, // Bidirectional keepalive to maintain NAT mappings
    PunchReady = 0x06, // Client -> relay: my external UDP endpoint (extended packet)
    PeerAddress = 0x07, // Relay -> client: your peer's external UDP endpoint (extended packet)
}
