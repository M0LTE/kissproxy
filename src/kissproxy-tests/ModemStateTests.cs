using FluentAssertions;
using kissproxylib;

namespace kissproxy_tests;

public class ModemStateTests
{
    [Fact]
    public void RecordFrameToModem_IncrementsCounters()
    {
        var state = new ModemState { Id = "test" };
        var frame = new byte[] { 0xC0, 0x00, 0x01, 0x02, 0xC0 };

        state.RecordFrameToModem(frame);

        state.FramesToModem.Should().Be(1);
        state.BytesToModem.Should().Be(frame.Length);
        state.LastFrameToModem.Should().NotBeNull();
    }

    [Fact]
    public void RecordFrameFromModem_IncrementsCounters()
    {
        var state = new ModemState { Id = "test" };
        var frame = new byte[] { 0xC0, 0x00, 0x01, 0x02, 0xC0 };

        state.RecordFrameFromModem(frame);

        state.FramesFromModem.Should().Be(1);
        state.BytesFromModem.Should().Be(frame.Length);
        state.LastFrameFromModem.Should().NotBeNull();
    }

    [Fact]
    public void RecordFrameToModem_WithDataFrameInfo_IncrementsDataFrameCounter()
    {
        var state = new ModemState { Id = "test" };
        var frame = new byte[] { 0xC0, 0x00, 0x01, 0x02, 0xC0 };
        var info = new FrameInfo { CommandCode = KissFrameBuilder.CMD_DATAFRAME };

        state.RecordFrameToModem(frame, info);

        state.DataFramesToModem.Should().Be(1);
        state.LastDataFrameToModem.Should().NotBeNull();
        state.LastFrameToModemInfo.Should().Be(info);
    }

    [Fact]
    public void RecordFrameToModem_WithParameterInfo_UpdatesCurrentParameter()
    {
        var state = new ModemState { Id = "test" };
        var frame = new byte[] { 0xC0, 0x01, 50, 0xC0 };
        var info = new FrameInfo
        {
            CommandCode = KissFrameBuilder.CMD_TXDELAY,
            ParameterValue = 50
        };

        state.RecordFrameToModem(frame, info);

        state.CurrentTxDelay.Should().Be(50);
    }

    [Fact]
    public void RecordFilteredFrame_IncrementsFilteredCounter()
    {
        var state = new ModemState { Id = "test" };

        state.RecordFilteredFrame();
        state.RecordFilteredFrame();
        state.RecordFilteredFrame();

        state.FramesFiltered.Should().Be(3);
    }

    [Fact]
    public void ResetStats_ClearsAllCounters()
    {
        var state = new ModemState { Id = "test" };
        var frame = new byte[] { 0xC0, 0x00, 0xC0 };

        state.RecordFrameToModem(frame);
        state.RecordFrameFromModem(frame);
        state.RecordFilteredFrame();
        state.ResetStats();

        state.FramesToModem.Should().Be(0);
        state.FramesFromModem.Should().Be(0);
        state.FramesFiltered.Should().Be(0);
        state.BytesToModem.Should().Be(0);
        state.BytesFromModem.Should().Be(0);
        state.LastFrameToModem.Should().BeNull();
        state.LastFrameFromModem.Should().BeNull();
    }

    [Fact]
    public void Snapshot_CreatesIndependentCopy()
    {
        var state = new ModemState { Id = "test", NodeConnected = true };
        var frame = new byte[] { 0xC0, 0x00, 0xC0 };
        state.RecordFrameToModem(frame);

        var snapshot = state.Snapshot();

        // Modify original
        state.RecordFrameToModem(frame);
        state.NodeConnected = false;

        // Snapshot should be unchanged
        snapshot.FramesToModem.Should().Be(1);
        snapshot.NodeConnected.Should().BeTrue();
    }

    [Fact]
    public void ModemStateManager_GetOrCreate_CreatesNewState()
    {
        var manager = new ModemStateManager();

        var state = manager.GetOrCreate("test");

        state.Should().NotBeNull();
        state.Id.Should().Be("test");
    }

    [Fact]
    public void ModemStateManager_GetOrCreate_ReturnsSameInstance()
    {
        var manager = new ModemStateManager();

        var state1 = manager.GetOrCreate("test");
        var state2 = manager.GetOrCreate("test");

        state1.Should().BeSameAs(state2);
    }

    [Fact]
    public void ModemStateManager_GetSnapshot_ReturnsSnapshot()
    {
        var manager = new ModemStateManager();
        var state = manager.GetOrCreate("test");
        state.NodeConnected = true;

        var snapshot = manager.GetSnapshot("test");

        snapshot.Should().NotBeNull();
        snapshot!.NodeConnected.Should().BeTrue();
        snapshot.Should().NotBeSameAs(state);
    }

    [Fact]
    public void ModemStateManager_GetSnapshot_NonExistent_ReturnsNull()
    {
        var manager = new ModemStateManager();

        var snapshot = manager.GetSnapshot("nonexistent");

        snapshot.Should().BeNull();
    }

    [Fact]
    public void ModemStateManager_Remove_RemovesState()
    {
        var manager = new ModemStateManager();
        manager.GetOrCreate("test");

        var result = manager.Remove("test");

        result.Should().BeTrue();
        manager.GetSnapshot("test").Should().BeNull();
    }

    [Fact]
    public void ModemStateManager_GetAllSnapshots_ReturnsAllStates()
    {
        var manager = new ModemStateManager();
        manager.GetOrCreate("test1");
        manager.GetOrCreate("test2");

        var snapshots = manager.GetAllSnapshots().ToList();

        snapshots.Should().HaveCount(2);
        snapshots.Select(s => s.Id).Should().Contain("test1");
        snapshots.Select(s => s.Id).Should().Contain("test2");
    }

    // Helper to convert hex string to byte array
    private static byte[] HexToBytes(string hex)
    {
        hex = hex.Replace(" ", "");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    [Theory]
    [InlineData("0000", 0, "0000")]
    [InlineData("0001", 1, "0001")]
    [InlineData("0010", 2, "0010")]
    [InlineData("0011", 3, "0011")]
    [InlineData("0100", 4, "0100")]
    [InlineData("0101", 5, "0101")]
    [InlineData("0110", 6, "0110")]
    [InlineData("0111", 7, "0111")]
    [InlineData("1000", 8, "1000")]
    [InlineData("1001", 9, "1001")]
    [InlineData("1010", 10, "1010")]
    [InlineData("1011", 11, "1011")]
    [InlineData("1100", 12, "1100")]
    [InlineData("1110", 14, "1110")]
    [InlineData("1111", 0, "1111")]  // Switch 1111 = "Set from KISS", actual mode decoded from firmware value (0x0000 = mode 0)
    public void NinoTncStatus_TryParse_AllDipSwitchPositions_ParsesCorrectly(string dipSwitch, int expectedMode, string expectedBinary)
    {
        // All frames from the sample file - indexed by DIP switch position
        var frames = new Dictionary<string, string>
        {
            ["0000"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 31 43 31 31 44 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 30 30 30 30 30 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 42 34 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 33 32 41 36 43 46 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["0001"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 36 36 37 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 31 30 30 34 31 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 31 36 38 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 38 42 44 30 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["0010"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 31 39 36 38 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 32 30 30 42 30 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 42 34 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 31 36 30 33 37 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["0011"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 37 31 33 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 33 30 30 34 30 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 42 34 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 43 34 31 42 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["0100"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 41 44 46 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 34 30 30 41 33 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 35 41 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 37 38 33 42 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["0101"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 32 32 41 39 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 35 30 30 46 31 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 34 33 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 33 32 38 38 39 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["0110"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 44 36 42 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 36 30 30 30 32 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 31 36 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 37 33 33 37 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["0111"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 38 41 45 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 37 30 30 39 33 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 31 36 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 34 41 31 35 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["1000"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 31 43 33 31 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 38 30 30 39 31 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 30 35 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 33 31 34 31 39 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["1001"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 45 41 30 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 39 30 30 39 32 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 30 42 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 32 31 34 34 39 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["1010"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 37 30 33 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 41 30 30 41 30 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 31 36 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 39 34 41 32 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["1011"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 38 38 37 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 42 30 30 41 32 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 32 44 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 43 31 36 39 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["1100"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 37 31 43 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 43 30 30 33 31 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 30 35 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 30 41 44 37 31 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["1110"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 30 46 30 31 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 45 30 30 32 33 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 30 35 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 32 39 35 30 33 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
            ["1111"] = "C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 31 41 34 38 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 46 30 30 30 30 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 42 34 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 33 32 31 37 30 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0",
        };

        var frame = HexToBytes(frames[dipSwitch]);
        var status = NinoTncStatus.TryParse(frame);

        status.Should().NotBeNull();
        status!.FirmwareVersion.Should().Be("3.44");
        status.CurrentMode.Should().Be(expectedMode);
        status.BoardSwitchModeBinary.Should().Be(expectedBinary);
    }

    [Fact]
    public void NinoTncStatus_TryParse_ValidTxTestFrame_ReturnsAllFields()
    {
        // TX Test frame for mode 0 (switches 0000) - verify all fields parse correctly
        var frame = HexToBytes("C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 31 43 31 31 44 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 30 30 30 30 30 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 42 34 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 33 32 41 36 43 46 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0");

        var status = NinoTncStatus.TryParse(frame);

        status.Should().NotBeNull();
        status!.FirmwareVersion.Should().Be("3.44");
        status.CurrentMode.Should().Be(0);
        status.CurrentModeName.Should().Be("9600 GFSK AX.25");
        status.BoardSwitchModeBinary.Should().Be("0000");
        status.Ax25RxPackets.Should().Be(0);
        status.Il2pRxPackets.Should().Be(0);
        status.Il2pRxUncorrectable.Should().Be(0);
        status.TxPacketCount.Should().Be(0);
        status.PreambleCount.Should().Be(0xB4); // 180
        status.LoopCycles.Should().Be(0x32A6CF);
        status.LostAdcSamples.Should().Be(0);
        status.UptimeMs.Should().Be(0x1C11D); // 114973 ms
    }

    [Fact]
    public void NinoTncStatus_TryParse_NonTxTestFrame_ReturnsNull()
    {
        // Regular KISS data frame without TX Test content
        var frame = new byte[] { 0xC0, 0x00, 0x01, 0x02, 0x03, 0xC0 };

        var status = NinoTncStatus.TryParse(frame);

        status.Should().BeNull();
    }

    [Fact]
    public void NinoTncStatus_TryParse_TooShortFrame_ReturnsNull()
    {
        var frame = new byte[] { 0xC0, 0x00, 0xC0 };

        var status = NinoTncStatus.TryParse(frame);

        status.Should().BeNull();
    }

    [Fact]
    public void RecordFrameFromModem_WithTxTestFrame_UpdatesNinoTncStatus()
    {
        var state = new ModemState { Id = "test" };

        // TX Test frame for mode 14
        var frame = new byte[]
        {
            0xC0, 0x00, 0x86, 0xA2, 0x84, 0x8A, 0x8A, 0xA0, 0xEA, 0x9C, 0x72, 0x6C, 0x60, 0x60, 0x82, 0x69,
            0x03, 0xF0, 0x3D, 0x46, 0x69, 0x72, 0x6D, 0x77, 0x61, 0x72, 0x65, 0x56, 0x72, 0x3A, 0x33, 0x2E,
            0x34, 0x34, 0x3D, 0x53, 0x65, 0x72, 0x69, 0x61, 0x6C, 0x4E, 0x6D, 0x62, 0x72, 0x3A, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3D, 0x55, 0x70, 0x74, 0x69, 0x6D, 0x65, 0x4D, 0x69, 0x6C,
            0x53, 0x3A, 0x30, 0x30, 0x30, 0x32, 0x30, 0x38, 0x45, 0x43, 0x3D, 0x42, 0x72, 0x64, 0x53, 0x77,
            0x63, 0x68, 0x4D, 0x6F, 0x64, 0x3A, 0x30, 0x34, 0x30, 0x45, 0x30, 0x30, 0x32, 0x33, 0x3D, 0x41,
            0x58, 0x32, 0x35, 0x52, 0x78, 0x50, 0x6B, 0x74, 0x73, 0x3A, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
            0x30, 0x30, 0x3D, 0x49, 0x4C, 0x32, 0x50, 0x52, 0x78, 0x50, 0x6B, 0x74, 0x73, 0x3A, 0x30, 0x30,
            0x30, 0x30, 0x30, 0x30, 0x30, 0x36, 0x3D, 0x49, 0x4C, 0x32, 0x50, 0x52, 0x78, 0x55, 0x6E, 0x43,
            0x72, 0x3A, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x3D, 0x54, 0x78, 0x50, 0x6B, 0x74,
            0x43, 0x6F, 0x75, 0x6E, 0x74, 0x3A, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x3D, 0x50,
            0x72, 0x65, 0x61, 0x6D, 0x62, 0x6C, 0x43, 0x6E, 0x74, 0x3A, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
            0x30, 0x35, 0x3D, 0x4C, 0x6F, 0x6F, 0x70, 0x43, 0x79, 0x63, 0x6C, 0x65, 0x73, 0x3A, 0x30, 0x30,
            0x36, 0x38, 0x35, 0x30, 0x45, 0x46, 0x3D, 0x4C, 0x6F, 0x73, 0x74, 0x41, 0x44, 0x43, 0x53, 0x6D,
            0x70, 0x3A, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0xC0
        };

        var info = new FrameInfo { CommandCode = KissFrameBuilder.CMD_DATAFRAME };
        state.RecordFrameFromModem(frame, info);

        state.NinoTncStatus.Should().NotBeNull();
        state.NinoTncStatus!.FirmwareVersion.Should().Be("3.44");
        state.NinoTncStatus.CurrentMode.Should().Be(14);
    }

    [Fact]
    public void NinoTncStatus_TryParse_SetFromKiss_DecodesActualMode()
    {
        // BrdSwchMod:040F0023 = board rev 04, switches 1111 (Set from KISS), operating mode 0x23 (300 AFSKPLL IL2P+CRC)
        // This simulates a TNC with DIP switches set to 1111 and mode set via KISS SETHW command
        var frame = HexToBytes("C0 00 86 A2 84 8A 8A A0 EA 9C 72 6C 60 60 82 69 03 F0 3D 46 69 72 6D 77 61 72 65 56 72 3A 33 2E 34 34 3D 53 65 72 69 61 6C 4E 6D 62 72 3A 00 00 00 00 00 00 00 00 3D 55 70 74 69 6D 65 4D 69 6C 53 3A 30 30 30 30 31 41 34 38 3D 42 72 64 53 77 63 68 4D 6F 64 3A 30 34 30 46 30 30 32 33 3D 41 58 32 35 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 50 6B 74 73 3A 30 30 30 30 30 30 30 30 3D 49 4C 32 50 52 78 55 6E 43 72 3A 30 30 30 30 30 30 30 30 3D 54 78 50 6B 74 43 6F 75 6E 74 3A 30 30 30 30 30 30 30 30 3D 50 72 65 61 6D 62 6C 43 6E 74 3A 30 30 30 30 30 30 42 34 3D 4C 6F 6F 70 43 79 63 6C 65 73 3A 30 30 30 33 32 31 37 30 3D 4C 6F 73 74 41 44 43 53 6D 70 3A 30 30 30 30 30 30 30 30 C0");

        var status = NinoTncStatus.TryParse(frame);

        status.Should().NotBeNull();
        status!.BoardSwitchModeBinary.Should().Be("1111");  // DIP switch position
        status.CurrentMode.Should().Be(14);  // Actual operating mode (decoded from 0x23)
        status.CurrentModeName.Should().Be("300 AFSKPLL IL2P+CRC");
        status.BoardSwitchMode.Should().Be(0x0023);  // Raw firmware mode value
    }
}
