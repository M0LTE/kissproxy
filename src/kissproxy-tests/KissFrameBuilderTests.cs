using FluentAssertions;
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
            TxDelayValue = 50,
            PersistenceValue = 63,
            SlotTimeValue = 10,
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
}
