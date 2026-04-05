using CodeDesigner.Ps2.OnlineServer.Config;
using CodeDesigner.Ps2.OnlineServer.Servers;
using CodeDesigner.Ps2.OnlineServer.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// CodeDesigner.Ps2.OnlineServer
// PS2 online server emulator: Medius (MUIS + MAS + MLS) + DNAS
// Compatible with SOCOM II and other SCE Medius-based PS2 games.
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

// Config file
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("config.json", optional: true, reloadOnChange: false);

// Console logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Load server config
var serverConfig = new OnlineServerConfig();
builder.Configuration.GetSection("Online").Bind(serverConfig);
// Also accept the legacy "Medius" section so old config.json files still work
if (serverConfig.ServerIp == "127.0.0.1" && builder.Configuration.GetSection("Medius").Exists())
    builder.Configuration.GetSection("Medius").Bind(serverConfig);

builder.Services.AddSingleton(serverConfig);

// Shared in-memory storage
builder.Services.AddSingleton<InMemoryStorage>();

// Servers
builder.Services.AddSingleton<MuisServer>();
builder.Services.AddSingleton<MasServer>();
builder.Services.AddSingleton<MlsServer>();
builder.Services.AddSingleton<DnasServer>();

// Hosted service that starts everything
builder.Services.AddHostedService<OnlineHostedService>();

var host = builder.Build();
await host.RunAsync();

// ─────────────────────────────────────────────────────────────────────────────

public class OnlineHostedService : IHostedService
{
    private readonly MuisServer          _muis;
    private readonly MasServer           _mas;
    private readonly MlsServer           _mls;
    private readonly DnasServer          _dnas;
    private readonly OnlineServerConfig  _config;
    private readonly ILogger<OnlineHostedService> _log;
    private CancellationTokenSource _cts = new();

    public OnlineHostedService(
        MuisServer muis, MasServer mas, MlsServer mls, DnasServer dnas,
        OnlineServerConfig config, ILogger<OnlineHostedService> log)
    {
        _muis   = muis;
        _mas    = mas;
        _mls    = mls;
        _dnas   = dnas;
        _config = config;
        _log    = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        _log.LogInformation("═══════════════════════════════════════════════════");
        _log.LogInformation("  CodeDesigner PS2 Online Server");
        _log.LogInformation("  Server IP   : {Ip}",   _config.ServerIp);
        _log.LogInformation("  MUIS Port   : {Port}", _config.MuisPort);
        _log.LogInformation("  MAS Port    : {Port}", _config.MasPort);
        _log.LogInformation("  MLS Port    : {Port}", _config.MlsPort);
        _log.LogInformation("  DNAS Port   : {Port} (region={Region})", _config.DnasPort, _config.DnasRegion);
        _log.LogInformation("  AppIds      : {Ids}",  string.Join(", ", _config.AllowedAppIds));
        _log.LogInformation("═══════════════════════════════════════════════════");

        await _muis.StartAsync(ct);
        await _mas.StartAsync(ct);
        await _mls.StartAsync(ct);
        await _dnas.StartAsync(ct);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("Shutting down online servers...");
        _cts.Cancel();
        _muis.Stop();
        _mas.Stop();
        _mls.Stop();
        _dnas.Stop();
        return Task.CompletedTask;
    }
}

