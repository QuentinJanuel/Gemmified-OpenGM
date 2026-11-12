using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenTK.Mathematics;

namespace OpenGM;

public static class TextManager
{
    public static List<FontAsset> FontAssets = new();

    /// <summary>The font <c>draw_set_font</c> chose, or null when nothing has chosen one.</summary>
    public static FontAsset? ChosenFont { get; private set; }

    /// <summary>
    /// The font text is drawn with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Falls back to the game's first font rather than being null.</b> A real GameMaker runner
    /// always has a font -- Studio's built-in default when the game has never called
    /// <c>draw_set_font</c> -- so <c>draw_text</c> before any font is set draws in it. This used
    /// to be a plain <c>null!</c> field, which made that same code take the engine down with a
    /// NullReferenceException in <see cref="FontHeight"/>, from inside whatever event happened to
    /// draw first. A game's own first font is not the default Studio would have used, but it is a
    /// font, and text in the wrong typeface is a far smaller lie than a crash.
    /// </para>
    /// <para>
    /// Still null for a game that carries no fonts at all, which is why the drawing paths check.
    /// <see cref="ChosenFont"/> is what <c>draw_get_font</c> reports, so a 2.3 game asking whether
    /// a font was set still gets -1 when none was.
    /// </para>
    /// </remarks>
    public static FontAsset fontAsset
    {
        get => ChosenFont ?? FontAssets.FirstOrDefault()!;
        set => ChosenFont = value;
    }

    public static HAlign halign;
    public static VAlign valign;

    public static void DrawText(double x, double y, string text)
    {
        DrawTextTransformed(x, y, text, 1, 1, 0);
    }

    public static void DrawTextTransformed(double x, double y, string text, double xscale, double yscale, double angle)
    {
        // Nothing to draw with: a game carrying no fonts at all. Silently drawing nothing is what
        // the rest of this file already assumes (see the null checks below) and is the only thing
        // left to do -- there is no glyph anywhere to fall back to.
        if (fontAsset == null)
        {
            return;
        }

        var c = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);
        CustomWindow.Draw(new GMTextJob()
        {
            text = text,
            screenPos = new Vector2d(x, y),
            Colors = [c, c, c, c],
            halign = halign,
            valign = valign,
            scale = new Vector2d(xscale, yscale),
            angle = angle,
            asset = fontAsset,
            // The default separation these paths have no argument to override -- see FontHeight.
            lineSep = FontHeight()
        });
    }

    /// <summary>
    /// The default distance between lines, i.e. what a line separation argument of -1 resolves to.
    /// GM's manual describes this as "based on the height of the 'M' character", and this used to
    /// take it literally -- but the runner actually uses the tallest glyph anywhere in the font
    /// (OpenGMK's draw.rs: `None => font.tallest_char_height`, and TextHeight below is the same
    /// measurement ported from the same place). Those only coincide for fonts whose 'M' happens to
    /// be the tallest thing in them; anything with accents, descenders or tall punctuation is
    /// taller, so lines were drawn overlapping their neighbours' descenders (Continuity's
    /// dialogfont: 'M' is 20px against a 25px tallest glyph, a fifth too tight).
    /// </summary>
    public static int FontHeight()
    {
        var font = fontAsset;
        if (font == null)
        {
            return 0;
        }

        var tallest = 0;
        foreach (var glyph in font.entries)
        {
            if (glyph.h > tallest)
            {
                tallest = glyph.h;
            }
        }

        return tallest;
    }

    /// <summary>
    /// The lines draw_text_ext and friends actually render: explicit newlines always break, and
    /// anything wider than <paramref name="lineWidth"/> wraps at the last space that fits. A
    /// negative width means no wrapping at all, matching GM's own documented default.
    /// </summary>
    public static string[] WrapText(string text, int lineWidth)
    {
        var lines = text.SplitLines();

        if (lineWidth < 0 || fontAsset == null)
        {
            return lines;
        }

        var wrapped = new List<string>();

        foreach (var line in lines)
        {
            var start = 0;

            while (true)
            {
                var width = 0;
                var end = start;
                var lastFittingSpace = -1;

                while (end < line.Length)
                {
                    var charWidth = CharWidth(line[end]);

                    // Always takes at least one character: a line narrower than a single glyph
                    // would otherwise never advance.
                    if (width + charWidth > lineWidth && end > start)
                    {
                        break;
                    }

                    if (line[end] == ' ')
                    {
                        lastFittingSpace = end;
                    }

                    width += charWidth;
                    end++;
                }

                if (end == line.Length)
                {
                    wrapped.Add(line[start..]);
                    break;
                }

                // Break at the last space that fits, dropping the space itself. If what didn't fit
                // IS the space, everything before it already fits, so break there rather than
                // backtracking to the previous word. A word wider than the whole line has no space
                // to break at at all, so break mid-word rather than stall.
                var breakAt = line[end] == ' '
                    ? end
                    : lastFittingSpace > start ? lastFittingSpace : end;
                wrapped.Add(line[start..breakAt]);
                start = breakAt;

                // A wrapped line doesn't begin with the run of spaces it broke on.
                while (start < line.Length && line[start] == ' ')
                {
                    start++;
                }

                if (start >= line.Length)
                {
                    break;
                }
            }
        }

        return wrapped.ToArray();
    }

    public static double TextHeight(string text)
    {
        if (text == null || text.Length == 0)
        {
            return 0;
        }

        // BUG : GM uses ScaleX for the height here. What???

        if (fontAsset.texture == null)
        {
            // runtime created
            return fontAsset.Size * fontAsset.ScaleX;
        }
        else
        {
            var maxGlyphHeight = fontAsset.entries.MaxBy(x => x.h)!.h;
            return maxGlyphHeight * fontAsset.ScaleX;
        }
    }

    /// <summary>
    /// Tallest glyph height on a single line (no embedded newlines). Split out from
    /// <see cref="StringHeight"/> so callers that already have a line in hand (e.g. the per-line
    /// text-draw loop) don't pay for a fresh <see cref="Extensions.SplitLines"/> allocation per
    /// call just to measure one line -- see the batched measurement in CustomWindow.Draw(GMTextJob).
    /// </summary>
    public static int LineHeight(string line)
    {
        var tallestChar = 0;
        foreach (var character in line)
        {
            if (!fontAsset.entriesDict.TryGetValue(character, out var glyph))
            {
                continue;
            }

            if (glyph.h > tallestChar)
            {
                tallestChar = glyph.h;
            }
        }

        return tallestChar;
    }

    /// <summary>
    /// How far one character advances the pen. Split out of <see cref="LineWidth"/> so
    /// <see cref="WrapText"/> decides where to break using the exact same widths the text is then
    /// measured and drawn with.
    /// </summary>
    public static int CharWidth(char character)
    {
        if (!fontAsset.entriesDict.TryGetValue(character, out Glyph? entry))
        {
            // offset sprite text for spaces
            return fontAsset.IsSpriteFont() && character == ' ' ? (int)fontAsset.Size : 0;
        }

        var width = (int)(entry.shift * fontAsset.ScaleX);

        if (fontAsset.IsSpriteFont())
        {
            width += (int)(fontAsset.sep * fontAsset.ScaleX);
        }

        return width;
    }

    /// <summary>Width of a single line (no embedded newlines). See <see cref="LineHeight"/>.</summary>
    public static int LineWidth(string line)
    {
        var totalWidth = 0;
        for (var i = 0; i < line.Length; i++)
        {
            totalWidth += CharWidth(line[i]);
        }

        return totalWidth;
    }

    public static int StringHeight(string text)
    {
        // TODO : wtf is this?

        var lines = text.SplitLines();

        // get tallest character in last line
        return ((lines.Length - 1) * FontHeight()) + LineHeight(lines[^1]);
    }

    public static int StringWidth(string text)
    {
        var lines = text.SplitLines();

        var longestLine = 0;
        foreach (var line in lines)
        {
            var width = LineWidth(line);
            if (width > longestLine)
            {
                longestLine = width;
            }
        }

        return longestLine;
    }

    public static void DrawTextColor(double x, double y, string text, int c1, int c2, int c3, int c4, double alpha)
    {
        CustomWindow.Draw(new GMTextJob()
        {
            text = text,
            screenPos = new Vector2d(x, y),
            Colors = [c1.ABGRToCol4(alpha), c2.ABGRToCol4(alpha), c3.ABGRToCol4(alpha), c4.ABGRToCol4(alpha)],
            halign = halign,
            valign = valign,
            scale = Vector2.One,
            angle = 0,
            asset = fontAsset,
            // The default separation these paths have no argument to override -- see FontHeight.
            lineSep = FontHeight()
        });
    }
}
