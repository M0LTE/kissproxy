using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace kissproxy;

internal class Proxy(string instance, Action<string, string> LogInformation, Action<string, string> LogError)
{
    internal async Task Run(string comPort, int baud, int tcpPort, bool anyHost, string? mqttServer, string? mqttUsername, string? mqttPassword, string? mqttTopic, bool base64)
    {
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

                Task nodeToModem = Task.Run(() =>
                {
                    while (true)
                    {
                        int read;
                        try
                        {
                            read = tcpStream.ReadByte();
                        }
                        catch (Exception)
                        {
                            LogInformation(instance, "Node disconnected (read threw), task ending");
                            return;
                        }

                        if (read == -1)
                        {
                            LogInformation(instance, "Node disconnected (read returned -1), task ending");
                            serialPort.Close();
                            break;
                        }

                        serialPort.Write(new[] { (byte)read }, 0, 1);
                    }
                });

                Task modemToNode = Task.Run(() =>
                {
                    while (true)
                    {
                        int i;
                        try
                        {
                            i = serialPort.ReadByte();
                        }
                        catch (Exception ex)
                        {
                            LogError(instance, $"Reading byte from modem blew up with {ex.Message}, task ending");
                            return;
                        }

                        if (i < 0)
                        {
                            LogError(instance, $"modem read returned {i}, task ending");
                            return;
                        }

                        try
                        {
                            tcpStream.WriteByte((byte)i);
                        }
                        catch (Exception ex)
                        {
                            LogError(instance, $"Writing byte to node blew up with {ex.Message}, task ending");
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
}