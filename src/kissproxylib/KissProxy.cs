using CliWrap;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using NAx25;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using static NAx25.KissFraming;

namespace kissproxylib;

public class KissProxy(string instance, ILogger logger, ISerialPortFactory serialPortFactory)
{
    private readonly List<byte> inboundBufferData = [];
    private readonly List<byte> outboundBufferData = [];
    private IManagedMqttClient? mqttClient;

    public KissProxy(ILogger logger) : this("", logger) { }

    public KissProxy(string instance, ILogger logger)
        : this(instance, logger, new SerialPortFactory()) { }

    private Task MqttClient_ConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        logger.LogInformation("Connected to MQTT broker");
        return Task.CompletedTask;
    }

    private IDisposable? BeginLoggingScope() => !string.IsNullOrWhiteSpace(instance) ? logger.BeginScope(new Dictionary<string, object>() { { "instance", instance } }) : null;

    private Task MqttClient_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        logger.LogWarning("Disconnected from MQTT broker: {reason}", arg?.ReasonString);
        return Task.CompletedTask;
    }

    private Task MqttClient_ConnectingFailedAsync(ConnectingFailedEventArgs arg)
    {
        logger.LogWarning("Failed to connect to MQTT broker: {reason}", arg?.Exception?.Message);
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

    private async Task SendBytesToMqttTopic(string topic, IList<byte> bytes, bool emitAsBase64String)
    {
        if (mqttClient == null)
        {
            return;
        }

        var messageBuilder = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce);

        messageBuilder = emitAsBase64String
            ? messageBuilder.WithPayload(Convert.ToBase64String(bytes.ToArray(), Base64FormattingOptions.InsertLineBreaks))
            : messageBuilder.WithPayload(bytes.ToArray());

        await mqttClient.EnqueueAsync(messageBuilder.Build());
    }

    public async Task Run(
        string modemComPort, 
        int modemSerialBaud = 57600, 
        int listenForNodeOnTcpPort = 8910,
        bool allowTcpConnectFromOtherHosts = false,
        string? mqttServer = null, 
        string? mqttUsername = null, 
        string? mqttPassword = null, 
        bool emitAsBase64String = false)
    {
        using var scope = BeginLoggingScope();
        logger.LogDebug("Starting");

        if (!string.IsNullOrWhiteSpace(mqttServer))
        {
            var (server, port) = SplitMqttServer(mqttServer);

            logger.LogInformation("Connecting to MQTT broker {server}:{port}...", server, port);

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
                using TcpListener tcpListener = new(allowTcpConnectFromOtherHosts ? IPAddress.Any : IPAddress.Loopback, listenForNodeOnTcpPort);
                try
                {
                    tcpListener.Start();
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed to start TCP listener: {reason}", ex.Message);
                    return;
                }

                logger.LogInformation("Awaiting node connection on port {port}", listenForNodeOnTcpPort);

                using var tcpClient = tcpListener.AcceptTcpClient();
                using var tcpStream = tcpClient.GetStream();
                logger.LogInformation("Accepted TCP node connection on port {port}", listenForNodeOnTcpPort);

                using ISerialPort serialPort = serialPortFactory.Create(modemComPort, modemSerialBaud);
                try
                {
                    serialPort.Open();
                }
                catch (Exception ex)
                {
                    logger.LogError("Could not open {comPort}: {reason}", modemComPort, ex.Message);
                    continue;
                }

                logger.LogInformation("Opened serial port {comPort}", modemComPort);

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
                            logger.LogError("Node disconnected (read threw), closing serial port");
                            serialPort.Close();
                            return;
                        }

                        if (read < 0)
                        {
                            logger.LogError("Node disconnected (read returned {read}), closing serial port", read);
                            serialPort.Close();
                            break;
                        }

                        var b = (byte)read;
                        ProcessByte(outboundBufferData, true, b, emitAsBase64String);

                        try
                        {
                            serialPort.Write([b], 0, 1);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError("Writing byte to modem blew up with \"{error}\", closing serial port", ex.Message);
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
                            logger.LogError("Reading byte from modem blew up with \"{error}\", disconnecting node", ex.Message);
                            tcpClient.Close();
                            return;
                        }

                        if (read < 0)
                        {
                            logger.LogError("modem read returned {read}, disconnecting node", read);
                            tcpClient.Close();
                            return;
                        }

                        var b = (byte)read;
                        ProcessByte(inboundBufferData, false, b, emitAsBase64String);

                        try
                        {
                            tcpStream.WriteByte((byte)read);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError("Writing byte to node blew up with \"{error}\", disconnecting node", ex.Message);
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
            logger.LogError(ex, "Top level exception handled");
        }
    }

    private void ProcessByte(List<byte> buffer, bool outbound, byte b, bool emitAsBase64String)
        => KissHelpers.ProcessBuffer(buffer, b, frame => Task.Run(async () => await ProcessFrame(outbound, frame, emitAsBase64String)));

    private static string LastDelimitation(string topic, char delimiter)
    {
        var parts = topic.Split(delimiter);
        return parts.Last();
    }

    private async Task ProcessFrame(bool outbound, byte[] kissFrame, bool emitAsBase64String)
    {
        string? description = null;

        var topic = $"kissproxy/{LastDelimitation(Environment.MachineName, '/')}/{instance}/{(outbound ? "to" : "from")}Modem";

        await SendBytesToMqttTopic($"{topic}/framed", kissFrame, emitAsBase64String);

        try
        {
            var (ax25Frame, portId, commandCode) = Unkiss(kissFrame);
            await SendBytesToMqttTopic($"{topic}/unframed/port{portId}/{commandCode}KissCmd", ax25Frame, emitAsBase64String);

            if (commandCode == KissCommandCode.DataFrame || commandCode == KissCommandCode.AckMode)
            {
                description = await GetDescription(ax25Frame);
                if (description != null)
                {
                    await EnqueueString($"{topic}/decoded/port{portId}/", description);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Exception: {message}", ex.Message);
            return;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            logger.LogDebug("{direction} {bytes} bytes: {description}", outbound ? "outbound" : "inbound", kissFrame.Length, description);
        }
    }

    private async Task<string?> GetDescription(byte[] frame)
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
                sw.Stop();
                string tmp = await SaveFrame(frame);
                try
                {
                    logger.LogError("ax2txt took too long: {millis}ms, and was killed, data saved {path}", sw.ElapsedMilliseconds, tmp);
                }
                catch (Exception ex)
                {
                    logger.LogError("ax2txt took too long: {millis}ms, and was killed, got exception while saving data: {error}", sw.ElapsedMilliseconds, ex.Message);
                }

                return null;
            }

            if (!execution.IsSuccess)
            {
                try
                {
                    string tmp = await SaveFrame(frame);
                    logger.LogError("ax2txt returned exit code {exitCode}, data saved {path}", execution.ExitCode, tmp);
                }
                catch (Exception ex)
                {
                    logger.LogError("ax2txt returned exit code {exitCode}, got exception while saving data: {error}", execution.ExitCode, ex.Message);
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
            logger.LogError("While running ax2txt: {error}", ex.Message);
            return null;
        }

        logger.LogError("Fell out the bottom");
        return null;
    }

    private static async Task<string> SaveFrame(byte[] frame)
    {
        var dir = "/tmp/kissproxy";
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = Path.Combine($"/tmp/kissproxy/{Guid.NewGuid()}");
        await File.WriteAllBytesAsync(tmp, frame);
        return tmp;
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