using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace kissproxy;

internal class Proxy(string instance, Action<string, string> LogInformation, Action<string, string> LogError)
{
    internal async Task Run(string comPort, int baud, int tcpPort, bool anyHost, string? mqttServer, string? mqttUsername, string? mqttPassword, string? mqttTopic, bool base64)
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

                using SerialPort serialPort = new(comPort, baud);
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
                            serialPort.Write(new[] { b }, 0, 1);
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

    private readonly List<byte> inboundBuffer = [];
    private readonly List<byte> outboundBuffer = [];

    private void Process(bool outbound, byte b)
    {
        KissHelpers.Process(outbound ? outboundBuffer : inboundBuffer, b, frame => { });
    }
}