using System.Collections;

namespace kissproxy;

public static class ExtensionMethods
{
    /// <summary>
    /// Convert e.g. 00000101 to 5 (MSB first)
    /// </summary>
    public static byte ConvertToByte(this bool[] bits)
    {
        int paddingBitsRequired = 8 - bits.Length;

        var bools = new List<bool>();

        for (int i = 0; i < paddingBitsRequired; i++)
        {
            bools.Add(false);
        }

        foreach (var bit in bits)
        {
            bools.Add(bit);
        }

        bools.Reverse();

        var ba = new BitArray(bools.ToArray());
        return ba.ConvertToByte();
    }

    /// <summary>
    /// Convert e.g. 10100000 to 5 (LSB first)
    /// </summary>
    public static byte ConvertToByte(this BitArray bits)
    {
        if (bits.Count != 8)
        {
            throw new ArgumentException("bits");
        }
        byte[] bytes = new byte[1];
        bits.CopyTo(bytes, 0);
        return bytes[0];
    }

    public static string ToHexByte(this byte b)
    {
        var digit = b.ToString("X");
        if (digit.Length == 1)
        {
            digit = "0" + digit;
        }
        return digit.ToLower();
    }
}
