using CliWrap;
using NAx25;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using static NAx25.KissFraming;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using System;

namespace kissproxy;

public class Proxy
{
    private readonly List<byte> inboundBufferData = [];
    private readonly List<byte> outboundBufferData = [];
    private readonly string instance;
    private readonly Action<string, string> LogInformation;
    private readonly Action<string, string> LogError;
    private readonly Action<string, string> LogDebug;
    private readonly ISerialPortFactory serialPortFactory;
    private IManagedMqttClient? mqttClient;

    public Proxy(string instance, Action<string, string> logInformation, Action<string, string> logError, Action<string, string> logDebug, ISerialPortFactory serialPortFactory)
    {
        this.instance = instance;
        LogInformation = logInformation;
        LogError = logError;
        LogDebug = logDebug;
        this.serialPortFactory = serialPortFactory;
    }

    private Task MqttClient_ConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        LogInformation(instance, "Connected to MQTT broker");
        return Task.CompletedTask;
    }

    private Task MqttClient_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        LogInformation("", "Disconnected from MQTT broker: " + arg?.ReasonString);
        return Task.CompletedTask;
    }

    private Task MqttClient_ConnectingFailedAsync(ConnectingFailedEventArgs arg)
    {
        LogInformation("", "Failed to connect to MQTT broker: " + arg?.Exception?.Message);
        return Task.CompletedTask;
    }

    private async Task EnqueueString(string topic, string? payload)
    {
        if (mqttClient == null || payload == null)
        {
            return;
        }

        var messageBuilder = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .WithPayload(payload);

        await mqttClient.EnqueueAsync(messageBuilder.Build());
    }

    private async Task EnqueueBytes(string topic, IList<byte> bytes, bool convertToBase64)
    {
        if (mqttClient == null)
        {
            return;
        }

        var messageBuilder = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce);

        messageBuilder = convertToBase64
            ? messageBuilder.WithPayload(Convert.ToBase64String(bytes.ToArray(), Base64FormattingOptions.InsertLineBreaks))
            : messageBuilder.WithPayload(bytes.ToArray());

        await mqttClient.EnqueueAsync(messageBuilder.Build());
    }

    public async Task Run(string comPort, int baud, int tcpPort, bool anyHost, string? mqttServer, string? mqttUsername, string? mqttPassword, string? mqttTopic, bool base64)
    {
        LogInformation(instance, "Starting");

        if (!string.IsNullOrWhiteSpace(mqttServer))
        {
            var (server, port) = SplitMqttServer(mqttServer);

            LogInformation(instance, $"Connecting to MQTT broker {server}:{port}...");

            var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithClientOptions(new MqttClientOptionsBuilder()
                    .WithClientId(LastDelimitation(Environment.MachineName, '/') + "_kissproxy_" + instance)
                    .WithTcpServer(server, port)
                    .WithCredentials(mqttUsername, mqttPassword)
                    .Build())
                .Build();

            mqttClient = new MqttFactory().CreateManagedMqttClient();
            await mqttClient.StartAsync(options);
            mqttClient.ConnectedAsync += MqttClient_ConnectedAsync;
            mqttClient.ConnectingFailedAsync += MqttClient_ConnectingFailedAsync;
            mqttClient.DisconnectedAsync += MqttClient_DisconnectedAsync;
        }

        try
        {
            while (true)
            {
                using TcpListener tcpListener = new(anyHost ? IPAddress.Any : IPAddress.Loopback, tcpPort);
                try
                {
                    tcpListener.Start();
                }
                catch (Exception ex)
                {
                    LogError(instance, $"Failed to start TCP listener: {ex.Message}");
                    return;
                }

                LogInformation(instance, $"Awaiting node connection on port {tcpPort}");

                using var tcpClient = tcpListener.AcceptTcpClient();
                using var tcpStream = tcpClient.GetStream();
                LogInformation(instance, $"Accepted TCP node connection on port {tcpPort}");

                using ISerialPort serialPort = serialPortFactory.Create(comPort, baud);
                try
                {
                    serialPort.Open();
                }
                catch (Exception ex)
                {
                    LogError(instance, $"Could not open {comPort}: {ex.Message}");
                    continue;
                }

                LogInformation(instance, $"Opened serial port {comPort}");

                Task nodeToModem = Task.Run(() =>
                {
                    List<byte> buffer = [];
                    while (true)
                    {
                        int read;
                        try
                        {
                            read = tcpStream.ReadByte();
                        }
                        catch (Exception)
                        {
                            LogError(instance, "Node disconnected (read threw), closing serial port");
                            serialPort.Close();
                            return;
                        }

                        if (read < 0)
                        {
                            LogError(instance, $"Node disconnected (read returned {read}), closing serial port");
                            serialPort.Close();
                            break;
                        }

                        var b = (byte)read;
                        ProcessByte(outboundBufferData, true, b, base64);

                        try
                        {
                            serialPort.Write([b], 0, 1);
                        }
                        catch (Exception ex)
                        {
                            LogError(instance, $"Writing byte to modem blew up with \"{ex.Message}\", closing serial port");
                            serialPort.Close();
                            break;
                        }
                    }
                });

                Task modemToNode = Task.Run(() =>
                {
                    while (true)
                    {
                        int read;
                        try
                        {
                            read = serialPort.ReadByte();
                        }
                        catch (Exception ex)
                        {
                            LogError(instance, $"Reading byte from modem blew up with \"{ex.Message}\", disconnecting node");
                            tcpClient.Close();
                            return;
                        }

                        if (read < 0)
                        {
                            LogError(instance, $"modem read returned {read}, disconnecting node");
                            tcpClient.Close();
                            return;
                        }

                        var b = (byte)read;
                        ProcessByte(inboundBufferData, false, b, base64);

                        try
                        {
                            tcpStream.WriteByte((byte)read);
                        }
                        catch (Exception ex)
                        {
                            LogError(instance, $"Writing byte to node blew up with \"{ex.Message}\", disconnecting node");
                            tcpClient.Close();
                            return;
                        }
                    }
                });

                await Task.WhenAll(nodeToModem, modemToNode);
            }
        }
        catch (Exception ex)
        {
            LogError(instance, $"Top level exception handled: {ex}");
        }
    }

    private void ProcessByte(List<byte> buffer, bool outbound, byte b, bool convertToBase64)
        => KissHelpers.ProcessBuffer(buffer, b, frame => Task.Run(async () => await ProcessFrame(outbound, frame, convertToBase64)));

    private static string LastDelimitation(string topic, char delimiter)
    {
        var parts = topic.Split(delimiter);
        return parts.Last();
    }

    private async Task ProcessFrame(bool outbound, byte[] kissFrame, bool convertToBase64)
    {
        string? description = null;

        var topic = $"kissproxy/{LastDelimitation(Environment.MachineName, '/')}/{instance}/{(outbound ? "to" : "from")}Modem";

        await EnqueueBytes($"{topic}/framed", kissFrame, convertToBase64);

        try
        {
            var (ax25Frame, portId, commandCode) = Unkiss(kissFrame);
            await EnqueueBytes($"{topic}/unframed/port{portId}/{commandCode}KissCmd", ax25Frame, convertToBase64);

            if (commandCode == KissCommandCode.DataFrame || commandCode == KissCommandCode.AckMode)
            {
                description = await GetDescription(instance, ax25Frame);
                if (description != null)
                {
                    await EnqueueString($"{topic}/decoded/port{portId}/", description);
                }
            }
        }
        catch (Exception ex)
        {
            LogError(instance, ex.Message);
            return;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            LogDebug(instance, $"{(outbound ? "outbound" : "inbound")} {kissFrame.Length} bytes: {description}");
        }
    }

    private async Task<string?> GetDescription(string instance, byte[] frame)
    {
        const string prog = "/opt/ax2txt/ax2txt";

        if (!File.Exists(prog))
        {
            return null;
        }

        if (frame == null || frame.Length == 0)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder();

            var cmd = frame
                | Cli.Wrap(prog)
                    .WithValidation(CommandResultValidation.None)
                | PipeTarget.ToStringBuilder(buffer);

            CommandResult execution;
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                execution = await cmd.ExecuteAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                LogError(instance, $"ax2txt took too long: {sw.ElapsedMilliseconds:0}ms, and was killed");
                return null;
            }

            if (!execution.IsSuccess)
            {
                try
                {
                    var dir = "/tmp/kissproxy";
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var tmp = Path.Combine($"/tmp/kissproxy/{Guid.NewGuid()}");
                    await File.WriteAllBytesAsync(tmp, frame);
                    LogError(instance, $"ax2txt returned exit code {execution.ExitCode}, data saved {tmp}");
                }
                catch (Exception ex)
                {
                    LogError(instance, $"ax2txt returned exit code {execution.ExitCode}, got exception while saving data: {ex.Message}");
                }

                return null;
            }

            var result = buffer.ToString();

            if (!string.IsNullOrWhiteSpace(result))
            {
                return result.Trim().Replace("\r", "").Replace("\n", " ");
            }
        }
        catch (Exception ex)
        {
            LogError(instance, $"While running ax2txt: {ex.Message}");
            return null;
        }

        LogError(instance, "Fell out the bottom");
        return null;
    }

    private static (string server, int port) SplitMqttServer(string mqttServer)
    {
        var parts = mqttServer.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1], out var port))
        {
            return (parts[0], port);
        }
        return (parts[0], 1883);
    }
}