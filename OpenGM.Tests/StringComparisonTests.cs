using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// Two strings are compared as strings, whatever they happen to look like.
///
/// GameMaker never reads a string as a number in order to compare it with another string. Doing so
/// was not merely lax here, it was wrong in a way that broke reflexivity: a string whose exponent
/// overflows a double parses as infinity on both sides, and <c>ApproxEqual</c> subtracts its
/// arguments, so infinity minus infinity gave NaN and the string compared *unequal to itself*.
///
/// That is not hypothetical. The mod loader routes each mod's messages with
/// <c>if (channel == "&lt;channel&gt;")</c>, and a mod whose id hashed to the eight hex characters
/// <c>9e424042</c> had every message it was sent dropped on the floor, with no error anywhere --
/// its keybinds never arrived and its sprite transfer hung forever.
/// </summary>
[TestClass]
public class StringComparisonTests
{
    private static bool Equal(string left, string right)
    {
        var asm = $"""
                  :[0]
                  push.s "{left}"@0
                  conv.s.v
                  push.s "{right}"@0
                  conv.s.v
                  cmp.v.v EQ
                  conv.b.v
                  ret.v
                  """;
        return TestUtils.ExecuteScript($"\"{left}\" == \"{right}\"", asm).Conv<bool>();
    }

    [TestMethod]
    public void AStringEqualsItself()
    {
        Assert.IsTrue(Equal("hello", "hello"));
        Assert.IsTrue(Equal("d47c4b13", "d47c4b13"), "a channel that does not look numeric");
    }

    [TestMethod]
    public void AStringWhoseExponentOverflowsEqualsItself()
    {
        // The one that broke: both sides parse to +infinity, and infinity - infinity is NaN.
        Assert.IsTrue(Equal("9e424042", "9e424042"));
        Assert.IsTrue(Equal("850e0471", "850e0471"));
    }

    [TestMethod]
    public void NumericLookingStringsAreStillStrings()
    {
        // Equal as numbers, different as strings. GameMaker says different.
        Assert.IsFalse(Equal("1e2", "100"));
        Assert.IsFalse(Equal("1.0", "1"));
        Assert.IsFalse(Equal("9e424042", "9e424043"), "both are infinity as doubles");
    }

    [TestMethod]
    public void DifferentStringsAreStillDifferent()
    {
        Assert.IsFalse(Equal("hello", "world"));
        Assert.IsFalse(Equal("9e424042", "d47c4b13"));
    }
}
