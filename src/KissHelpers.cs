namespace kissproxy;

public static class KissHelpers
{
    public const byte FEND = 0xc0;

    public delegate void ProcessFrameDelegate(List<byte> bytes);

    public static void Process(List<byte> buffer, byte b, ProcessFrameDelegate processFrame)
    {
        if (b == FEND && buffer.Count == 1 && buffer[0] == FEND)
        {
            // discard repeated FENDs
            return;
        }

        // keep anything else
        buffer.Add(b);

        if (b == FEND && buffer.Count > 2 && buffer[^1] == FEND)
        {
            // it's a frame
            processFrame(buffer);
            buffer.Clear();
        }
    }
}