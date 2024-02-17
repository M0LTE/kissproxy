namespace kissproxy;

public interface ISerialPortFactory
{
    ISerialPort Create(string comPort, int baud);
}

public class SerialPortFactory : ISerialPortFactory
{
    public ISerialPort Create(string comPort, int baud)
    {
        return new RealSerialPort(comPort, baud);
    }
}