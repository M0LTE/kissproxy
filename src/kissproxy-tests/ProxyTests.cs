using kissproxylib;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using Xunit.Abstractions;

namespace kissproxy_tests;

public class ProxyTests(ITestOutputHelper testOutputHelper)
{
    private const byte FEND = 0xc0;

    [Fact]
    public async Task Integration()
    {
        var target = new KissProxy("test", new TestLogger(testOutputHelper), new MockSerialPortFactory());
        var runner = Task.Run(async () => await target.Run("", 0, 12345, false, default, default, default, default));

        var tcpClient = new TcpClient();
        tcpClient.Connect("localhost", 12345);
        var stream = tcpClient.GetStream();
        stream.Write([FEND, FEND, 0x11, FEND, FEND, FEND, 0x12, FEND]);
        stream.Flush();

        await Task.Delay(5000);
    }
}

internal class TestLogger(ITestOutputHelper testOutputHelper) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => testOutputHelper.WriteLine(formatter(state, exception));
}

internal class MockSerialPortFactory : ISerialPortFactory
{
    public ISerialPort Create(string comPort, int baud) => new FakeSerialPort();
}

internal class FakeSerialPort : ISerialPort
{
    public void Close() { }
    public void DiscardInBuffer() { }
    public void Dispose() { }
    public void Open() { }
    public int ReadByte()
    {
        Thread.CurrentThread.Join();
        return 0;
    }

    public void Write(byte[] bytes, int v1, int v2) { }
}
