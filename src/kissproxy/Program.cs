using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using System.CommandLine;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using static kissproxy.KissHelpers;

var comPortOption = new Option<string?>("--comport", "The COM port the modem is connected to, e.g. /dev/ttyACM0")
{
    IsRequired = true,
};
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

/*rootCommand.SetHandler(
    (comport, baud, tcpPort, anyHost, mqttServer, mqttUser, mqttPassword, mqttTopic, base64) 
        => Run(comport!, baud, tcpPort, anyHost, mqttServer, mqttUser, mqttPassword, mqttTopic, base64),
    comPortOption,
    baudOption,
    tcpPortOption,
    anyHostOption,
    brokerOption, 
    brokerUserOption,
    brokerPasswordOption,
    brokerTopicOption,
    publishBase64Option);*/

rootCommand.SetHandler(async context => { 

    var comPort = context.ParseResult.GetValueForOption(comPortOption);
    var baud = context.ParseResult.GetValueForOption(baudOption);
    var tcpPort = context.ParseResult.GetValueForOption(tcpPortOption);
    var anyHost = context.ParseResult.GetValueForOption(anyHostOption);
    var mqttServer = context.ParseResult.GetValueForOption(brokerOption);
    var mqttUser = context.ParseResult.GetValueForOption(brokerUserOption);
    var mqttPassword = context.ParseResult.GetValueForOption(brokerPasswordOption);
    var mqttTopic = context.ParseResult.GetValueForOption(brokerTopicOption);
    var base64 = context.ParseResult.GetValueForOption(publishBase64Option);

    using var sp = new SerialPort(comPort!, baud);
    try
    {
        sp.Open();
    }
    catch (Exception ex)
    {
        Exit($"Could not connect to {comPort}: {ex.Message}, terminating");
        return;
    }

    LogInformation($"Connected to modem on {comPort} at {baud}");

    Action<List<byte>>? tcpSend = null, serialSend = buffer => sp.Write(buffer.ToArray(), 0, buffer.Count);
    IManagedMqttClient? mqttClient = null;

    _ = Task.Run(async () =>
    {
        try
        {
            List<byte> serialBuffer = new();
            while (true)
            {
                int read;
                try
                {
                    read = sp.ReadByte();
                }
                catch (OperationCanceledException)
                {
                    Exit("Modem has disconnected, terminating");
                    return;
                }

                if (read == -1)
                {
                    Exit("Modem returned -1, terminating");
                    return;
                }

                serialBuffer.Add((byte)read);
                await ProcessBuffer(serialBuffer, tcpSend, false, mqttClient, comPort!, mqttTopic, base64);
            }
        }
        catch (Exception ex)
        {
            Exit($"Exception: {ex.Message}, terminating");
            return;
        }
    });

    _ = Task.Run(async () =>
    {
        try
        {
            while (true)
            {
                TcpListener tcpListener = new(anyHost ? IPAddress.Any : IPAddress.Loopback, tcpPort);
                tcpListener.Start();
                LogInformation($"Awaiting node connection on port {tcpPort}");

                using var tcpClient = tcpListener.AcceptTcpClient();
                using var tcpStream = tcpClient.GetStream();
                tcpSend = buffer => tcpStream.Write(buffer.ToArray(), 0, buffer.Count);
                LogInformation("Accepted TCP node connection");

                List<byte> tcpBuffer = new();
                while (true)
                {
                    var read = tcpStream.ReadByte();
                    if (read == -1)
                    {
                        LogInformation("Node disconnected");
                        tcpSend = null;
                        break;
                    }
                    tcpBuffer.Add((byte)read);
                    await ProcessBuffer(tcpBuffer, serialSend, true, mqttClient, comPort!, mqttTopic, base64);
                }
            }
        }
        catch (Exception ex)
        {
            Exit($"Exception: {ex.Message}, terminating");
            return;
        }
    });

    if (!string.IsNullOrWhiteSpace(mqttServer))
    {
        var (server, port) = SplitMqttServer(mqttServer);

        LogInformation($"Connecting to MQTT broker {server}:{port}...");

        var options = new ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithClientOptions(new MqttClientOptionsBuilder()
                .WithClientId("kissproxy")
                .WithTcpServer(server, port)
                .WithCredentials(mqttUser, mqttPassword)
                //.WithTls() //TODO
                .Build())
            .Build();

        mqttClient = new MqttFactory().CreateManagedMqttClient();
        //await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic("my/topic").Build());
        await mqttClient.StartAsync(options);
        mqttClient.ConnectedAsync += MqttClient_ConnectedAsync;
        mqttClient.ConnectingFailedAsync += MqttClient_ConnectingFailedAsync;
        mqttClient.DisconnectedAsync += MqttClient_DisconnectedAsync;
    }

    Thread.CurrentThread.Join();
});

return rootCommand.Invoke(args);

static Task MqttClient_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
{
    LogInformation("Disconnected from MQTT broker: " + arg?.ReasonString);
    return Task.CompletedTask;
}

static Task MqttClient_ConnectingFailedAsync(ConnectingFailedEventArgs arg)
{
    LogInformation("Failed to connect to MQTT broker: " + arg?.Exception?.Message);
    return Task.CompletedTask;
}

static Task MqttClient_ConnectedAsync(MqttClientConnectedEventArgs arg)
{
    LogInformation("Connected to MQTT broker");
    return Task.CompletedTask;
}

static (string server, int port) SplitMqttServer(string mqttServer)
{
    var parts = mqttServer.Split(':');
    if (parts.Length ==2 && int.TryParse(parts[1], out var port))
    {
        return (parts[0], port);
    }
    return (parts[0], 1883);
}

static async Task ProcessBuffer(List<byte> buffer, Action<List<byte>>? send, bool toModem, IManagedMqttClient? client, string comPort, string? topic, bool convertToBase64)
{
    DiscardRepeatedFends(buffer);

    if (IsKissFrame(buffer))
    {
        await PublishKissFrame(client, buffer, toModem, comPort, topic, convertToBase64);

        if (send == null)
        {
            LogInformation("Nowhere to send a frame, discarded.");
        }
        else
        {
            send(buffer);
            //LogInformation($"{(toModem ? ">" : "<")} {buffer.Count}b");
        }

        buffer.Clear();
    }
}

static async Task PublishKissFrame(IManagedMqttClient? client, List<byte> buffer, bool toModem, string comPort, string? topic, bool convertToBase64)
{
    if (client == null)
    {
        return;
    }

    topic ??= $"kissproxy/{Sanitise(Environment.MachineName)}/{Sanitise(comPort)}/{(toModem ? "to" : "from")}Modem";

    await EnqueueBytes(client, $"{topic}/framed", buffer, convertToBase64);
    try
    {
        var (rawFrame, portId, commandCode) = Unkiss(buffer);
        await EnqueueBytes(client, $"{topic}/unframed/port{portId}/{commandCode}KissCmd", rawFrame, convertToBase64);
    }
    catch (Exception ex)
    {
        LogInformation($"Problem unframing KISS frame: {ex.Message}");
    }
}

static async Task EnqueueBytes(IManagedMqttClient client, string topic, IList<byte> bytes, bool convertToBase64)
{
    var messageBuilder = new MqttApplicationMessageBuilder()
        .WithTopic(topic)
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce);

    messageBuilder = convertToBase64
        ? messageBuilder.WithPayload(Convert.ToBase64String(bytes.ToArray(), Base64FormattingOptions.InsertLineBreaks))
        : messageBuilder.WithPayload(bytes.ToArray());

    await client.EnqueueAsync(messageBuilder.Build());
}

static string Sanitise(string comPort)
{
    var parts = comPort.Split('/');
    return parts.Last();
}

static void LogInformation(string message)
{
    Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ff}Z  {message}");
}

static void Exit(string v)
{
    LogInformation(v);
    Environment.Exit(1);
}