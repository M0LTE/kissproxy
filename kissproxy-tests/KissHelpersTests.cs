using FluentAssertions;
using kissproxy;

namespace kissproxy_tests;

public class KissHelpersTests
{
    private const byte FEND = 0xc0;
    private List<byte>? frame = null;

    private void ProcessFrame(List<byte> bytes)
    {
        frame = bytes.ToList();
    }

    [Fact]
    public void DiscardsRepeatedFENDs()
    {
        List<byte> bytes = [FEND];
        KissHelpers.Process(bytes, FEND, ProcessFrame);
        bytes.Should().Equal(FEND);
        frame.Should().BeNull();
    }

    [Fact]
    public void ProcessesFrames()
    {
        List<byte> buffer = [FEND, 0x11];
        KissHelpers.Process(buffer, FEND, ProcessFrame);
        buffer.Should().BeEmpty();
        frame.Should().Equal(FEND, 0x11, FEND);
    }
}