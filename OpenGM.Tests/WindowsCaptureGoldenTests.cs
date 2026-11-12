using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// Values captured verbatim from <b>GameMaker Studio 1.4.9999 on Windows</b> (a probe project,
/// rmProbe). Every assertion here is a number the real runtime printed, not one derived from
/// documentation or from OpenGMK -- both of which were wrong about several of these.
///
/// Two of the fixes below were pre-existing bugs that no amount of reading would have found, and
/// one was a regression introduced by "correcting" code that had been right all along
/// (date_get_week). That asymmetry is the argument for capturing rather than inferring.
/// </summary>
[TestClass]
public class WindowsCaptureGoldenTests
{
    private static double Round(double v) => MathFunctions.round([v]).Conv<double>();

    /*
     * round() -- "R round_half", "R round_eps", "R round_big"
     */

    [TestMethod]
    public void RoundUsesBankersRounding()
    {
        // Captured: "R round_half: 0 2 2 4 0 -2 -2"
        Assert.AreEqual(0.0, Round(0.5));
        Assert.AreEqual(2.0, Round(1.5));
        Assert.AreEqual(2.0, Round(2.5));
        Assert.AreEqual(4.0, Round(3.5));
        Assert.AreEqual(0.0, Round(-0.5));
        Assert.AreEqual(-2.0, Round(-1.5));
        Assert.AreEqual(-2.0, Round(-2.5));

        // round(-0.5) is -0.0 in IEEE and GM prints it as "0", so the sign has to be normalised or
        // string() renders "-0".
        Assert.IsFalse(double.IsNegative(Round(-0.5)), "negative zero leaked out of round()");
    }

    [TestMethod]
    public void RoundKeepsDoublePrecision()
    {
        // Captured: "R round_eps: 1 0 3 2".
        //
        // This is the assertion that catches the old `(float)num` narrowing. As singles,
        // 0.5000000001 and 2.5000000001 are exactly 0.5f and 2.5f, which banker's rounding then
        // sends to 0 and 2 -- the opposite answers.
        Assert.AreEqual(1.0, Round(0.5000000001));
        Assert.AreEqual(0.0, Round(0.4999999999));
        Assert.AreEqual(3.0, Round(2.5000000001));
        Assert.AreEqual(2.0, Round(2.4999999999));
    }

    [TestMethod]
    public void RoundReturnsARealNotAnInt()
    {
        // Captured: "R round_big: 2147483649 4294967296 -2147483650".
        // All three are outside int range; returning int wrapped them to nonsense.
        Assert.AreEqual(2147483649.0, Round(2147483648.7));
        Assert.AreEqual(4294967296.0, Round(4294967296.5)); // .5 -> even
        Assert.AreEqual(-2147483650.0, Round(-2147483649.7));
    }

    /*
     * clamp() -- "R misc: ... clamp=0"
     */

    [TestMethod]
    public void ClampLetsTheMaxWinWhenBoundsAreInverted()
    {
        // Captured: clamp(5, 10, 0) == 0. The old implementation tested `val <= min` first and
        // returned min, i.e. 10 -- the opposite answer whenever a game passes its bounds backwards.
        Assert.AreEqual(0.0, MathFunctions.clamp([5, 10, 0]).Conv<double>());

        // Ordinary, non-inverted behaviour is unchanged.
        Assert.AreEqual(5.0, MathFunctions.clamp([5, 0, 10]).Conv<double>());
        Assert.AreEqual(0.0, MathFunctions.clamp([-5, 0, 10]).Conv<double>());
        Assert.AreEqual(10.0, MathFunctions.clamp([50, 0, 10]).Conv<double>());
    }

    /*
     * Assorted maths -- "R modulo", "R div", "R frac", "R misc"
     */


    [TestMethod]
    public void SignOfZeroIsZero()
    {
        // Captured: "sign0=0 sign-3=-1". Worth pinning because CustomMath.Sign next door
        // deliberately returns 1 for zero (it mirrors Unity), and reaching for that one here would
        // be an easy and silent mistake.
        Assert.AreEqual(0, MathFunctions.sign([0]).Conv<int>());
        Assert.AreEqual(-1, MathFunctions.sign([-3]).Conv<int>());
        Assert.AreEqual(1, MathFunctions.sign([3]).Conv<int>());

        // Captured: "R frac: -0.2500" -- frac keeps the sign of its argument.
        Assert.AreEqual(-0.25, MathFunctions.frac([-2.25]).Conv<double>(), 1e-12);
    }

    /*
     * The RNG. Identified from a 600-draw capture: GameMaker uses WELL512a, and GMS 1.4 differs
     * from 2.x only in how the seed is loaded into the 16-word state -- 1.4 keeps the running LCG
     * value signed (so the shift sign-extends) and masks it to 31 bits before feeding it back.
     * OpenGM had the 2.x seeding plus a wrong float conversion, so every seeded run diverged.
     *
     * Anything deliberately seeded -- a daily challenge, a replay, a seeded run, a shuffled deck --
     * depends on these matching exactly, and nothing about that failure would look like an RNG
     * problem from the outside.
     */


    [TestMethod]
    public void SeededRandomMatchesTheRealRunner()
    {
        CompatFlags.LegacyRandomSeeding = true;
        MathFunctions.random_set_seed([12345]);

        // Printed by GMS 1.4.9999 to 15 decimals.
        Assert.AreEqual(0.399504934903234, MathFunctions.random([1]).Conv<double>(), 1e-15);
        Assert.AreEqual(0.125807473435998, MathFunctions.random([1]).Conv<double>(), 1e-15);
        Assert.AreEqual(0.507937133545056, MathFunctions.random([1]).Conv<double>(), 1e-15);

        Assert.AreEqual(12345, MathFunctions.random_get_seed([]).Conv<int>());
    }

    [TestMethod]
    public void SeededIrandomMatchesTheRealRunner()
    {
        // Note this is NOT the float sequence above scaled: irandom consumes the raw integer draw
        // rather than the [0,1) one, so 0.12580747 * 101 = 12 is the wrong prediction and 82 is the
        // right answer.
        MathFunctions.random_set_seed([12345]);
        Assert.AreEqual(40, MathFunctions.irandom([100]).Conv<int>());
        Assert.AreEqual(82, MathFunctions.irandom([100]).Conv<int>());
        Assert.AreEqual(57, MathFunctions.irandom([100]).Conv<int>());
    }

    [TestMethod]
    public void SeededRandomRangeMatchesTheRealRunner()
    {
        // Captured: "N rrange: 13.99504935 irange=20 choose=4", i.e. random_range shares the same
        // draw random() would have made: 10 + 0.39950493 * 10.
        MathFunctions.random_set_seed([12345]);
        Assert.AreEqual(13.99504935, MathFunctions.random_range([10, 20]).Conv<double>(), 5e-8);
    }

    /*
     * Colour packing -- "X colors", "X color_get"
     */

    [TestMethod]
    public void ColoursArePackedBgr()
    {
        Assert.AreEqual(197121, GraphicFunctions.make_color_rgb([1, 2, 3]).Conv<int>()); // 0x030201
        Assert.AreEqual(8388736, GraphicFunctions.merge_colour([255, 16711680, 0.5]).Conv<int>()); // 0x800080
        Assert.AreEqual(8322871, GraphicFunctions.make_color_hsv([100, 200, 255]).Conv<int>()); // 0x7EFF37

        // c_orange is 0x40A0FF, so red is the *low* byte.
        Assert.AreEqual(255, GraphicFunctions.color_get_red([4235519]).Conv<int>());
        Assert.AreEqual(160, GraphicFunctions.color_get_green([4235519]).Conv<int>());
        Assert.AreEqual(64, GraphicFunctions.color_get_blue([4235519]).Conv<int>());
        Assert.AreEqual(21, GraphicFunctions.color_get_hue([4235519]).Conv<int>());
        Assert.AreEqual(191, GraphicFunctions.color_get_saturation([4235519]).Conv<int>());
        Assert.AreEqual(255, GraphicFunctions.color_get_value([4235519]).Conv<int>());
    }

    /*
     * Strings -- "X strings", "X str2", "X str3"
     */

    [TestMethod]
    public void StringIndexingIsOneBasedAndClampsAtBothEnds()
    {
        // Captured: char_at0=[a] char_at9=[]. Index 0 is clamped up to the first character rather
        // than returning "" the way an out-of-range index past the end does.
        Assert.AreEqual("a", MathFunctions.string_char_at(["abc", 0]).Conv<string>());
        Assert.AreEqual("a", MathFunctions.string_char_at(["abc", 1]).Conv<string>());
        Assert.AreEqual("", MathFunctions.string_char_at(["abc", 9]).Conv<string>());

        Assert.AreEqual(2, MathFunctions.string_pos(["b", "abcb"]).Conv<int>());
        Assert.AreEqual("bcd", MathFunctions.string_copy(["abcdef", 2, 3]).Conv<string>());
        Assert.AreEqual("aXYbcd", MathFunctions.string_insert(["XY", "abcd", 2]).Conv<string>());
        Assert.AreEqual("aef", MathFunctions.string_delete(["abcdef", 2, 3]).Conv<string>());
        Assert.AreEqual("ababab", MathFunctions.string_repeat(["ab", 3]).Conv<string>());
        Assert.AreEqual("a--b--c", MathFunctions.string_replace_all(["aXbXc", "X", "--"]).Conv<string>());
        Assert.AreEqual("ab", MathFunctions.string_letters(["a1b2!"]).Conv<string>());
        Assert.AreEqual("12", MathFunctions.string_digits(["a1b2!"]).Conv<string>());
        Assert.AreEqual("a1b2", MathFunctions.string_lettersdigits(["a1b2!"]).Conv<string>());
    }
}

/// <summary>
/// <c>string(real)</c>'s runtime formatting, which an earlier capture appeared to contradict.
///
/// That capture printed <c>string(2.5)</c> as "2.5", suggesting OpenGM's two-decimal formatting was
/// wrong -- but the same run printed a stored real -2.5 as "-2.50" when read back out of a
/// ds_list. Two different formatters in one run is the signature of compile-time constant folding,
/// so rather than change anything, the probe was rewritten to force runtime evaluation through
/// <c>real()</c>. It came back:
///
///   SR runtime: [2.50] [0.33] [0.30] [2] [123456789012] [0.00] [-2.50]
///
/// i.e. non-integers get exactly two decimals and integers get none, which is what OpenGM already
/// did. The literals in the first capture were folded by the compiler with a different formatter.
/// Pinned here so nobody "fixes" it back.
/// </summary>
[TestClass]
public class StringOfRealTests
{
    private static string S(double v) => v.Conv<string>();

    [TestMethod]
    public void NonIntegersGetTwoDecimalsAndIntegersGetNone()
    {
        Assert.AreEqual("2.50", S(2.5));
        Assert.AreEqual("-2.50", S(-2.5));
        Assert.AreEqual("0.33", S(1.0 / 3.0));
        Assert.AreEqual("0.30", S(0.1 + 0.2));
        Assert.AreEqual("0.00", S(0.000001));

        Assert.AreEqual("2", S(2));
        Assert.AreEqual("123456789012", S(123456789012.0));
    }
}

/// <summary>
/// The RNG, pinned against the raw draws rather than one or two convenient values.
///
/// The identification is worth recording because two of the three errors masked each other. The
/// float conversion divided the low 31 bits by int.MaxValue instead of dividing all 32 bits by
/// 2^32, which halves every value -- so when an earlier pass reverse-engineered raw integers from
/// the printed floats, every bit was shifted by one and the WELL512 step function looked like it
/// didn't match. Only after checking which divisor made the raw values land on integers did the
/// step function verify, leaving the seeding as the single real difference.
/// </summary>
[TestClass]
public class RandomGeneratorTests
{
    private bool _saved;

    [TestInitialize]
    public void SetUp()
    {
        _saved = CompatFlags.LegacyRandomSeeding;
        CompatFlags.LegacyRandomSeeding = true;
    }

    [TestCleanup]
    public void TearDown() => CompatFlags.LegacyRandomSeeding = _saved;

    [TestMethod]
    public void SeedingMatchesTheCapturedStream()
    {
        // Seed 12345, first six raw draws (float x 2^32 from the capture).
        GMRandom.InitRandom(12345);
        uint[] expected = [1715860630, 540338984, 2181573377, 2053762291, 1826726686, 179941711];
        foreach (var want in expected)
        {
            Assert.AreEqual(want, GMRandom.YYRandom());
        }
    }

    [TestMethod]
    public void OtherSeedsMatchToo()
    {
        GMRandom.InitRandom(0);
        Assert.AreEqual(3451936207u, GMRandom.YYRandom());

        GMRandom.InitRandom(1);
        Assert.AreEqual(392972662u, GMRandom.YYRandom());

        // random_set_seed(-1) stores 0xFFFFFFFF -- the seed is unsigned.
        GMRandom.InitRandom(4294967295);
        Assert.AreEqual(0.590345776872709, GMRandom.fYYRandom(), 1e-15);
    }

    [TestMethod]
    public void TheStepFunctionIsSharedWithGms2OnlyTheSeedingDiffers()
    {
        // Same 16-word state, both flag settings -> identical output. This is what makes the
        // version gate safe: it touches the seeding and nothing else.
        CompatFlags.LegacyRandomSeeding = true;
        GMRandom.InitRandom(12345);
        var legacyState = (uint[])GMRandom.State.Clone();
        var legacyIndex = GMRandom.Index;

        CompatFlags.LegacyRandomSeeding = false;
        GMRandom.InitRandom(12345);
        var modernState = (uint[])GMRandom.State.Clone();

        CollectionAssert.AreNotEqual(legacyState, modernState, "the seeding must actually differ");

        // Drive the modern path from the legacy state: the draws must match the legacy ones.
        GMRandom.State = legacyState;
        GMRandom.Index = legacyIndex;
        Assert.AreEqual(1715860630u, GMRandom.YYRandom());
    }

    [TestMethod]
    public void IrandomConsumesTwoDrawsAndRandomConsumesOne()
    {
        // Measured: after irandom(), the next random() is the *third* draw from the seed, but after
        // random_range() or choose() it is the second. The HTML runner takes a second draw purely to
        // keep state parity with the C++ one, and a game mixing the two desyncs without it.
        GMRandom.InitRandom(777);
        var first = GMRandom.fYYRandom();
        var second = GMRandom.fYYRandom();
        var third = GMRandom.fYYRandom();

        GMRandom.InitRandom(777);
        MathFunctions.irandom([100]);
        Assert.AreEqual(third, MathFunctions.random([1]).Conv<double>(), 1e-15,
            "irandom should have consumed two draws");

        GMRandom.InitRandom(777);
        MathFunctions.random_range([0, 1]);
        Assert.AreEqual(second, MathFunctions.random([1]).Conv<double>(), 1e-15,
            "random_range should have consumed one draw");

        _ = first;
    }
}
