namespace OpenGM;

/// <summary>
/// Standard CRC-32 (the PNG/zlib polynomial, 0xEDB88320 reflected).
/// </summary>
/// <remarks>
/// Two callers, for unrelated reasons: PNG chunks carry one by specification, and GameMaker's
/// <c>ds_map</c> buckets its keys by one -- see <c>DataStructuresFunctions.InRunnerOrder</c>. Shared
/// rather than written twice so there is one table and one definition of "the CRC" in the engine.
/// </remarks>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    /// <summary>
    /// The CRC of <paramref name="data"/>, optionally continuing from a previous result so a value
    /// can be accumulated across several spans.
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> data, uint start = 0)
    {
        var c = start ^ 0xFFFFFFFF;
        foreach (var b in data)
        {
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFF;
    }
}
