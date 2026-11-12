using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.SerializedFiles;

namespace OpenGM.Tests;

/// <summary>
/// draw_text_ext's width argument was parsed and then dropped ("// TODO : implement"), so text
/// that should wrap ran straight off the end of its box -- Continuity's dialogue box draws with
/// draw_text_ext_transformed(..., -1, 704, ...) and overflowed on the right.
/// </summary>
[TestClass]
public class TextWrapTests
{
    // A 10px-per-character font, so an expected break point is just a character count.
    private const int CharWidth = 10;

    [TestInitialize]
    public void SetUp()
    {
        var font = new FontAsset
        {
            name = "test font",
            spriteIndex = -1,
            texture = new SpritePageItem(),
            Size = 10,
            ScaleX = 1,
            ScaleY = 1
        };

        for (var c = 32; c < 127; c++)
        {
            var glyph = new Glyph { characterIndex = c, frameIndex = -1, shift = CharWidth, w = CharWidth, h = 12 };
            font.entries.Add(glyph);
            font.entriesDict.Add(c, glyph);
        }

        TextManager.fontAsset = font;
    }

    private static string[] Wrap(string text, int width) => TextManager.WrapText(text, width);

    [TestMethod]
    public void NegativeWidthDoesNotWrap()
    {
        // GM's documented "no wrapping" width, and what every non-ext draw call passes.
        CollectionAssert.AreEqual(new[] { "one two three" }, Wrap("one two three", -1));
    }

    [TestMethod]
    public void TextNarrowerThanTheWidthIsLeftAlone()
    {
        CollectionAssert.AreEqual(new[] { "one two" }, Wrap("one two", 100));
    }

    [TestMethod]
    public void WrapsAtTheLastSpaceThatFits()
    {
        // "one two three" is 13 chars = 130px. At 90px "one two" (7 chars, 70px) fits but
        // "one two t" doesn't, so it breaks at the space before "three".
        CollectionAssert.AreEqual(new[] { "one two", "three" }, Wrap("one two three", 90));
    }

    [TestMethod]
    public void TheSpaceBrokenOnIsDropped()
    {
        // Not " three" -- a wrapped line doesn't start with the whitespace it broke on, or every
        // line after the first would be visibly indented.
        var lines = Wrap("aaa bbb ccc", 70);

        CollectionAssert.AreEqual(new[] { "aaa bbb", "ccc" }, lines);
    }

    [TestMethod]
    public void WrapsRepeatedlyForTextManyTimesTheWidth()
    {
        CollectionAssert.AreEqual(new[] { "aa", "bb", "cc", "dd" }, Wrap("aa bb cc dd", 25));
    }

    [TestMethod]
    public void AWordWiderThanTheWholeLineBreaksMidWord()
    {
        // No space to break at, so it has to split the word rather than overflow forever.
        CollectionAssert.AreEqual(new[] { "abcde", "fghij", "kl" }, Wrap("abcdefghijkl", 50));
    }

    [TestMethod]
    public void AWidthNarrowerThanOneCharacterStillTerminates()
    {
        // Every line takes at least one character, or this loops forever.
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Wrap("abc", 1));
    }

    [TestMethod]
    public void ExplicitNewlinesAlwaysBreakAndAreWrappedIndependently()
    {
        CollectionAssert.AreEqual(new[] { "aa", "bb", "cc" }, Wrap("aa bb\ncc", 25));
    }

    [TestMethod]
    public void EmptyTextStaysASingleEmptyLine()
    {
        // The draw path indexes lines[^1] to measure the last line's height, so this must never
        // come back empty.
        CollectionAssert.AreEqual(new[] { "" }, Wrap("", 100));
    }

    [TestMethod]
    public void DefaultLineSeparationIsTheTallestGlyphNotM()
    {
        // Every glyph in the synthetic font is 12px except one deliberately taller one, standing in
        // for the accents/descenders/tall punctuation a real font has above its 'M'. GM paces lines
        // by the tallest glyph in the whole font (OpenGMK: font.tallest_char_height), so lines don't
        // overlap their neighbours' descenders.
        Assert.AreEqual(12, TextManager.FontHeight());

        var tall = new Glyph { characterIndex = 'Á', frameIndex = -1, shift = CharWidth, w = CharWidth, h = 17 };
        TextManager.fontAsset.entries.Add(tall);
        TextManager.fontAsset.entriesDict.Add('Á', tall);

        Assert.AreEqual(17, TextManager.FontHeight(), "an 'M'-only measurement would still say 12");
    }

    [TestMethod]
    public void WrappedWidthNeverExceedsTheLimit()
    {
        const string text = "Look at these moves! This new cape and ears aren't just for show, that's for sure!";

        foreach (var width in new[] { 40, 90, 200, 704 })
        {
            foreach (var line in Wrap(text, width))
            {
                Assert.IsTrue(
                    TextManager.LineWidth(line) <= width || line.Length == 1,
                    $"line \"{line}\" is {TextManager.LineWidth(line)}px, over the {width}px limit");
            }
        }
    }
}
