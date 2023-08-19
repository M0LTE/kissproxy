namespace kissproxy;

internal static class KissHelpers
{
    private const byte FEND = 0xc0;

    public static bool IsKissFrame(List<byte> buffer) => buffer.Count > 2 && buffer[0] == FEND && buffer[^1] == FEND;

    public static void DiscardRepeatedFends(List<byte> buffer)
    {
        if (buffer.Count > 1 && buffer[^1] == FEND && buffer[^2] == FEND)
        {
            buffer.RemoveAt(buffer.Count - 1);
        }
    }
}

public enum KissCommandCode : byte
{
    DataFrame = 0x00,
    TxDelay = 0x01,
    Persistence = 0x02,
    SlotTime = 0x03,
    TxTail = 0x04,
    FullDuplex = 0x05,
    SetHardware = 0x06,
    AckMode = 0x0c,
    ExitKissMode = 0xff
}