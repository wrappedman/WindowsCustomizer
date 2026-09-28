using WindowsCustomizer.Core.Services;
using Xunit;

namespace WindowsCustomizer.Core.Tests;

public class WimHeaderReaderTests
{
    /// <summary>Builds a synthetic but structurally valid WIMHEADER_V1_PACKED buffer (real [MS-WIM] layout).</summary>
    internal static byte[] BuildHeader(
        uint version = 0x00010D00,
        uint flags = 0x00000002,
        uint compression = 2,
        uint imageCount = 4,
        ushort part = 1,
        ushort totalParts = 1,
        Guid? guid = null)
    {
        var buffer = new byte[WimHeaderReader.HeaderSize];
        var magic = System.Text.Encoding.ASCII.GetBytes("MSWIM\0\0\0");
        Array.Copy(magic, buffer, magic.Length);
        BitConverter.GetBytes((uint)WimHeaderReader.HeaderSize).CopyTo(buffer, 8);
        BitConverter.GetBytes(version).CopyTo(buffer, 12);
        BitConverter.GetBytes(flags).CopyTo(buffer, 16);
        BitConverter.GetBytes(compression).CopyTo(buffer, 20);   // chunk size / legacy enum
        (guid ?? Guid.NewGuid()).ToByteArray().CopyTo(buffer, 24); // gWIMGuid
        BitConverter.GetBytes(part).CopyTo(buffer, 40);
        BitConverter.GetBytes(totalParts).CopyTo(buffer, 42);
        BitConverter.GetBytes(imageCount).CopyTo(buffer, 44);
        return buffer;
    }

    [Fact]
    public void TryParse_ValidHeader_ReturnsSummary()
    {
        var g = Guid.NewGuid();
        var summary = WimHeaderReader.TryParse(BuildHeader(imageCount: 6, compression: 2, guid: g));

        Assert.NotNull(summary);
        Assert.Equal(6, summary!.ImageCount);
        Assert.Equal("LZX (maximum)", summary.Compression);
        Assert.Equal("1.13.0", summary.Version);
        Assert.False(summary.IsSpanned);
        Assert.Equal(g, summary.WimGuid);
    }

    [Fact]
    public void TryParse_EsdLzmsFlag_ReportsRecoveryCompression()
    {
        // ESD files use the LZMS flag block (bit 19) and compression code 3.
        var summary = WimHeaderReader.TryParse(BuildHeader(flags: 0x00080002, compression: 3, imageCount: 1));
        Assert.NotNull(summary);
        Assert.Equal("LZMS (recovery)", summary!.Compression);
    }

    [Fact]
    public void TryParse_SpannedParts_DetectsSpanning()
    {
        var summary = WimHeaderReader.TryParse(BuildHeader(part: 2, totalParts: 4));
        Assert.NotNull(summary);
        Assert.True(summary!.IsSpanned);
        Assert.Equal(2, summary.PartNumber);
        Assert.Equal(4, summary.TotalParts);
    }

    [Fact]
    public void TryParse_WrongMagic_ReturnsNull()
    {
        var buffer = BuildHeader();
        buffer[0] = (byte)'X';
        Assert.Null(WimHeaderReader.TryParse(buffer));
    }

    [Fact]
    public void TryParse_TooShort_ReturnsNull()
    {
        Assert.Null(WimHeaderReader.TryParse(new byte[64]));
    }

    [Fact]
    public void TryRead_Stream_ParsesFromPositionZero()
    {
        using var stream = new MemoryStream(BuildHeader(imageCount: 3));
        var summary = WimHeaderReader.TryRead(stream);
        Assert.NotNull(summary);
        Assert.Equal(3, summary!.ImageCount);
    }

    [Fact]
    public void TryRead_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();
        Assert.Null(WimHeaderReader.TryRead(stream));
    }
}
