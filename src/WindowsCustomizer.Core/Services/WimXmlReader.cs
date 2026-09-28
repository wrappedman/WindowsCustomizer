namespace WindowsCustomizer.Core.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

/// <summary>One Windows image (edition) as described by the WIM XML manifest.</summary>
public sealed record WimImageEntry(
    int Index,
    string Name,
    string Description,
    string EditionId,
    string Architecture,
    string Build,
    string InstallationType);

/// <summary>
/// Reads the per-edition XML manifest of a WIM/ESD WITHOUT extracting or mounting it.
/// Per the Windows Imaging Format documentation the XML data resource is stored as
/// uncompressed UTF-16 (BOM FE FF) clear text, so a tiny seek + read is enough — this makes
/// the Edition list appear in under a second, straight from the ISO.
/// </summary>
public static class WimXmlReader
{
    private static readonly byte[] BomUtf16Le = { 0xFF, 0xFE };
    private static readonly byte[] BomUtf8 = { 0xEF, 0xBB, 0xBF };

    /// <summary>Reads the image list from a WIM/ESD stream. Returns null when the manifest is not readable.</summary>
    public static List<WimImageEntry>? TryReadImages(Stream wimStream)
    {
        try
        {
            var header = ReadHeader(wimStream);
            if (header is null)
            {
                return null;
            }

            var xmlBytes = TryReadXmlResource(wimStream, header.Value);
            if (xmlBytes is null)
            {
                return null;
            }

            return ParseManifest(xmlBytes);
        }
        catch
        {
            return null; // callers fall back to the DISM path
        }
    }

    private static byte[]? ReadExactly(Stream stream, long offset, int count)
    {
        if (offset < 0 || count <= 0 || count > 64 * 1024 * 1024)
        {
            return null;
        }

        if (!stream.CanSeek)
        {
            return null;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return read == count ? buffer : null;
    }

    private static (long Size, byte Flags, long Offset, long Original)? ReadHeader(Stream stream)
    {
        var header = ReadExactly(stream, 0, WimHeaderReader.HeaderSize);
        if (header is null || WimHeaderReader.TryParse(header) is null)
        {
            return null;
        }

        // rhXmlData lives at offset 72 in the 208-byte header (see [MS-WIM]).
        return WimHeaderReader.ReadResourceEntry(header, WimHeaderReader.XmlDataResourceOffset);
    }

    private static byte[]? TryReadXmlResource(Stream stream, (long Size, byte Flags, long Offset, long Original) res)
    {
        // Sanity: the manifest is small (typically < 1 MB) and lives after the header.
        if (res.Offset < WimHeaderReader.HeaderSize || res.Size <= 0 || res.Size > 32 * 1024 * 1024)
        {
            return null;
        }

        if (stream.CanSeek && res.Offset + res.Size > stream.Length)
        {
            return null;
        }

        // Compressed manifests are rare — bail out and let the caller use DISM.
        if (res.Size != res.Original && res.Original > 0)
        {
            return null;
        }

        return ReadExactly(stream, res.Offset, (int)res.Size);
    }

    /// <summary>Parses the UTF-16/UTF-8 XML manifest into image entries.</summary>
    public static List<WimImageEntry>? ParseManifest(byte[] xmlBytes)
    {
        try
        {
            var text = Decode(xmlBytes);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var doc = XDocument.Parse(text);
            var root = doc.Root;
            if (root is null || !root.Name.LocalName.Equals("WIM", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var images = new List<WimImageEntry>();
            foreach (var image in root.Elements().Where(e => e.Name.LocalName.Equals("IMAGE", StringComparison.OrdinalIgnoreCase)))
            {
                var indexAttr = image.Attribute("INDEX")?.Value;
                if (!int.TryParse(indexAttr, out var index))
                {
                    index = images.Count + 1;
                }

                string Get(string name) =>
                    image.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim()
                    ?? string.Empty;

                var windows = image.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase));
                string WinGet(string name) =>
                    windows?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim()
                    ?? string.Empty;

                var archCode = WinGet("ARCH");
                images.Add(new WimImageEntry(
                    Index: index,
                    Name: Get("NAME"),
                    Description: Get("DESCRIPTION"),
                    EditionId: WinGet("EDITIONID"),
                    Architecture: ArchitectureName(archCode),
                    Build: WinGet("BUILD"),
                    InstallationType: WinGet("INSTALLATIONTYPE")));
            }

            return images.Count > 0 ? images : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(BomUtf16Le))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.AsSpan().StartsWith(BomUtf8))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // Manifest without BOM: prefer UTF-16LE (the documented format) when it has NULs.
        if (bytes.Length > 4 && bytes[1] == 0 && bytes[3] == 0)
        {
            return Encoding.Unicode.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string ArchitectureName(string code) => code switch
    {
        "0" => "x86",
        "5" => "ARM",
        "6" => "IA64",
        "9" => "x64",
        "12" => "ARM64",
        _ => code
    };
}
