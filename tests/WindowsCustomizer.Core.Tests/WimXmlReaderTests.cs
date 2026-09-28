using System.Text;
using WindowsCustomizer.Core.Services;
using Xunit;

namespace WindowsCustomizer.Core.Tests;

public class WimXmlReaderTests
{
    private const string SampleManifest = """
        <?xml version="1.0" encoding="utf-16" standalone="yes"?>
        <WIM>
          <IMAGE INDEX="1">
            <NAME>Windows 11 Home</NAME>
            <DESCRIPTION>Windows 11 Home</DESCRIPTION>
            <WINDOWS>
              <ARCH>9</ARCH>
              <BUILD>22621</BUILD>
              <EDITIONID>Core</EDITIONID>
              <INSTALLATIONTYPE>Client</INSTALLATIONTYPE>
            </WINDOWS>
          </IMAGE>
          <IMAGE INDEX="2">
            <NAME>Windows 11 Pro</NAME>
            <DESCRIPTION>Windows 11 Pro</DESCRIPTION>
            <WINDOWS>
              <ARCH>9</ARCH>
              <BUILD>22621</BUILD>
              <EDITIONID>Professional</EDITIONID>
              <INSTALLATIONTYPE>Client</INSTALLATIONTYPE>
            </WINDOWS>
          </IMAGE>
        </WIM>
        """;

    [Fact]
    public void ParseManifest_ReadsAllImages()
    {
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(SampleManifest))
            .ToArray();

        var images = WimXmlReader.ParseManifest(bytes);

        Assert.NotNull(images);
        Assert.Equal(2, images!.Count);
        Assert.Equal(1, images[0].Index);
        Assert.Equal("Windows 11 Home", images[0].Name);
        Assert.Equal("Core", images[0].EditionId);
        Assert.Equal("x64", images[0].Architecture);
        Assert.Equal("22621", images[0].Build);
        Assert.Equal("Windows 11 Pro", images[1].Name);
    }

    [Fact]
    public void ParseManifest_Utf8WithoutBom_AlsoWorks()
    {
        var bytes = Encoding.UTF8.GetBytes(SampleManifest);
        var images = WimXmlReader.ParseManifest(bytes);
        Assert.NotNull(images);
        Assert.Equal(2, images!.Count);
    }

    [Fact]
    public void ParseManifest_Garbage_ReturnsNull()
    {
        Assert.Null(WimXmlReader.ParseManifest(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void TryReadImages_FromFakeWimStream_ReadsXmlResource()
    {
        // Build a minimal fake WIM: 208-byte header + XML resource at offset 208.
        var xmlBytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(SampleManifest))
            .ToArray();

        using var ms = new MemoryStream();
        var header = WimHeaderReaderTests.BuildHeader(imageCount: 2, flags: 0x00040002, compression: 65536);
        // rhXmlData at offset 72: sizeAndFlags(u64) offset(u64) originalSize(u64)
        BitConverter.GetBytes((ulong)xmlBytes.Length).CopyTo(header, 72);
        BitConverter.GetBytes(208UL).CopyTo(header, 80);
        BitConverter.GetBytes((ulong)xmlBytes.Length).CopyTo(header, 88);

        ms.Write(header);
        ms.Write(xmlBytes);
        ms.Position = 0;

        var images = WimXmlReader.TryReadImages(ms);

        Assert.NotNull(images);
        Assert.Equal(2, images!.Count);
        Assert.Equal("Windows 11 Pro", images[1].Name);
    }

    [Fact]
    public void TryReadImages_NonWimStream_ReturnsNull()
    {
        using var ms = new MemoryStream(new byte[512]);
        Assert.Null(WimXmlReader.TryReadImages(ms));
    }
}
