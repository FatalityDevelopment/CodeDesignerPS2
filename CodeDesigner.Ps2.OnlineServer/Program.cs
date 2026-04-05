using CodeDesigner.Ps2.OnlineServer.Config;
using CodeDesigner.Ps2.OnlineServer.Servers;
using CodeDesigner.Ps2.OnlineServer.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// CodeDesigner.Ps2.OnlineServer
// PS2 Medius online server emulator (MAS + MLS)
// Compatible with SOCOM II and other SCE Medius-based games.
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

// Load Medius config
var mediusConfig = new MediusConfig();
builder.Configuration.GetSection("Medius").Bind(mediusConfig);
builder.Services.AddSingleton(mediusConfig);

// Shared in-memory storage
builder.Services.AddSingleton<InMemoryStorage>();

// Servers
builder.Services.AddSingleton<MuisServer>();
builder.Services.AddSingleton<MasServer>();
builder.Services.AddSingleton<MlsServer>();

// Hosted service that starts everything
builder.Services.AddHostedService<MediusHostedService>();

var host = builder.Build();
await host.RunAsync();

// ─────────────────────────────────────────────────────────────────────────────

public class MediusHostedService : IHostedService
{
    private readonly MuisServer   _muis;
    private readonly MasServer    _mas;
    private readonly MlsServer    _mls;
    private readonly MediusConfig _config;
    private readonly ILogger<MediusHostedService> _log;
    private CancellationTokenSource _cts = new();

    public MediusHostedService(MuisServer muis, MasServer mas, MlsServer mls, MediusConfig config, ILogger<MediusHostedService> log)
    {
        _muis   = muis;
        _mas    = mas;
        _mls    = mls;
        _config = config;
        _log    = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        _log.LogInformation("═══════════════════════════════════════════════════");
        _log.LogInformation("  CodeDesigner PS2 Medius Server");
        _log.LogInformation("  Server IP   : {Ip}", _config.ServerIp);
        _log.LogInformation("  MUIS Port   : {Port}", _config.MuisPort);
        _log.LogInformation("  MAS Port    : {Port}", _config.MasPort);
        _log.LogInformation("  MLS Port    : {Port}", _config.MlsPort);
        _log.LogInformation("  AppIds      : {Ids}", string.Join(", ", _config.AllowedAppIds));
        _log.LogInformation("═══════════════════════════════════════════════════");

        await _muis.StartAsync(ct);
        await _mas.StartAsync(ct);
        await _mls.StartAsync(ct);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("Shutting down Medius servers...");
        _cts.Cancel();
        _muis.Stop();
        _mas.Stop();
        _mls.Stop();
        return Task.CompletedTask;
    }
}
