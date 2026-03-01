using kissproxy;
using kissproxylib;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

// Determine config file path
string configPath = File.Exists("/etc/kissproxy.conf")
    ? "/etc/kissproxy.conf"
    : "kissproxy.conf";

// Load configuration (create default if doesn't exist)
var configManager = new ConfigManager(configPath);
GlobalConfig config;
try
{
    if (!File.Exists(configPath))
    {
        // Create default config file
        var defaultConfig = new GlobalConfig
        {
            WebPort = 8080,
            Password = "changeme",
            Modems = []
        };
        var defaultJson = System.Text.Json.JsonSerializer.Serialize(defaultConfig, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(configPath, defaultJson);
        LogInfo($"Created default configuration at {configPath}");
    }

    config = configManager.Load();
    LogInfo($"Loaded configuration from {configPath}");
}
catch (Exception ex)
{
    LogError($"Error loading config: {ex.Message}");
    return 1;
}

// Check if we have any modems configured
if (config.Modems.Count == 0)
{
    LogInfo("No modems configured. Starting web UI for initial setup.");
}

// Check config writeability
if (configManager.IsWriteable)
{
    LogInfo("Config file is writeable");
}
else
{
    LogWarn($"Config file is NOT writeable: {configPath}");
}

// Check if password needs to be set
if (configManager.NeedsMigration)
{
    LogWarn("Config needs migration - password must be set via web UI before saving");
}

// Create state manager
var stateManager = new ModemStateManager();

// Create proxy instances dictionary
var proxyInstances = new Dictionary<string, KissProxy>();

// Cancellation token for graceful shutdown
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    LogInfo("Shutdown requested...");
};

// Start web server
var builder = WebApplication.CreateBuilder();
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// Serve static files from wwwroot
var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath)
    });
    app.MapFallbackToFile("index.html");
}
else
{
    // Development: try to find wwwroot relative to source
    var devWwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    if (Directory.Exists(devWwwroot))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(devWwwroot)
        });
        app.MapFallbackToFile("index.html");
    }
    else
    {
        LogWarn($"wwwroot directory not found at {wwwrootPath} or {devWwwroot}");
    }
}

// Map API endpoints
WebApi.MapEndpoints(app, configManager, stateManager, proxyInstances);

// Start modem proxy tasks
var proxyTasks = new List<Task>();
foreach (var modemConfig in config.Modems)
{
    var state = stateManager.GetOrCreate(modemConfig.Id);
    var logger = new ConsoleLogger(modemConfig.Id);
    var proxy = new KissProxy(modemConfig.Id, logger);

    proxyInstances[modemConfig.Id] = proxy;

    var task = Task.Run(async () =>
    {
        try
        {
            await proxy.Run(modemConfig, config, state, cts.Token);
        }
        catch (Exception ex)
        {
            LogError($"Modem {modemConfig.Id} proxy error: {ex.Message}");
        }
    });
    proxyTasks.Add(task);

    LogInfo($"Started proxy for modem '{modemConfig.Id}' on port {modemConfig.TcpPort}");
}

// Subscribe to config changes to start/stop proxies
configManager.ModemConfigChanged += modemId =>
{
    LogInfo($"Config changed for modem '{modemId}'");

    // If proxy exists, notify it of config change
    if (proxyInstances.TryGetValue(modemId, out var proxy))
    {
        var newConfig = configManager.GetModem(modemId);
        if (newConfig != null)
        {
            proxy.OnConfigChanged(newConfig);
        }
    }
};

// Run web server
var webUrl = $"http://0.0.0.0:{config.WebPort}";
LogInfo($"Starting web server on {webUrl}");

try
{
    await app.RunAsync(webUrl);
}
catch (Exception ex)
{
    LogError($"Web server error: {ex.Message}");
    cts.Cancel();
}

// Wait for proxy tasks to complete
await Task.WhenAll(proxyTasks);

return 0;

// Helper methods
static void LogInfo(string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {message}");
static void LogWarn(string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  WARN: {message}");
static void LogError(string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  ERROR: {message}");

/// <summary>
/// Simple console logger implementation.
/// </summary>
class ConsoleLogger : ILogger
{
    private readonly string instanceName;
    private string? scopeName;

    public ConsoleLogger(string instanceName = "")
    {
        this.instanceName = instanceName;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var name = scopeName ?? instanceName;
        var prefix = string.IsNullOrEmpty(name) ? "" : $"[{name}] ";
        var message = formatter(state, exception);

        var levelStr = logLevel switch
        {
            LogLevel.Error or LogLevel.Critical => "ERROR",
            LogLevel.Warning => "WARN",
            LogLevel.Debug => "DEBUG",
            _ => "INFO"
        };

        if (logLevel >= LogLevel.Debug)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {levelStr}: {prefix}{message}");
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {prefix}{message}");
            }
        }
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        scopeName = state.ToString();
        return new ScopeDisposable(() => scopeName = null);
    }

    private class ScopeDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
