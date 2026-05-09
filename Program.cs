using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
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
Row("File TTL", "10 min  (staged files auto-expire)");
Row("Max rooms", "500  (global)");
Row("Max per IP", "5  concurrent connections");

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  ───────────────────────────────────────────────────────────────────────");
Console.ResetColor();

Row("License", "MIT License. Copyright (c) 2026 Skynr Labs.", ConsoleColor.Yellow);

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

// Allow generic browser-based frontend applications to use the HTTP endpoints (like /file)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
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
app.MapGet("/", () => Results.Ok("Relay OK"));

// ── File staging (POST /file, GET /file/{token}) ─────────────────────────────
// Files are held in RAM for up to 10 minutes.  The token is a 16-char lowercase
// hex string generated from 8 cryptographically-random bytes.  Slots are
// consumed on the first successful GET, or swept after expiry.
const long MaxStagedFileBytes = 10L * 1024 * 1024;  // 10 MB per file
const int  MaxStagedFiles     = 200;                 // global in-memory cap
var fileStagingTtl = TimeSpan.FromMinutes(10);
var stagedFiles = new ConcurrentDictionary<string, (byte[] Data, DateTime Expiry)>(
    StringComparer.OrdinalIgnoreCase);

// Background sweep: remove expired entries every 2 minutes.
_ = Task.Run(async () =>
{
    while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), app.Lifetime.ApplicationStopping); }
        catch (OperationCanceledException) { break; }
        var now = DateTime.UtcNow;
        foreach (var kv in stagedFiles)
            if (now > kv.Value.Expiry) stagedFiles.TryRemove(kv.Key, out _);
    }
});

// POST /file — upload a file (up to 10 MB), returns a 16-char hex token.
app.MapPost("/file", async (HttpContext ctx) =>
{
    if (ctx.Request.ContentLength > MaxStagedFileBytes)
    {
        ctx.Response.StatusCode = 413;
        await ctx.Response.WriteAsync("File too large (max 10 MB).");
        return;
    }
    if (stagedFiles.Count >= MaxStagedFiles)
    {
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsync("Server is at capacity; try again later.");
        return;
    }
    using var ms = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
    if (ms.Length == 0)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Empty body.");
        return;
    }
    if (ms.Length > MaxStagedFileBytes)
    {
        ctx.Response.StatusCode = 413;
        await ctx.Response.WriteAsync("File too large (max 10 MB).");
        return;
    }
    var tokenBytes = RandomNumberGenerator.GetBytes(8);
    var token = Convert.ToHexString(tokenBytes).ToLowerInvariant(); // 16 hex chars
    stagedFiles[token] = (ms.ToArray(), DateTime.UtcNow.Add(fileStagingTtl));
    ctx.Response.ContentType = "text/plain";
    await ctx.Response.WriteAsync(token);
});

// GET /file/{token} — download and consume a staged file (single-use).
app.MapGet("/file/{token}", async (HttpContext ctx, string token) =>
{
    if (!stagedFiles.TryRemove(token, out var entry))
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync("File not found or already downloaded.");
        return;
    }
    if (DateTime.UtcNow > entry.Expiry)
    {
        ctx.Response.StatusCode = 410;
        await ctx.Response.WriteAsync("File token has expired.");
        return;
    }
    ctx.Response.ContentType   = "application/octet-stream";
    ctx.Response.ContentLength = entry.Data.Length;
    await ctx.Response.Body.WriteAsync(entry.Data, ctx.RequestAborted);
});

await app.RunAsync();

