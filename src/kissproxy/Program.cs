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

    private static int Main(string[] args)
    {
        var comPortOption = new Option<string?>("--comport", "The COM port the modem is connected to, e.g. /dev/ttyACM0");
        comPortOption.AddAlias("-c");

        var baudOption = new Option<int>("--baud", "The baud rate of the modem");
        baudOption.AddAlias("-b");
        baudOption.SetDefaultValue(57600);

        var tcpPortOption = new Option<int>("--tcpport", "The TCP port to listen on");
        tcpPortOption.AddAlias("-p");
        tcpPortOption.SetDefaultValue(8910);

        var anyHostOption = new Option<bool>(
            name: "--anyhost",
            description: "Whether to accept connections from any host, instead of just localhost",
            getDefaultValue: () => false);
        anyHostOption.AddAlias("-a");

        var brokerOption = new Option<string?>("--mqtt-server", "MQTT server to forward KISS frames to");
        brokerOption.AddAlias("-m");

        var brokerUserOption = new Option<string?>("--mqtt-user", "MQTT username");
        brokerUserOption.AddAlias("-mu");

        var brokerPasswordOption = new Option<string?>("--mqtt-pass", "MQTT password");
        brokerPasswordOption.AddAlias("-mp");

        var topicOption = new Option<string?>("--mqtt-topic-prefix", "MQTT topic prefix");
        topicOption.AddAlias("-mt");

        var publishBase64Option = new Option<bool>("--base64", "Publish base64 strings rather than raw bytes");

        var rootCommand = new RootCommand("Serial-to-TCP proxy for serial KISS modems, including MQTT tracing support.");
        rootCommand.AddOption(comPortOption);
        rootCommand.AddOption(baudOption);
        rootCommand.AddOption(tcpPortOption);
        rootCommand.AddOption(anyHostOption);
        rootCommand.AddOption(brokerOption);
        rootCommand.AddOption(brokerUserOption);
        rootCommand.AddOption(brokerPasswordOption);
        rootCommand.AddOption(topicOption);
        rootCommand.AddOption(publishBase64Option);
        rootCommand.SetHandler(async context =>
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
                var modemComPort = context.ParseResult.GetValueForOption(comPortOption);
                var modemSerialBaud = context.ParseResult.GetValueForOption(baudOption);
                var listenForNodeOnTcpPort = context.ParseResult.GetValueForOption(tcpPortOption);
                var allowTcpConnectFromOtherHosts = context.ParseResult.GetValueForOption(anyHostOption);
                var mqttServer = context.ParseResult.GetValueForOption(brokerOption);
                var mqttUser = context.ParseResult.GetValueForOption(brokerUserOption);
                var mqttPassword = context.ParseResult.GetValueForOption(brokerPasswordOption);
                var mqttTopicPrefix = context.ParseResult.GetValueForOption(topicOption);
                var emitFramesToMqttAsBase64String = context.ParseResult.GetValueForOption(publishBase64Option);

                await new KissProxy(new ConsoleLogger())
                    .Run(modemComPort!, modemSerialBaud, listenForNodeOnTcpPort, allowTcpConnectFromOtherHosts, mqttServer, mqttUser, mqttPassword, mqttTopicPrefix, emitFramesToMqttAsBase64String);
            }
        });

        return rootCommand.Invoke(args);
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