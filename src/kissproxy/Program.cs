using kissproxylib;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

namespace kissproxy;

internal class Program
{
    private static readonly JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };

    private static async Task<int> Main(string[] args)
    {
        var comPortOption = new Option<string?>("--comport")
        {
            Description = "The COM port the modem is connected to, e.g. /dev/ttyACM0"
        };
        comPortOption.Aliases.Add("-c");

        var baudOption = new Option<int>("--baud")
        {
            Description = "The baud rate of the modem",
            DefaultValueFactory = _ => 57600
        };
        baudOption.Aliases.Add("-b");

        var tcpPortOption = new Option<int>("--tcpport")
        {
            Description = "The TCP port to listen on",
            DefaultValueFactory = _ => 8910
        };
        tcpPortOption.Aliases.Add("-p");

        var anyHostOption = new Option<bool>("--anyhost")
        {
            Description = "Whether to accept connections from any host, instead of just localhost"
        };
        anyHostOption.Aliases.Add("-a");

        var brokerOption = new Option<string?>("--mqtt-server")
        {
            Description = "MQTT server to forward KISS frames to"
        };
        brokerOption.Aliases.Add("-m");

        var brokerUserOption = new Option<string?>("--mqtt-user")
        {
            Description = "MQTT username"
        };
        brokerUserOption.Aliases.Add("-mu");

        var brokerPasswordOption = new Option<string?>("--mqtt-pass")
        {
            Description = "MQTT password"
        };
        brokerPasswordOption.Aliases.Add("-mp");

        var topicOption = new Option<string?>("--mqtt-topic-prefix")
        {
            Description = "MQTT topic prefix"
        };
        topicOption.Aliases.Add("-mt");

        var publishBase64Option = new Option<bool>("--base64")
        {
            Description = "Publish base64 strings rather than raw bytes"
        };

        var rootCommand = new RootCommand("Serial-to-TCP proxy for serial KISS modems, including MQTT tracing support.");
        rootCommand.Options.Add(comPortOption);
        rootCommand.Options.Add(baudOption);
        rootCommand.Options.Add(tcpPortOption);
        rootCommand.Options.Add(anyHostOption);
        rootCommand.Options.Add(brokerOption);
        rootCommand.Options.Add(brokerUserOption);
        rootCommand.Options.Add(brokerPasswordOption);
        rootCommand.Options.Add(topicOption);
        rootCommand.Options.Add(publishBase64Option);
        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            string configFile = "/etc/kissproxy.conf";

            if (!File.Exists(configFile))
            {
                configFile = "kissproxy.conf";
            }

            if (File.Exists(configFile))
            {
                LogInformation("", $"Using {configFile} and ignoring any command line parameters");

                var file = File.ReadAllText(configFile);
                Config[] config;
                try
                {
                    config = JsonSerializer.Deserialize<Config[]>(file, options) ?? throw new InvalidOperationException();
                }
                catch (Exception ex)
                {
                    LogError("", $"Error reading config file: {ex.Message}");
                    Environment.Exit(-1);
                    return;
                }

                Collection<Task> tasks = [];
                foreach (var instance in config)
                {
                    KissProxy proxy = new(instance.Id, new ConsoleLogger());
                    tasks.Add(Task.Run(async () => await proxy.Run(instance.ComPort, instance.Baud, instance.TcpPort, instance.AnyHost, instance.MqttServer, instance.MqttUsername, instance.MqttPassword, instance.MqttTopicPrefix, instance.Base64)));
                }
                Task.WaitAll([.. tasks]);
            }
            else
            {
                var modemComPort = parseResult.GetValue(comPortOption);
                var modemSerialBaud = parseResult.GetValue(baudOption);
                var listenForNodeOnTcpPort = parseResult.GetValue(tcpPortOption);
                var allowTcpConnectFromOtherHosts = parseResult.GetValue(anyHostOption);
                var mqttServer = parseResult.GetValue(brokerOption);
                var mqttUser = parseResult.GetValue(brokerUserOption);
                var mqttPassword = parseResult.GetValue(brokerPasswordOption);
                var mqttTopicPrefix = parseResult.GetValue(topicOption);
                var emitFramesToMqttAsBase64String = parseResult.GetValue(publishBase64Option);

                await new KissProxy(new ConsoleLogger())
                    .Run(modemComPort!, modemSerialBaud, listenForNodeOnTcpPort, allowTcpConnectFromOtherHosts, mqttServer, mqttUser, mqttPassword, mqttTopicPrefix, emitFramesToMqttAsBase64String);
            }
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private class ConsoleLogger : ILogger
    {
        private string instanceName = "";

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel > LogLevel.Information)
            {
                LogError(instanceName, formatter(state, exception));
            }
            else if (logLevel > LogLevel.Debug)
            {
                LogInformation(instanceName, formatter(state, exception));
            }
            else if (logLevel > LogLevel.Trace)
            {
                LogDebug(instanceName, formatter(state, exception));
            }
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state != null)
            {
                instanceName = state.ToString()!;
            }

            return new MyDisposable();
        }

        public sealed class MyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private static void LogDebug(string instanceName, string message) => Debug.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {instanceName}{(instanceName == "" ? "" : "  ")}{message}");
    private static void LogInformation(string instanceName, string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {instanceName}{(instanceName == "" ? "" : "  ")}{message}");
    private static void LogError(string instanceName, string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {instanceName}{(instanceName == "" ? "" : "  ")}{message}");
}
