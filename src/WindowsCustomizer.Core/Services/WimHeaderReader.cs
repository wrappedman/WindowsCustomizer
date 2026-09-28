using System.Buffers.Binary;
using System.Text;
using WindowsCustomizer.Core.Models;

namespace WindowsCustomizer.Core.Services;

/// <summary>
/// Minimal, allocation-light WIM/ESD header parser. The 208-byte header at offset 0 is always
/// uncompressed — even inside ESD files — so we can learn image count, compression and layout
/// without any heavy image machinery. Deep per-image metadata (editions, builds) is Phase 2 work.
/// </summary>
public static class WimHeaderReader
{
    /// <summary>Size of WIMHEADER_V1_PACKED as produced by wimgapi/DISM.</summary>
    public const int HeaderSize = 208;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MSWIM\0\0\0");

    /// <summary>Tries to parse a WIM header from a stream starting at position 0.</summary>
    public static WimHeaderSummary? TryRead(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            return null;
        }

        Span<byte> buffer = stackalloc byte[HeaderSize];
        var total = 0;
        try
        {
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            while (total < HeaderSize)
            {
                var read = stream.Read(buffer.Slice(total, HeaderSize - total));
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }
        }
        catch (IOException)
        {
            return null;
        }

        return total < HeaderSize ? null : TryParse(buffer);
    }

    /// <summary>Tries to parse a WIM header from a byte buffer of at least <see cref="HeaderSize"/> bytes.</summary>
    /// <remarks>
    /// Layout per [MS-WIM] _WIMHEADER_V1_PACKED (208 bytes):
    /// ImageTag(8) cbSize(4) dwVersion(4) dwFlags(4) dwCompressionSize(4) gWIMGuid(16)
    /// usPartNumber(2) usTotalParts(2) dwImageCount(4) rhOffsetTable(24) rhXmlData(24)
    /// rhBootMetadata(24) dwBootIndex(4) rhIntegrity(24) bUnused(60).
    /// </remarks>
    public static WimHeaderSummary? TryParse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            return null;
        }

        // 0..7   ImageTag "MSWIM\0\0\0"
        for (var i = 0; i < Magic.Length; i++)
        {
            if (buffer[i] != Magic[i])
            {
                return null;
            }
        }

        //  8..11  cbSize (header size, normally 208)
        var cbSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4));
        if (cbSize is 0 or > 4096)
        {
            return null;
        }

        // 12..15  dwVersion (0x00010D00 == 1.13)
        var version = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4));
        // 16..19  dwFlags
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4));
        // 20..23  dwCompressionSize — the chunk size on real files; small legacy enum handled in CompressionName.
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20, 4));
        // 24..39  gWIMGuid
        var guid = new Guid(buffer.Slice(24, 16));
        // 40..41  usPartNumber
        var partNumber = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(40, 2));
        // 42..43  usTotalParts
        var totalParts = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(42, 2));
        // 44..47  dwImageCount
        var imageCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(44, 4));

        var major = (version >> 16) & 0xFFFF;
        var minor = (version >> 8) & 0xFF;
        var patch = version & 0xFF;

        var isSpanned = (flags & 0x00000008) != 0 || totalParts > 1;

        return new WimHeaderSummary(
            ImageCount: (int)imageCount,
            Compression: CompressionName(compression, flags),
            Version: $"{major}.{minor}.{patch}",
            IsSpanned: isSpanned,
            PartNumber: partNumber,
            TotalParts: totalParts,
            Flags: flags,
            WimGuid: guid);
    }

    /// <summary>Resource entry (RESHDR_DISK_SHORT) helpers — 24 bytes: sizeAndFlags(8) offset(8) originalSize(8).</summary>
    public static (long Size, byte Flags, long Offset, long OriginalSize) ReadResourceEntry(ReadOnlySpan<byte> buffer, int offset)
    {
        var sizeAndFlags = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8));
        var size = (long)(sizeAndFlags & 0x00FF_FFFF_FFFF_FFFFUL);
        var flags = (byte)(sizeAndFlags >> 56);
        var resOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset + 8, 8));
        var original = (long)BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset + 16, 8));
        return (size, flags, resOffset, original);
    }

    /// <summary>Offset of the rhXmlData resource entry inside the 208-byte header.</summary>
    public const int XmlDataResourceOffset = 72;

    private static string CompressionName(uint compression, uint flags)
    {
        // Prefer the explicit flags block (bit 16/17/18) when present; fall back to the enum.
        if ((flags & 0x00080000) != 0)
        {
            return "LZMS (recovery)";
        }

        if ((flags & 0x00040000) != 0)
        {
            return "LZX (maximum)";
        }

        if ((flags & 0x00020000) != 0)
        {
            return "XPRESS (fast)";
        }

        // On real files bytes 20..23 hold the chunk size (e.g. 65536); only small legacy
        // values are treated as the compression enum.
        return compression switch
        {
            0 => "None",
            1 => "XPRESS (fast)",
            2 => "LZX (maximum)",
            3 => "LZMS (recovery)",
            4 => "LZMS (recovery)",
            <= 65536 => "LZX (maximum)",
            _ => "LZX (maximum)"
        };
    }
}
