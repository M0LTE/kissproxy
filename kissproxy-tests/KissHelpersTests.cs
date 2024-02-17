using FluentAssertions;
using kissproxy;

namespace kissproxy_tests;

public class KissHelpersTests
{
    private const byte FEND = 0xc0;
    private byte[]? frame = null;

    [Fact]
    public void DiscardsRepeatedFENDs()
    {
        List<byte> bytes = [FEND];
        KissHelpers.ProcessBuffer(bytes, FEND, f => frame = f);
        bytes.Should().Equal(FEND);
        frame.Should().BeNull();
    }

    [Fact]
    public void ProcessesFrames()
    {
        List<byte> buffer = [FEND, 0x11];
        KissHelpers.ProcessBuffer(buffer, FEND, f => frame = f);
        buffer.Should().BeEmpty();
        frame.Should().Equal(FEND, 0x11, FEND);
    }
}