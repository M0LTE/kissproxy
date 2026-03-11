using kissproxy;

namespace kissproxylib;

/// <summary>
/// Information about a NinoTNC mode including bit rate for timing calculations.
/// </summary>
public record NinoModeInfo(int Mode, string Name, int BitRateHz)
{
    /// <summary>
    /// Calculate transmission duration for a frame of the given size.
    /// </summary>
    /// <param name="frameBytes">Size of the frame in bytes (AX.25 payload, not KISS framing)</param>
    /// <returns>Transmission duration in milliseconds</returns>
    public double CalculateTransmissionMs(int frameBytes)
    {
        return (frameBytes * 8.0 / BitRateHz) * 1000.0;
    }
}

/// <summary>
/// Utilities for building and parsing KISS protocol frames.
/// </summary>
public static class KissFrameBuilder
{
    public const byte FEND = 0xC0;
    public const byte FESC = 0xDB;
    public const byte TFEND = 0xDC;
    public const byte TFESC = 0xDD;

    // KISS command codes (low nibble of command byte)
    public const byte CMD_DATAFRAME = 0x00;
    public const byte CMD_TXDELAY = 0x01;
    public const byte CMD_PERSISTENCE = 0x02;
    public const byte CMD_SLOTTIME = 0x03;
    public const byte CMD_TXTAIL = 0x04;
    public const byte CMD_FULLDUPLEX = 0x05;
    public const byte CMD_SETHW = 0x06;
    public const byte CMD_ACKMODE = 0x0C;    // Multi-drop KISS acknowledgment mode
    public const byte CMD_RETURN = 0xFF;

    /// <summary>
    /// NinoTNC mode definitions with human-readable names (by DIP switch position).
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> NinoModes = new Dictionary<int, string>
    {
        { 0, "9600 GFSK AX.25" },
        { 1, "19200 4FSK" },
        { 2, "9600 GFSK IL2P+CRC" },
        { 3, "9600 4FSK" },
        { 4, "4800 GFSK IL2P+CRC" },
        { 5, "3600 QPSK IL2P+CRC" },
        { 6, "1200 AFSK AX.25" },
        { 7, "1200 AFSK IL2P+CRC" },
        { 8, "300 BPSK IL2P+CRC" },
        { 9, "600 QPSK IL2P+CRC" },
        { 10, "1200 BPSK IL2P+CRC" },
        { 11, "2400 QPSK IL2P+CRC" },
        { 12, "300 AFSK AX.25" },
        { 13, "300 AFSKPLL IL2P" },
        { 14, "300 AFSKPLL IL2P+CRC" },
        { 15, "Set from KISS" }
    };

    /// <summary>
    /// NinoTNC internal firmware mode values (from BrdSwchMod ZZZZ bytes) to mode names.
    /// Used when DIP switches are set to 1111 (mode 15, "Set from KISS") to decode the actual operating mode.
    /// These values are firmware version specific (v3.44).
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> NinoFirmwareModes = new Dictionary<int, string>
    {
        { 0x00, "9600 GFSK AX.25" },        // Switch 0
        { 0x41, "19200 4FSK" },             // Switch 1
        { 0xB0, "9600 GFSK IL2P+CRC" },     // Switch 2
        { 0x40, "9600 4FSK" },              // Switch 3
        { 0xA3, "4800 GFSK IL2P+CRC" },     // Switch 4
        { 0xF1, "3600 QPSK IL2P+CRC" },     // Switch 5
        { 0x02, "1200 AFSK AX.25" },        // Switch 6
        { 0x93, "1200 AFSK IL2P+CRC" },     // Switch 7
        { 0x91, "300 BPSK IL2P+CRC" },      // Switch 8
        { 0x92, "600 QPSK IL2P+CRC" },      // Switch 9
        { 0xA0, "1200 BPSK IL2P+CRC" },     // Switch 10
        { 0xA2, "2400 QPSK IL2P+CRC" },     // Switch 11
        { 0x31, "300 AFSK AX.25" },         // Switch 12
        { 0x22, "300 AFSKPLL IL2P" },       // Switch 13
        { 0x23, "300 AFSKPLL IL2P+CRC" },   // Switch 14
        { 0xF3, "Set from KISS" }           // Switch 15 (shouldn't normally see this)
    };

    /// <summary>
    /// NinoTNC mode information including bit rates for timing calculations.
    /// Bit rates are the raw data rate (symbol rate × bits per symbol).
    /// </summary>
    public static readonly IReadOnlyDictionary<int, NinoModeInfo> NinoModeInfos = new Dictionary<int, NinoModeInfo>
    {
        { 0, new(0, "9600 GFSK AX.25", 9600) },
        { 1, new(1, "19200 4FSK", 19200) },
        { 2, new(2, "9600 GFSK IL2P+CRC", 9600) },
        { 3, new(3, "9600 4FSK", 9600) },
        { 4, new(4, "4800 GFSK IL2P+CRC", 4800) },
        { 5, new(5, "3600 QPSK IL2P+CRC", 3600) },
        { 6, new(6, "1200 AFSK AX.25", 1200) },
        { 7, new(7, "1200 AFSK IL2P+CRC", 1200) },
        { 8, new(8, "300 BPSK IL2P+CRC", 300) },
        { 9, new(9, "600 QPSK IL2P+CRC", 600) },
        { 10, new(10, "1200 BPSK IL2P+CRC", 1200) },
        { 11, new(11, "2400 QPSK IL2P+CRC", 2400) },
        { 12, new(12, "300 AFSK AX.25", 300) },
        { 13, new(13, "300 AFSKPLL IL2P", 300) },
        { 14, new(14, "300 AFSKPLL IL2P+CRC", 300) },
        { 15, new(15, "Set from KISS", 0) }  // Variable rate, set via KISS command
    };

    /// <summary>
    /// Gets mode info for a given mode number. Returns null if mode is unknown.
    /// </summary>
    public static NinoModeInfo? GetModeInfo(int mode)
    {
        return NinoModeInfos.TryGetValue(mode, out var info) ? info : null;
    }

    /// <summary>
    /// Builds a KISS command byte from port and command.
    /// </summary>
    public static byte BuildCommandByte(byte command, int port = 0)
    {
        return (byte)((port << 4) | (command & 0x0F));
    }

    /// <summary>
    /// Parses a KISS command byte to extract command code and port.
    /// </summary>
    public static (byte command, int port) ParseCommandByte(byte cmdByte)
    {
        return ((byte)(cmdByte & 0x0F), cmdByte >> 4);
    }

    /// <summary>
    /// Gets the human-readable name for a KISS command code.
    /// </summary>
    public static string GetCommandName(byte command)
    {
        return command switch
        {
            CMD_DATAFRAME => "DataFrame",
            CMD_TXDELAY => "TxDelay",
            CMD_PERSISTENCE => "Persistence",
            CMD_SLOTTIME => "SlotTime",
            CMD_TXTAIL => "TxTail",
            CMD_FULLDUPLEX => "FullDuplex",
            CMD_SETHW => "SetHardware",
            CMD_ACKMODE => "AckMode",
            CMD_RETURN => "Return",
            _ => $"Unknown({command})"
        };
    }

    /// <summary>
    /// Builds a complete KISS frame for a single-byte parameter command.
    /// </summary>
    public static byte[] BuildParameterFrame(byte command, byte value, int port = 0)
    {
        var cmdByte = BuildCommandByte(command, port);
        return [FEND, cmdByte, value, FEND];
    }

    /// <summary>
    /// Builds a KISS SETHW frame for NinoTNC mode setting.
    /// </summary>
    /// <param name="mode">NinoTNC mode (0-14)</param>
    /// <param name="persist">If true, persist to flash memory. If false, temporary only.</param>
    /// <param name="port">KISS port (0-15)</param>
    public static byte[] BuildSetHwFrame(int mode, bool persist, int port = 0)
    {
        // NinoTNC: add 16 to mode to prevent flash write
        byte modeValue = (byte)(mode + (persist ? 0 : 16));
        return BuildParameterFrame(CMD_SETHW, modeValue, port);
    }

    /// <summary>
    /// Checks if a command should be filtered based on config settings.
    /// </summary>
    public static bool ShouldFilter(byte command, Config config)
    {
        return command switch
        {
            CMD_TXDELAY => config.FilterTxDelay,
            CMD_PERSISTENCE => config.FilterPersistence,
            CMD_SLOTTIME => config.FilterSlotTime,
            CMD_TXTAIL => config.FilterTxTail,
            CMD_FULLDUPLEX => config.FilterFullDuplex,
            CMD_SETHW => config.FilterSetHardware,
            _ => false  // DataFrame and unknown commands pass through
        };
    }

    /// <summary>
    /// Extracts the command byte from a complete KISS frame.
    /// Returns null if frame is invalid.
    /// </summary>
    public static byte? GetCommandByteFromFrame(byte[] frame)
    {
        if (frame == null || frame.Length < 3)
            return null;

        // Frame format: [FEND] [cmd] [...payload...] [FEND]
        int startIdx = 0;
        if (frame[0] == FEND)
            startIdx = 1;

        if (startIdx >= frame.Length)
            return null;

        return frame[startIdx];
    }

    /// <summary>
    /// Extracts ACKMODE frame details: sequence bytes and payload length.
    /// Returns null if frame is not a valid ACKMODE frame.
    /// </summary>
    public static (byte seqHi, byte seqLo, int payloadBytes)? ParseAckModeFrame(byte[] frame)
    {
        if (frame == null || frame.Length < 5)
            return null;

        var cmdByte = GetCommandByteFromFrame(frame);
        if (!cmdByte.HasValue)
            return null;

        var (command, _) = ParseCommandByte(cmdByte.Value);
        if (command != CMD_ACKMODE)
            return null;

        // Frame format: [FEND] [cmd] [seqHi] [seqLo] [payload...] [FEND]
        int startIdx = frame[0] == FEND ? 1 : 0;

        // Need at least cmd + seqHi + seqLo
        if (startIdx + 3 > frame.Length)
            return null;

        byte seqHi = frame[startIdx + 1];
        byte seqLo = frame[startIdx + 2];

        // Payload is everything between seqLo and final FEND
        int payloadStart = startIdx + 3;
        int payloadEnd = frame[^1] == FEND ? frame.Length - 1 : frame.Length;
        int payloadBytes = payloadEnd - payloadStart;

        return (seqHi, seqLo, payloadBytes);
    }

    /// <summary>
    /// Extracts sequence bytes from an ACKMODE ACK frame.
    /// ACK format: [FEND] [cmd] [seqHi] [seqLo] [FEND]
    /// Returns null if frame is not a valid ACK.
    /// </summary>
    public static (byte seqHi, byte seqLo)? ParseAckModeAck(byte[] frame)
    {
        if (frame == null || frame.Length < 4)
            return null;

        var cmdByte = GetCommandByteFromFrame(frame);
        if (!cmdByte.HasValue)
            return null;

        var (command, _) = ParseCommandByte(cmdByte.Value);
        if (command != CMD_ACKMODE)
            return null;

        // ACK is exactly 5 bytes: FEND cmd seqHi seqLo FEND
        // Or 4 bytes without leading FEND: cmd seqHi seqLo FEND
        int startIdx = frame[0] == FEND ? 1 : 0;

        if (startIdx + 3 > frame.Length)
            return null;

        return (frame[startIdx + 1], frame[startIdx + 2]);
    }

    /// <summary>
    /// Builds all configured parameter frames based on config settings.
    /// Returns empty array if no parameters are configured.
    /// </summary>
    /// <param name="includeSetHardware">
    /// When false, the SETHW (NinoTNC mode) frame is omitted.
    /// Pass false for periodic timer resends when PersistNinoMode is enabled,
    /// to avoid unnecessary flash write cycles on the TNC.
    /// </param>
    public static byte[][] BuildAllParameterFrames(Config config, int port = 0, bool includeSetHardware = true)
    {
        var frames = new List<byte[]>();

        if (config.TxDelayValue.HasValue)
            frames.Add(BuildParameterFrame(CMD_TXDELAY, (byte)config.TxDelayValue.Value, port));

        if (config.PersistenceValue.HasValue)
            frames.Add(BuildParameterFrame(CMD_PERSISTENCE, (byte)config.PersistenceValue.Value, port));

        if (config.SlotTimeValue.HasValue)
            frames.Add(BuildParameterFrame(CMD_SLOTTIME, (byte)config.SlotTimeValue.Value, port));

        if (config.TxTailValue.HasValue)
            frames.Add(BuildParameterFrame(CMD_TXTAIL, (byte)config.TxTailValue.Value, port));

        if (config.FullDuplexValue.HasValue)
            frames.Add(BuildParameterFrame(CMD_FULLDUPLEX, (byte)(config.FullDuplexValue.Value ? 1 : 0), port));

        if (config.NinoMode.HasValue && includeSetHardware)
            frames.Add(BuildSetHwFrame(config.NinoMode.Value, config.PersistNinoMode, port));

        return frames.ToArray();
    }
}
