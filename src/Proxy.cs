using System.Diagnostics;
//using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks.Dataflow;

namespace kissproxy;

public class Proxy
{
    public Proxy(string instance, Action<string, string> logInformation, Action<string, string> logError, ISerialPortFactory serialPortFactory)
    {
        inboundProcess = new(b => ProcessByte(false, b), new ExecutionDataflowBlockOptions { EnsureOrdered = true, MaxDegreeOfParallelism = 1 });
        outboundProcess = new(b => ProcessByte(true, b), new ExecutionDataflowBlockOptions { EnsureOrdered = true, MaxDegreeOfParallelism = 1 });

        this.instance = instance;
        LogInformation = logInformation;
        LogError = logError;
        this.serialPortFactory = serialPortFactory;
    }

    public async Task Run(string comPort, int baud, int tcpPort, bool anyHost, string? mqttServer, string? mqttUsername, string? mqttPassword, string? mqttTopic, bool base64)
    {
        LogInformation(instance, "Starting");

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
                        Process(true, b);

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
                        Process(false, b);

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

    private readonly List<byte> inboundBufferData = [];
    private readonly List<byte> outboundBufferData = [];

    private void Process(bool outbound, byte b)
    {
        var action = outbound ? outboundProcess : inboundProcess;
        Debug.Assert(action.Post(b));
    }

    private readonly ActionBlock<byte> inboundProcess;
    private readonly ActionBlock<byte> outboundProcess;

    private readonly string instance;
    private readonly Action<string, string> LogInformation;
    private readonly Action<string, string> LogError;
    private readonly ISerialPortFactory serialPortFactory;

    private void ProcessByte(bool outbound, byte b)
    {
        LogInformation(instance, $"{nameof(ProcessByte)}({(outbound ? "outbound" : "inbound")}, {b})");
        var buffer = outbound ? outboundBufferData : inboundBufferData;
        buffer.Add(b);
        KissHelpers.ProcessBuffer(buffer, b, frame => ProcessFrame(outbound, frame));
    }

    private void ProcessFrame(bool outbound, byte[] frame)
    {
        LogInformation(instance, $"Frame received: {(outbound ? "outbound" : "inbound")}, {frame.Length} bytes");
    }
}