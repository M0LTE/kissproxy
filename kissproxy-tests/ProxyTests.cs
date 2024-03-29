﻿using kissproxy;
using System.Net.Sockets;
using Xunit.Abstractions;

namespace kissproxy_tests;

public class ProxyTests(ITestOutputHelper testOutputHelper)
{
    private const byte FEND = 0xc0;

    [Fact]
    public async void Integration()
    {
        var target = new Proxy("test", Log, Log, Log, new MockSerialPortFactory());
        var runner = Task.Run(async () => await target.Run("", 0, 12345, false, default, default, default, default, default));

        var tcpClient = new TcpClient();
        tcpClient.Connect("localhost", 12345);
        var stream = tcpClient.GetStream();
        stream.Write(new byte[] { FEND, FEND, 0x11, FEND, FEND, FEND, 0x12, FEND });
        stream.Flush();

        await Task.Delay(5000);
    }

    private void Log(string instance, string message) => testOutputHelper.WriteLine(message);
}

internal class MockSerialPortFactory : ISerialPortFactory
{
    public ISerialPort Create(string comPort, int baud)
    {
        return new FakeSerialPort();
    }
}

internal class FakeSerialPort : ISerialPort
{
    public void Close()
    {
    }

    public void Dispose()
    {
    }

    public void Open()
    {
    }

    public int ReadByte()
    {
        Thread.CurrentThread.Join();
        return 0;
    }

    public void Write(byte[] bytes, int v1, int v2)
    {
    }
}
