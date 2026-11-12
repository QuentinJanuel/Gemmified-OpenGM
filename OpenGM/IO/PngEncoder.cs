using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenGM.IO;

/// <summary>
/// A minimal PNG writer: 8-bit RGBA, one IDAT, no interlacing, no filtering.
///
/// StbImageSharp only decodes, and the one other encoder in the tree (ImageMagick, via
/// UndertaleModLib) has no wasm build at all -- which is why screen_save_part settled for stashing
/// pixels in a memory cache instead of writing a file. That works only for the
/// capture-then-sprite_add-the-same-path idiom; a game that checks file_exists first, or expects the
/// file to still be there next session, needs a real one on disk. tijit engine 2's save previews do
/// both: surface_save on saving, then file_exists + sprite_add when the file-select menu is built.
///
/// Deliberately not optimised: the compression level is the default rather than the smallest, and
/// every scanline uses filter type 0. These files are small (a 128x96 preview), written rarely, and
/// any PNG decoder reads them.
/// </summary>
public static class PngEncoder
{
    /// <param name="rgba">Row-major, top row first, 4 bytes per pixel.</param>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException($"Need {width * height * 4} bytes for {width}x{height}, got {rgba.Length}", nameof(rgba));
        }

        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]); // PNG signature

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;  // bits per channel
        ihdr[9] = 6;  // colour type: RGBA
        ihdr[10] = 0; // deflate
        ihdr[11] = 0; // adaptive filtering
        ihdr[12] = 0; // no interlacing
        WriteChunk(output, "IHDR"u8, ihdr);

        // Scanlines, each prefixed with its filter type.
        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0; // filter: None
            rgba.Slice(y * stride, stride).CopyTo(raw.AsSpan((y * (stride + 1)) + 1, stride));
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            deflate.Write(raw);
        }
        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));

        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);

        // The CRC covers the type and the data, but not the length.
        var crc = Crc32.Compute(type);
        crc = Crc32.Compute(data, crc);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    // CRC32 lives in OpenGM.Crc32 -- GameMaker's ds_map buckets by the same function.
}
