using MemoryPack;
using OpenTK.Mathematics;

namespace OpenGM;

public static class Extensions
{
    public static Color4 ABGRToCol4(this uint bgr)
    {
        var bytes = BitConverter.GetBytes(bgr);
        return BitConverter.IsLittleEndian
            ? new Color4(bytes[0], bytes[1], bytes[2], bytes[3])
            : new Color4(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    public static Color4 ABGRToCol4(this int bgr)
    {
        var bytes = BitConverter.GetBytes(bgr);
        return BitConverter.IsLittleEndian
            ? new Color4(bytes[0], bytes[1], bytes[2], bytes[3])
            : new Color4(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    public static Color4 ABGRToCol4(this uint bgr, double alpha)
    {
        var bytes = BitConverter.GetBytes(bgr);
        return BitConverter.IsLittleEndian
            ? new Color4(bytes[0], bytes[1], bytes[2], (byte)(alpha * 255))
            : new Color4(bytes[3], bytes[2], bytes[1], (byte)(alpha * 255));
    }

    public static Color4 ABGRToCol4(this int bgr, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);

        var bytes = BitConverter.GetBytes(bgr);
        return BitConverter.IsLittleEndian
            ? new Color4(bytes[0], bytes[1], bytes[2], (byte)(alpha * 255))
            : new Color4(bytes[3], bytes[2], bytes[1], (byte)(alpha * 255));
    }

    public static uint Col4ToABGR(this Color4 col)
    {
        var byteCol = (System.Drawing.Color)col; // kinda dumb but easy way to get the bytes back

        return (uint)(byteCol.R | (byteCol.G << 8) | (byteCol.B << 16) | (byteCol.A << 24));
    }

    public static Color4 Scale(this Color4 col, double val) => col.Scale((float)val);

    public static Color4 Scale(this Color4 col, float val)
    {
        if (val <= 0)
        {
            return new(0, 0, 0, 0);
        }

        if (val >= 1)
        {
            return new(255, 255, 255, 255);
        }

        return new(col.R * val, col.G * val, col.B * val, col.A * val);
    }

    // sometimes things use \r\n, sometimes they use \n
    //
    // A trailing separator (a string ending in \n, or -- via CompatFlags.ApplyHashNewlines -- a
    // trailing #) must NOT count as an extra, invisible blank final line: .NET's Split('\n') gives
    // "A\n".Split('\n') == ["A", ""], but real GM's own line iteration (confirmed against
    // OpenGMK's LineIterator, gm8emulator/src/game/draw.rs) stops once it's consumed the string and
    // never manufactures a line for "whatever comes after the last separator" when that's nothing --
    // reserving vertical space (string_height, draw_text's own line spacing) for a line nothing
    // actually occupies. Only the single trailing empty entry is dropped -- a real intentional blank
    // line elsewhere (or even a genuine blank line right before the end, from two consecutive
    // separators) is untouched.
    public static string[] SplitLines(this string @this)
    {
        var lines = @this.Replace("\r\n", "\n").Split('\n');

        if (lines.Length > 1 && lines[^1].Length == 0)
        {
            return lines[..^1];
        }

        return lines;
    }

    public static T ReadMemoryPack<T>(this BinaryReader @this)
    {
        var length = @this.ReadInt32();
        var bytes = @this.ReadBytes(length);
        return MemoryPackSerializer.Deserialize<T>(bytes)!;
    }

    public static void WriteMemoryPack<T>(this BinaryWriter @this, T value)
    {
        var bytes = MemoryPackSerializer.Serialize(value);
        @this.Write(bytes.Length);
        @this.Write(bytes);
    }

    // https://stackoverflow.com/a/1262619/17543401
    private static Random rng = new();
    public static void Shuffle<T>(this IList<T> list)
    {
        var n = list.Count;
        while (n > 1)
        {
            n--;
            var k = rng.Next(n + 1); // TODO : bad! use GMRandom or get rid of this func entirely
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}
