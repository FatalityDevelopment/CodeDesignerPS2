using CodeDesigner.Ps2.Multiplayer.Config;
using CodeDesigner.Ps2.Multiplayer.Servers;
using CodeDesigner.Ps2.Multiplayer.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// ─────────────────────────────────────────────────────────────────────────────
// CodeDesigner.Ps2.Multiplayer
// PS2 online server emulator: Medius (MUIS + MAS + MLS) + DNAS
// Compatible with SOCOM II and other SCE Medius-based PS2 games.
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

// Config file
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("config.json", optional: true, reloadOnChange: false);

// Console logging + file logging
var logFile = Path.Combine(AppContext.BaseDirectory, "multiplayer.log");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new FileLoggerProvider(logFile));
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Load server config
var serverConfig = new MultiplayerConfig();
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
builder.Services.AddSingleton<DnsServer>();
builder.Services.AddSingleton<DmeServer>();
builder.Services.AddSingleton<NatServer>();

// Hosted service that starts everything
builder.Services.AddHostedService<MultiplayerHostedService>();

var host = builder.Build();
await host.RunAsync();
