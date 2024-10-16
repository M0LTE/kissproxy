using System.IO.Ports;
using System.Xml.Serialization;

namespace kissproxylib;

public interface ISerialPort : IDisposable
{
    void Open();
    void Close();
    int ReadByte();
    void Write(byte[] buffer, int offset, int count);
    void DiscardInBuffer();
}

public class RealSerialPort(string port, int baud) : ISerialPort
{
    private readonly SerialPort serialPort = new(port, baud);
    public void Close() => serialPort.Close();
    public void Open() => serialPort.Open();
    public int ReadByte() => serialPort.ReadByte();
    public void Write(byte[] buffer, int offset, int count) => serialPort.Write(buffer, offset, count);
    public void Dispose() => serialPort.Dispose();
    public void DiscardInBuffer() => serialPort.DiscardInBuffer();
}