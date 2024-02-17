using System;

namespace kissproxy;

public static class KissHelpers
{
    public const byte FEND = 0xc0;

    public delegate void ProcessFrameDelegate(byte[] bytes);

    public static void ProcessBuffer(List<byte> buffer, byte b, ProcessFrameDelegate processFrame)
    {
        if (b == FEND && buffer.Count > 0 && buffer.Last() == FEND)
        {
            // discard repeated FENDs
            return;
        }

        // keep anything else
        buffer.Add(b);

        // it's a frame
        if (b == FEND && buffer.Count > 2 && buffer[^1] == FEND)
        {
            if (buffer[0] == FEND)
            {
                processFrame(buffer.ToArray());
            }
            else
            {
                byte[] buffer2 = new byte[buffer.Count + 1];
                buffer2[0] = FEND;
                buffer.CopyTo(buffer2, 1);
                processFrame(buffer2);
            }

            buffer.Clear();
        }
    }
}
