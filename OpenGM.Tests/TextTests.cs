using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using OpenGM.SerializedFiles;

namespace OpenGM.Tests;

/// <summary>
/// Line splitting against a real font's metrics (fnt_main: every digit is 7px of advance).
/// See <see cref="TextWrapTests"/> for the wrapping rules themselves, against a synthetic font
/// whose round numbers make the expected break points obvious.
/// </summary>
[TestClass]
public class TextTests
{
    [TestInitialize]
    public void SetUp()
    {
        var text = File.ReadAllText("fnt_main.json");
        TextManager.fontAsset = JsonConvert.DeserializeObject<FontAsset>(text)!;

        // The fixture predates FontAsset's Size/ScaleX/ScaleY fields and so leaves them at 0. A
        // real font always carries a scale (GameLoader fills it in); without this every glyph
        // measures 0px wide and nothing ever wraps.
        TextManager.fontAsset.ScaleX = 1;
        TextManager.fontAsset.ScaleY = 1;
    }

    [TestMethod]
    public void NoWrapNoNewline()
    {
        var str = "123456789";

        var lines = TextManager.WrapText(str, -1);

        Assert.AreEqual(1, lines.Length);
        Assert.AreEqual(str, lines[0]);
    }

    [TestMethod]
    public void NoWrapOneNewline()
    {
        var lines = TextManager.WrapText("12345\n6789", -1);

        Assert.AreEqual(2, lines.Length);
    }

    [TestMethod]
    public void WrappingNoNewline()
    {
        // Nine digits at 7px of advance each, capped at 20px a line: two digits (14px) fit and
        // three (21px) don't, so this comes out as four full lines plus the leftover digit.
        //
        // The old, unused SplitText this replaced answered 3 here, by taking the character that
        // overflowed as well -- lines came out one character (and 1px) over the limit it was given.
        var lines = TextManager.WrapText("123456789", 20);

        CollectionAssert.AreEqual(new[] { "12", "34", "56", "78", "9" }, lines);
    }
}
