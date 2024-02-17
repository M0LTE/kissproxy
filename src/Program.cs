using System.Collections.ObjectModel;
using System.CommandLine;
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

        var brokerTopicOption = new Option<string?>("--mqtt-topic", "MQTT topic");
        brokerTopicOption.AddAlias("-mt");

        var publishBase64Option = new Option<bool>("--base64", "Publish base64 strings rather than raw bytes");

        var rootCommand = new RootCommand("Serial-to-TCP proxy for serial KISS modems, including MQTT tracing support.");
        rootCommand.AddOption(comPortOption);
        rootCommand.AddOption(baudOption);
        rootCommand.AddOption(tcpPortOption);
        rootCommand.AddOption(anyHostOption);
        rootCommand.AddOption(brokerOption);
        rootCommand.AddOption(brokerUserOption);
        rootCommand.AddOption(brokerPasswordOption);
        rootCommand.AddOption(brokerTopicOption);
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
                    Proxy proxy = new(instance.Id, LogInformation, LogError, new SerialPortFactory());
                    tasks.Add(Task.Run(async () => await proxy.Run(instance.ComPort, instance.Baud, instance.TcpPort, instance.AnyHost, instance.MqttServer, instance.MqttUsername, instance.MqttPassword, instance.MqttTopic, instance.Base64)));
                }
                Task.WaitAll([.. tasks]);
            }
            else
            {
                var comPort = context.ParseResult.GetValueForOption(comPortOption);
                var baud = context.ParseResult.GetValueForOption(baudOption);
                var tcpPort = context.ParseResult.GetValueForOption(tcpPortOption);
                var anyHost = context.ParseResult.GetValueForOption(anyHostOption);
                var mqttServer = context.ParseResult.GetValueForOption(brokerOption);
                var mqttUser = context.ParseResult.GetValueForOption(brokerUserOption);
                var mqttPassword = context.ParseResult.GetValueForOption(brokerPasswordOption);
                var mqttTopic = context.ParseResult.GetValueForOption(brokerTopicOption);
                var base64 = context.ParseResult.GetValueForOption(publishBase64Option);

                await new Proxy("", LogInformation, LogError, new SerialPortFactory()).Run(comPort!, baud, tcpPort, anyHost, mqttServer, mqttUser, mqttPassword, mqttTopic, base64);
            }
        });

        return rootCommand.Invoke(args);
    }

    private static void LogInformation(string instance, string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {instance}{(instance == "" ? "" : "  ")}{message}");
    private static void LogError(string instance, string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {instance}{(instance == "" ? "" : "  ")}{message}");
}