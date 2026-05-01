using System.Net.NetworkInformation;
using System.Net.Sockets;
using SemaBuzz.Relay;

// SemaBuzz Relay Server  (ASP.NET Core WebSocket relay)
//
// Hosting:
//   Railway / Render / Fly.io  — set PORT env var; TLS terminated by platform.
//   Self-hosted                — run behind nginx/Caddy for HTTPS.
//
// Usage:
//   dotnet run                         ← defaults to PORT env var or 7171
//   dotnet run -- --port 8080
//   SemaBuzz-Relay-Windows.exe --port 8080
//   ./SemaBuzz-Relay-Linux --port 8080
//
// Stopping:
//   Ctrl+C                             ← clean shutdown
//   Windows background: Stop-Process -Name "SemaBuzz-Relay-Windows"
//   Linux background:   pkill SemaBuzz-Relay-Linux
//   Docker:             docker stop <container-name>

var portStr = Environment.GetEnvironmentVariable("PORT");
int port;
if (int.TryParse(portStr, out var p))
    port = p;
else
    port = 7171;

for (var i = 0; i < args.Length - 1; i++)
    if ((args[i] == "--port" || args[i] == "-p") && int.TryParse(args[i + 1], out var ap))
        port = ap;

// ── Banner ────────────────────────────────────────────────────────────────────
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  ███████╗███████╗███╗   ███╗ █████╗ ██████╗ ██╗   ██╗███████╗███████╗");
Console.WriteLine("  ██╔════╝██╔════╝████╗ ████║██╔══██╗██╔══██╗██║   ██║╚══███╔╝╚══███╔╝");
Console.WriteLine("  ███████╗█████╗  ██╔████╔██║███████║██████╔╝██║   ██║  ███╔╝   ███╔╝ ");
Console.WriteLine("  ╚════██║██╔══╝  ██║╚██╔╝██║██╔══██║██╔══██╗██║   ██║ ███╔╝   ███╔╝  ");
Console.WriteLine("  ███████║███████╗██║ ╚═╝ ██║██║  ██║██████╔╝╚██████╔╝███████╗███████╗");
Console.WriteLine("  ╚══════╝╚══════╝╚═╝     ╚═╝╚═╝  ╚═╝╚═════╝  ╚═════╝ ╚══════╝╚══════╝");
Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("                    ·  R E L A Y   S E R V E R  ·");
Console.ResetColor();
Console.WriteLine();

// ── Server info ───────────────────────────────────────────────────────────────
var localIPs = NetworkInterface.GetAllNetworkInterfaces()
    .Where(ni => ni.OperationalStatus == OperationalStatus.Up
              && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
    .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
    .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
    .Select(ua => ua.Address.ToString())
    .ToList();

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  ───────────────────────────────────────────────────────────────────────");
Console.ResetColor();

static void Row(string label, string value, ConsoleColor valueColor = ConsoleColor.White)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write($"  {label,-14}");
    Console.ForegroundColor = valueColor;
    Console.WriteLine(value);
    Console.ResetColor();
}

Row("Version", "1.1.0");
Row("Port", port.ToString());
Row("Relay URI", $"ws://localhost:{port}/relay", ConsoleColor.Green);
foreach (var ip in localIPs)
    Row("", $"ws://{ip}:{port}/relay", ConsoleColor.Green);
Row("Health", $"http://localhost:{port}/");
Row("Keep-alive", "30 s");
Row("Room TTL", "10 min");
Row("Max rooms", "500  (global)");
Row("Max per IP", "5  concurrent connections");

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  ───────────────────────────────────────────────────────────────────────");
Console.ResetColor();

Row("License", "Copyright (c) 2026 Skynr Labs. All rights reserved.", ConsoleColor.Yellow);

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.Write($"  {"Privacy",-14}");
Console.ResetColor();
Console.WriteLine("Blind pass-through. Message content is never read,");
Console.WriteLine($"  {"",14}logged, or stored. IPs are held in memory only for");
Console.WriteLine($"  {"",14}the duration of an active session.");

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  ───────────────────────────────────────────────────────────────────────");
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.Write("  Press ");
Console.ForegroundColor = ConsoleColor.White;
Console.Write("Ctrl+C");
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine(" to stop.");
Console.ResetColor();
Console.WriteLine();

// ── ASP.NET Core host ─────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning); // quiet in production

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// C-1: Only trust X-Forwarded-For when TRUST_PROXY=true is explicitly set by the operator.
// Without this, any client can spoof an arbitrary IP and bypass the per-IP connection cap.
var trustProxy = string.Equals(
    Environment.GetEnvironmentVariable("TRUST_PROXY"), "true",
    StringComparison.OrdinalIgnoreCase);

var relay = new RelayServer();

// WebSocket endpoint: clients connect here to join a relay room.
app.Map("/relay", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 426;
        await ctx.Response.WriteAsync("WebSocket upgrade required.");
        return;
    }
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    // Only honour X-Forwarded-For when TRUST_PROXY=true is set — prevents IP spoofing.
    string? remoteIp = null;
    if (trustProxy)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (forwarded != null)
            remoteIp = forwarded.Split(',')[0].Trim();
    }
    if (remoteIp == null)
    {
        if (ctx.Connection.RemoteIpAddress != null)
            remoteIp = ctx.Connection.RemoteIpAddress.ToString();
    }
    if (remoteIp == null)
        remoteIp = "unknown";
    await relay.HandleClientAsync(ws, remoteIp, ctx.RequestAborted);
});

// Health check for Railway / Render uptime monitors.
app.MapGet("/", () => Results.Ok("SemaBuzz Relay OK"));

await app.RunAsync();

