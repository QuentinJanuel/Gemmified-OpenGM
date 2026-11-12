using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// string_format(val, total, dec) -- `dec` decimal places, space-padded on the left to `total`
/// characters. Pinned to GameMaker Studio 1.4.9999 output (a probe project), which printed
/// "1.00000000" for string_format(1, 1, 8) and "3.99999833" for the float32 accumulation of 0.1 x40.
///
/// The old implementation was wrong two different ways, and both showed up in one capture:
///
///   - A whole number returned early with no decimals at all, so it printed "1" where GM prints
///     "1.00000000".
///   - Everything else took a *substring of the fractional part's decimal text*. That only works
///     while that text looks like "0.xxxx". For 4.000000000000002 the fractional part stringifies
///     as "1.7763568394002505E-15", whose characters 2..9 are "77635683" -- so it answered
///     "4.77635683" for a value that is 4 to fourteen decimal places.
///
/// The second one is the nastier bug: a tiny fractional part is exactly what accumulating a float
/// produces, so this misreported image_index, timers, positions, and anything else built by repeated
/// addition -- and it looks like a plausible number rather than an obvious error.
/// </summary>
[TestClass]
public class StringFormatTests
{
    private static string Format(double val, int total, int dec)
        => MathFunctions.string_format([val, total, dec]).Conv<string>();

    [TestMethod]
    public void MatchesTheWindowsCapture()
    {
        Assert.AreEqual("1.00000000", Format(1, 1, 8), "a whole number still gets its decimals");
        Assert.AreEqual("2.00000000", Format(2, 1, 8));
        Assert.AreEqual("0.00000000", Format(0, 1, 8));

        // What real GM printed for the float32 accumulation of 0.1 forty times.
        Assert.AreEqual("3.99999833", Format(3.9999983310699463, 1, 8));
    }

    [TestMethod]
    public void ATinyFractionalPartDoesntBecomeADifferentNumber()
    {
        // The 4.77635683 case, which is what sent me chasing a non-existent animation bug.
        Assert.AreEqual("4.00000000", Format(4.000000000000002, 1, 8));
        Assert.AreEqual("1.00000000", Format(0.9999999999999999, 1, 8));
    }

    [TestMethod]
    public void DecimalPlacesAreHonoured()
    {
        Assert.AreEqual("3.14", Format(Math.PI, 1, 2));
        Assert.AreEqual("3.1416", Format(Math.PI, 1, 4));
        Assert.AreEqual("3", Format(Math.PI, 1, 0), "no decimals means no decimal point either");
    }

    [TestMethod]
    public void TotalWidthPadsOnTheLeftWithSpaces()
    {
        Assert.AreEqual("  1.50", Format(1.5, 6, 2));
        Assert.AreEqual("1.50", Format(1.5, 4, 2), "exactly wide enough needs no padding");
        Assert.AreEqual("1.50", Format(1.5, 1, 2), "already wider than asked for, so left alone");
    }

    [TestMethod]
    public void NegativeValuesKeepTheirSign()
    {
        Assert.AreEqual("-1.00000000", Format(-1, 1, 8));
        Assert.AreEqual("-3.14", Format(-Math.PI, 1, 2));
    }
}
