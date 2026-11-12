using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// The builtins added after I Wanna Spook Jam's Raynius boss died on
/// <c>Can't resolve script arctan</c> mid-fight (gml_Script_scrRay_Lightning), plus everything of
/// the same shape found by diffing OpenGM's registered <c>[GMLFunction]</c> names against OpenGMK's
/// <c>gml/mappings.rs</c> reference list.
///
/// An unresolved call doesn't degrade -- it aborts the whole event, so a one-line pure function
/// nobody implemented takes a boss fight with it. Each of these is exercised through real bytecode
/// (the same <c>call.i name(argc=N)</c> path that failed) rather than by calling the C# directly,
/// so the test covers the registration as well as the maths.
/// </summary>
[TestClass]
public class MissingBuiltinsTests
{
    [TestInitialize]
    public void SetUp() => ScriptResolver.InitGMLFunctions();

    /// <summary>Calls <paramref name="name"/> with the given literal doubles, via real bytecode.</summary>
    private static object? Call(string name, params double[] args)
    {
        var asm = ":[0]\n";

        // GML pushes a call's arguments in REVERSE order (last one first) -- see any real compiled
        // `array_set(arr, 0, 99)`. Getting this backwards is invisible for a symmetric function and
        // silently wrong for arctan2/darctan2, which is half of what these tests are here to pin.
        foreach (var arg in args.Reverse())
        {
            asm += $"push.d {arg.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nconv.d.v\n";
        }

        asm += $"call.i {name}(argc={args.Length})\nret.v";

        return TestUtils.ExecuteScript($"{name}({string.Join(", ", args)})", asm);
    }

    private static void AssertClose(double expected, object? actual, string message)
        => Assert.AreEqual(expected, actual.Conv<double>(), 1e-10, message);

    [TestMethod]
    public void Arctan()
    {
        AssertClose(0, Call("arctan", 0), "arctan(0)");
        AssertClose(Math.PI / 4, Call("arctan", 1), "arctan(1) is an eighth turn, in radians");
        AssertClose(-Math.PI / 4, Call("arctan", -1), "arctan(-1)");
    }

    [TestMethod]
    public void Arctan2TakesYThenX()
    {
        // The argument order is the trap: arctan2(y, x), like C's atan2. Swapping them still
        // returns a plausible-looking angle, which is exactly why it needs pinning down.
        AssertClose(Math.PI / 2, Call("arctan2", 1, 0), "straight up");
        AssertClose(0, Call("arctan2", 0, 1), "straight right");
        AssertClose(Math.PI, Call("arctan2", 0, -1), "straight left");
        AssertClose(3 * Math.PI / 4, Call("arctan2", 1, -1), "up and to the left");
    }

    [TestMethod]
    public void ExpAndLn()
    {
        AssertClose(1, Call("exp", 0), "exp(0)");
        AssertClose(Math.E, Call("exp", 1), "exp(1)");
        AssertClose(0, Call("ln", 1), "ln(1)");
        AssertClose(1, Call("ln", Math.E), "ln(e)");
        AssertClose(3, Call("ln", Math.Exp(3)), "ln undoes exp");
    }

    [TestMethod]
    public void DegreeTrigReturnsDegrees()
    {
        AssertClose(1, Call("dtan", 45), "dtan(45)");
        AssertClose(30, Call("darcsin", 0.5), "darcsin(0.5)");
        AssertClose(60, Call("darccos", 0.5), "darccos(0.5)");
        AssertClose(45, Call("darctan", 1), "darctan(1)");
        AssertClose(135, Call("darctan2", 1, -1), "darctan2(y, x), same order as arctan2");
    }

    [TestMethod]
    public void Min3AndMax3()
    {
        Assert.AreEqual(2d, Call("min3", 7, 2, 5).Conv<double>());
        Assert.AreEqual(7d, Call("max3", 7, 2, 5).Conv<double>());
        Assert.AreEqual(-5d, Call("min3", -5, -2, -3).Conv<double>());
    }

    [TestMethod]
    public void DotProductIsNotNormalised()
    {
        // Studio splits these into two functions; GM8's single dot_product normalised its inputs,
        // so taking OpenGMK at face value here would have been wrong.
        Assert.AreEqual(11d, Call("dot_product", 3, 4, 1, 2).Conv<double>(), "3*1 + 4*2");
        Assert.AreEqual(0d, Call("dot_product", 1, 0, 0, 1).Conv<double>(), "perpendicular");
        Assert.AreEqual(32d, Call("dot_product_3d", 1, 2, 3, 4, 5, 6).Conv<double>(), "4 + 10 + 18");
    }

    [TestMethod]
    public void NormalisedDotProductIsTheCosineOfTheAngle()
    {
        AssertClose(1, Call("dot_product_normalised", 3, 0, 7, 0), "same direction, any lengths");
        AssertClose(-1, Call("dot_product_normalised", 3, 0, -7, 0), "opposite");
        AssertClose(0, Call("dot_product_normalised", 0, 5, 5, 0), "perpendicular");
        AssertClose(Math.Sqrt(0.5), Call("dot_product_normalised", 1, 0, 1, 1), "45 degrees apart");
        AssertClose(1, Call("dot_product_3d_normalised", 0, 0, 2, 0, 0, 9), "same direction in 3d");

        // A zero-length vector has no direction, so this returns 0 rather than the NaN a plain
        // divide by its length would give -- a NaN here silently poisons the caller's own maths.
        Assert.AreEqual(0d, Call("dot_product_normalised", 0, 0, 1, 1).Conv<double>());
    }

    [TestMethod]
    public void PointDistance3d()
    {
        Assert.AreEqual(5d, Call("point_distance_3d", 0, 0, 0, 3, 4, 0).Conv<double>());
        Assert.AreEqual(7d, Call("point_distance_3d", 1, 2, 3, 3, 5, 9).Conv<double>(), "2-3-6 triple");
    }

    [TestMethod]
    public void StringBytes()
    {
        var asm = """
                  :[0]
                  push.s "hi"@0
                  conv.s.v
                  call.i string_byte_length(argc=1)
                  ret.v
                  """;
        Assert.AreEqual(2, TestUtils.ExecuteScript("string_byte_length(\"hi\")", asm).Conv<int>());

        // Multi-byte on purpose: this is the whole reason the function is separate from
        // string_length, whose answer for this string is 1.
        asm = """
              :[0]
              push.s "é"@0
              conv.s.v
              call.i string_byte_length(argc=1)
              ret.v
              """;
        Assert.AreEqual(2, TestUtils.ExecuteScript("string_byte_length(\"é\")", asm).Conv<int>(),
            "UTF-8 bytes, not characters");

        asm = """
              :[0]
              pushi.i 2
              conv.i.v
              push.s "ABC"@0
              conv.s.v
              call.i string_byte_at(argc=2)
              ret.v
              """;
        Assert.AreEqual(66, TestUtils.ExecuteScript("string_byte_at(\"ABC\", 2)", asm).Conv<int>(), "'B'");

        asm = """
              :[0]
              pushi.i 9
              conv.i.v
              push.s "ABC"@0
              conv.s.v
              call.i string_byte_at(argc=2)
              ret.v
              """;
        Assert.AreEqual(0, TestUtils.ExecuteScript("string_byte_at(\"ABC\", 9)", asm).Conv<int>(),
            "past the end reads 0, not whatever memory happens to be there");
    }

    [TestMethod]
    public void AnsiChar()
    {
        Assert.AreEqual("A", Call("ansi_char", 65).Conv<string>());
        Assert.AreEqual(" ", Call("ansi_char", 32).Conv<string>());
    }

    [TestMethod]
    public void PointInCircle()
    {
        Assert.IsTrue(Call("point_in_circle", 0, 0, 0, 0, 5).Conv<bool>(), "dead centre");
        Assert.IsTrue(Call("point_in_circle", 3, 4, 0, 0, 5).Conv<bool>(), "exactly on the edge counts");
        Assert.IsFalse(Call("point_in_circle", 3, 4, 0, 0, 4.9).Conv<bool>(), "just outside");
        Assert.IsTrue(Call("point_in_circle", 12, 9, 10, 10, 3).Conv<bool>(), "off-origin circle");
    }

    [TestMethod]
    public void PointInTriangle()
    {
        // Same triangle wound both ways -- the sign test has to agree either way round.
        Assert.IsTrue(Call("point_in_triangle", 2, 1, 0, 0, 10, 0, 0, 10).Conv<bool>());
        Assert.IsTrue(Call("point_in_triangle", 2, 1, 0, 10, 10, 0, 0, 0).Conv<bool>(), "reverse winding");
        Assert.IsFalse(Call("point_in_triangle", 9, 9, 0, 0, 10, 0, 0, 10).Conv<bool>(), "past the hypotenuse");
        Assert.IsFalse(Call("point_in_triangle", -1, 1, 0, 0, 10, 0, 0, 10).Conv<bool>(), "outside entirely");
    }
}
