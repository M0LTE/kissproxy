using AwesomeAssertions;
using kissproxy;
using kissproxylib;

namespace kissproxy_tests;

public class KissFrameBuilderTests
{
    private const byte FEND = 0xC0;

    [Fact]
    public void BuildCommandByte_Port0Command1_ReturnsCorrectByte()
    {
        var result = KissFrameBuilder.BuildCommandByte(0x01, 0);
        result.Should().Be(0x01);
    }

    [Fact]
    public void BuildCommandByte_Port2Command3_ReturnsCorrectByte()
    {
        var result = KissFrameBuilder.BuildCommandByte(0x03, 2);
        result.Should().Be(0x23); // (2 << 4) | 3 = 32 + 3 = 35 = 0x23
    }

    [Fact]
    public void ParseCommandByte_ReturnsCorrectCommandAndPort()
    {
        var (command, port) = KissFrameBuilder.ParseCommandByte(0x23);
        command.Should().Be(0x03);
        port.Should().Be(2);
    }

    [Fact]
    public void ParseCommandByte_Port0_ReturnsCorrectValues()
    {
        var (command, port) = KissFrameBuilder.ParseCommandByte(0x01);
        command.Should().Be(0x01);
        port.Should().Be(0);
    }

    [Fact]
    public void BuildParameterFrame_TxDelay_ReturnsCorrectFrame()
    {
        var frame = KissFrameBuilder.BuildParameterFrame(KissFrameBuilder.CMD_TXDELAY, 50);
        frame.Should().Equal(FEND, 0x01, 50, FEND);
    }

    [Fact]
    public void BuildParameterFrame_WithPort_ReturnsCorrectFrame()
    {
        var frame = KissFrameBuilder.BuildParameterFrame(KissFrameBuilder.CMD_PERSISTENCE, 63, port: 1);
        frame.Should().Equal(FEND, 0x12, 63, FEND); // (1 << 4) | 2 = 0x12
    }

    [Fact]
    public void BuildSetHwFrame_PersistMode_ReturnsCorrectFrame()
    {
        var frame = KissFrameBuilder.BuildSetHwFrame(mode: 5, persist: true);
        // SETHW is command 6, mode 5 with persist = no +16
        frame.Should().Equal(FEND, 0x06, 5, FEND);
    }

    [Fact]
    public void BuildSetHwFrame_TemporaryMode_ReturnsCorrectFrame()
    {
        var frame = KissFrameBuilder.BuildSetHwFrame(mode: 5, persist: false);
        // SETHW is command 6, mode 5 + 16 (temporary) = 21
        frame.Should().Equal(FEND, 0x06, 21, FEND);
    }

    [Fact]
    public void GetCommandByteFromFrame_ValidFrame_ReturnsCommandByte()
    {
        var frame = new byte[] { FEND, 0x23, 0x45, FEND };
        var result = KissFrameBuilder.GetCommandByteFromFrame(frame);
        result.Should().Be(0x23);
    }

    [Fact]
    public void GetCommandByteFromFrame_ShortFrame_ReturnsNull()
    {
        var frame = new byte[] { FEND, 0x01 };
        var result = KissFrameBuilder.GetCommandByteFromFrame(frame);
        result.Should().BeNull();
    }

    [Fact]
    public void GetCommandName_ReturnsCorrectNames()
    {
        KissFrameBuilder.GetCommandName(0x00).Should().Be("DataFrame");
        KissFrameBuilder.GetCommandName(0x01).Should().Be("TxDelay");
        KissFrameBuilder.GetCommandName(0x02).Should().Be("Persistence");
        KissFrameBuilder.GetCommandName(0x03).Should().Be("SlotTime");
        KissFrameBuilder.GetCommandName(0x04).Should().Be("TxTail");
        KissFrameBuilder.GetCommandName(0x05).Should().Be("FullDuplex");
        KissFrameBuilder.GetCommandName(0x06).Should().Be("SetHardware");
        KissFrameBuilder.GetCommandName(0x0C).Should().Be("AckMode");
        KissFrameBuilder.GetCommandName(0xFF).Should().Be("Return");
        KissFrameBuilder.GetCommandName(0x99).Should().Be("Unknown(153)");
    }

    [Fact]
    public void CMD_ACKMODE_HasCorrectValue()
    {
        KissFrameBuilder.CMD_ACKMODE.Should().Be(0x0C);
    }

    [Fact]
    public void ShouldFilter_DataFrame_ReturnsFalse()
    {
        var config = new Config { Id = "test", ComPort = "/dev/ttyACM0", FilterTxDelay = true };
        var result = KissFrameBuilder.ShouldFilter(KissFrameBuilder.CMD_DATAFRAME, config);
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldFilter_FilteredCommand_ReturnsTrue()
    {
        var config = new Config { Id = "test", ComPort = "/dev/ttyACM0", FilterTxDelay = true };
        var result = KissFrameBuilder.ShouldFilter(KissFrameBuilder.CMD_TXDELAY, config);
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldFilter_UnfilteredCommand_ReturnsFalse()
    {
        var config = new Config { Id = "test", ComPort = "/dev/ttyACM0", FilterTxDelay = false };
        var result = KissFrameBuilder.ShouldFilter(KissFrameBuilder.CMD_TXDELAY, config);
        result.Should().BeFalse();
    }

    [Fact]
    public void BuildAllParameterFrames_WithAllValues_ReturnsAllFrames()
    {
        var config = new Config
        {
            Id = "test",
            ComPort = "/dev/ttyACM0",
            TxDelayValue = 500,
            PersistenceValue = 63,
            SlotTimeValue = 100,
            TxTailValue = 0,
            FullDuplexValue = false,
            NinoMode = 5,
            PersistNinoMode = false
        };

        var frames = KissFrameBuilder.BuildAllParameterFrames(config);
        frames.Should().HaveCount(6);
    }

    [Fact]
    public void BuildAllParameterFrames_WithNoValues_ReturnsEmptyArray()
    {
        var config = new Config { Id = "test", ComPort = "/dev/ttyACM0" };
        var frames = KissFrameBuilder.BuildAllParameterFrames(config);
        frames.Should().BeEmpty();
    }

    [Fact]
    public void NinoModes_ContainsExpectedModes()
    {
        KissFrameBuilder.NinoModes.Should().ContainKey(0);
        KissFrameBuilder.NinoModes.Should().ContainKey(5);
        KissFrameBuilder.NinoModes.Should().ContainKey(10);
        KissFrameBuilder.NinoModes.Should().ContainKey(14);

        KissFrameBuilder.NinoModes[0].Should().Contain("9600");
        KissFrameBuilder.NinoModes[10].Should().Contain("1200");
    }

    // --- AX.25 UI frame and KISS data frame tests ---

    [Fact]
    public void EncodeCallsign_SimpleCall_ReturnsCorrectBytes()
    {
        var result = KissFrameBuilder.EncodeCallsign("CQ", false);
        result.Should().HaveCount(7);
        // 'C' = 0x43, shifted left = 0x86
        result[0].Should().Be(0x86);
        // 'Q' = 0x51, shifted left = 0xA2
        result[1].Should().Be(0xA2);
        // Remaining positions are space (0x20), shifted left = 0x40
        result[2].Should().Be(0x40);
        result[3].Should().Be(0x40);
        result[4].Should().Be(0x40);
        result[5].Should().Be(0x40);
        // SSID byte: 0x60 | (0 << 1) | 0 = 0x60
        result[6].Should().Be(0x60);
    }

    [Fact]
    public void EncodeCallsign_WithSsid_ReturnsCorrectSsidByte()
    {
        var result = KissFrameBuilder.EncodeCallsign("VK2ABC-5", false);
        result.Should().HaveCount(7);
        // SSID byte: 0x60 | (5 << 1) | 0 = 0x60 | 0x0A = 0x6A
        result[6].Should().Be(0x6A);
    }

    [Fact]
    public void EncodeCallsign_LastAddress_SetsEndBit()
    {
        var result = KissFrameBuilder.EncodeCallsign("CQ", true);
        // SSID byte with end bit: 0x60 | (0 << 1) | 1 = 0x61
        result[6].Should().Be(0x61);
    }

    [Fact]
    public void EncodeCallsign_LastAddressWithSsid_SetsEndBit()
    {
        var result = KissFrameBuilder.EncodeCallsign("TEST-15", true);
        // SSID byte: 0x60 | (15 << 1) | 1 = 0x60 | 0x1E | 0x01 = 0x7F
        result[6].Should().Be(0x7F);
    }

    [Fact]
    public void EncodeCallsign_EmptyCallsign_Throws()
    {
        var action = () => KissFrameBuilder.EncodeCallsign("", false);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EncodeCallsign_TooLongCallsign_Throws()
    {
        var action = () => KissFrameBuilder.EncodeCallsign("TOOLONG1", false);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EncodeCallsign_InvalidSsid_Throws()
    {
        var action = () => KissFrameBuilder.EncodeCallsign("TEST-16", false);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildAx25UiFrame_ProducesCorrectFrame()
    {
        var info = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var frame = KissFrameBuilder.BuildAx25UiFrame("SRC", "DST", info);

        // Dest (7) + Src (7) + Control (1) + PID (1) + Info (5) = 21 bytes
        frame.Should().HaveCount(21);

        // Control field at offset 14 should be 0x03 (UI)
        frame[14].Should().Be(0x03);

        // PID at offset 15 should be 0xF0 (No layer 3)
        frame[15].Should().Be(0xF0);

        // Info field
        frame[16].Should().Be(0x48); // 'H'
        frame[17].Should().Be(0x65); // 'e'

        // Source address (last) should have end-of-address bit set
        (frame[13] & 0x01).Should().Be(1);
        // Dest address (not last) should not have end-of-address bit
        (frame[6] & 0x01).Should().Be(0);
    }

    [Fact]
    public void BuildKissDataFrame_WrapsCorrectly()
    {
        var ax25Data = new byte[] { 0x01, 0x02, 0x03 };
        var frame = KissFrameBuilder.BuildKissDataFrame(ax25Data);

        // FEND + cmd + data + FEND = 6 bytes
        frame.Should().HaveCount(6);
        frame[0].Should().Be(FEND);
        frame[1].Should().Be(0x00); // CMD_DATAFRAME on port 0
        frame[2].Should().Be(0x01);
        frame[3].Should().Be(0x02);
        frame[4].Should().Be(0x03);
        frame[5].Should().Be(FEND);
    }

    [Fact]
    public void BuildKissDataFrame_WithPort_SetsCorrectCommandByte()
    {
        var ax25Data = new byte[] { 0x01 };
        var frame = KissFrameBuilder.BuildKissDataFrame(ax25Data, port: 3);

        frame[1].Should().Be(0x30); // (3 << 4) | 0x00
    }

    [Fact]
    public void BuildKissDataFrame_EscapesFendInPayload()
    {
        var ax25Data = new byte[] { 0x01, FEND, 0x03 };
        var frame = KissFrameBuilder.BuildKissDataFrame(ax25Data);

        // FEND + cmd + 0x01 + FESC + TFEND + 0x03 + FEND = 7 bytes
        frame.Should().HaveCount(7);
        frame[0].Should().Be(FEND);
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x01);
        frame[3].Should().Be(KissFrameBuilder.FESC);
        frame[4].Should().Be(KissFrameBuilder.TFEND);
        frame[5].Should().Be(0x03);
        frame[6].Should().Be(FEND);
    }

    [Fact]
    public void BuildKissDataFrame_EscapesFescInPayload()
    {
        var ax25Data = new byte[] { KissFrameBuilder.FESC };
        var frame = KissFrameBuilder.BuildKissDataFrame(ax25Data);

        // FEND + cmd + FESC + TFESC + FEND = 5 bytes
        frame.Should().HaveCount(5);
        frame[2].Should().Be(KissFrameBuilder.FESC);
        frame[3].Should().Be(KissFrameBuilder.TFESC);
    }
}
