using OpenGM.IO;
using System.Text;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// The pre-Studio ("legacy") text format for <c>ds_map_write</c> / <c>ds_map_read</c>.
/// </summary>
/// <remarks>
/// <para>
/// Not what a modern runner writes by default -- that is the binary encoding in
/// DataStructuresFunctions, and its headers were measured on Windows (see <c>DsHeaderMap</c>).
/// This is the older one both GM8 and Studio's <c>legacy</c> argument speak, and games do still
/// ask for it: I Wanna Spook Jam's save file is in it, written by a real install.
/// </para>
/// <code>
///     map    := entry ("," entry)*
///     entry  := tag ":" hex(key) ":" hex(value)
///     tag    := (key is a string ? 1 : 0) | (value is a string ? 2 : 0)
/// </code>
/// <para>
/// <b>Measured, not guessed.</b> Reversed from that save, whose 33 entries -- reals, strings,
/// zeroes, an MD5, a room name -- all decode cleanly and sensibly under this grammar. A string
/// encodes as its UTF-8 bytes; a real as its IEEE-754 double <b>big-endian</b> with trailing zero
/// bytes dropped, so <c>1.0</c> is <c>3FF0</c> and <c>0.0</c> is the empty string. Hex is
/// uppercase and an empty map is the empty string. Splitting on ',' and ':' is safe because every
/// payload is hex.
/// </para>
/// <para>
/// Note the endianness: this format is big-endian and the binary one is little-endian. They are
/// different formats from different eras, not two spellings of one.
/// </para>
/// <para>
/// The list, queue, stack and priority structures have a legacy format of their own that nothing
/// here has measured, so they neither read nor write one.
/// </para>
/// </remarks>
internal static class DsText
{
    /// <summary>Serialises a map into the format above. Insertion order; a real runner's order is
    /// its own hash table's, and no reader depends on it.</summary>
    public static string WriteMap(IEnumerable<KeyValuePair<object, object?>> map)
    {
        var entries = new List<string>();
        foreach (var (key, value) in map)
        {
            var tag = (key is string ? 1 : 0) | (value is string ? 2 : 0);
            entries.Add($"{tag}:{Hex(key)}:{Hex(value)}");
        }

        return string.Join(",", entries);
    }

    /// <summary>
    /// Parses the format above into <paramref name="into"/>, or returns false having changed
    /// nothing.
    /// </summary>
    /// <remarks>
    /// All-or-nothing on purpose. A half-applied map is a save file the game will happily carry on
    /// with -- some progress restored, the rest silently zero -- which is worse than a load that
    /// visibly did not happen.
    /// </remarks>
    public static bool TryReadMap(string text, Dictionary<object, object?> into, out List<object> order)
    {
        order = [];
        var parsed = new List<KeyValuePair<object, object?>>();
        foreach (var entry in text.Split(','))
        {
            var fields = entry.Split(':');
            if (fields.Length != 3 || !int.TryParse(fields[0], out var tag))
            {
                return false;
            }

            if (!TryUnhex(fields[1], (tag & 1) != 0, out var key) ||
                !TryUnhex(fields[2], (tag & 2) != 0, out var value))
            {
                return false;
            }

            // Keys go through the same normalisation every other ds_map entry point uses, or a key
            // read back from a file would not match the one the game looks it up with.
            parsed.Add(new KeyValuePair<object, object?>(VMExecutor.DictHash(key)!, value));
        }

        foreach (var (key, value) in parsed)
        {
            into[key] = value;
            order.Add(key);
        }

        return true;
    }

    /// <summary>One value, encoded as the grammar wants it.</summary>
    private static string Hex(object? value)
    {
        if (value is string text)
        {
            return Convert.ToHexString(Encoding.UTF8.GetBytes(text));
        }

        var bytes = BitConverter.GetBytes(Real(value));
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        // Trailing zero bytes are dropped, which is why 0.0 comes out as the empty string. Not an
        // optimisation on our part -- it is what the real format does, and a reader that padded
        // differently would decode every whole number wrongly.
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        return Convert.ToHexString(bytes, 0, length);
    }

    /// <summary>
    /// Anything that is not a string, as the double a data structure stores it as. Shared with the
    /// binary format's <c>WriteDsValue</c>, so the two cannot disagree about what a value is.
    /// </summary>
    /// <remarks>
    /// Every integer width the VM can hand us, not just the obvious two: a GML literal small enough
    /// for a 16-bit immediate arrives as a short, and listing only int/long is what once made
    /// <c>global.stage = 0</c> serialise as NaN while the string beside it came back fine.
    /// </remarks>
    public static double Real(object? value) => value switch
    {
        // NaN, not zero. There is no tag for undefined in this format either, so it keeps sharing
        // NaN's encoding exactly as it did in the one before -- a wart, but one games already
        // depend on and one that changing here would silently alter the meaning of old saves.
        // See SaveRoundTripTests.AnUndefinedValueComesBackAsNaNRatherThanUndefined.
        null => double.NaN,
        double d => d,
        float f => f,
        bool b => b ? 1d : 0d,
        sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToDouble(value),
        _ => Unserialisable(value),
    };

    private static double Unserialisable(object value)
    {
        DebugLog.LogWarning(
            $"Can't serialise {value.GetType().Name} into a data structure, writing NaN instead.");
        return double.NaN;
    }

    private static bool TryUnhex(string hex, bool isString, out object? value)
    {
        value = null;
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        if (isString)
        {
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        if (bytes.Length > 8)
        {
            return false;
        }

        // The bytes the writer dropped were zeros, so a short payload pads on the right.
        var padded = new byte[8];
        bytes.CopyTo(padded, 0);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(padded);
        }

        value = BitConverter.ToDouble(padded);
        return true;
    }
}
