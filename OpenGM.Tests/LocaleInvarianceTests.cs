using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// Numbers must survive a round trip through a string on a machine whose locale writes 1,5.
///
/// GML has no notion of locale: <c>string(1.5)</c> is "1.50" wherever GameMaker runs, and
/// <c>real("1.50")</c> reads it back. .NET routes both through the current culture unless told
/// otherwise, and the failure is silent and expensive -- a French machine produced
/// <c>Couldn't read "1534,4650000000022" as a real!</c> from a mod's saved position, and the game
/// simply lost it.
///
/// Each conversion was fixed as it was found, which is whack-a-mole with data loss as the prize.
/// These pin the behaviour rather than the fix, so a new builtin that formats a number the lazy way
/// fails here instead of on somebody's save.
/// </summary>
[TestClass]
public class LocaleInvarianceTests
{
    private static readonly CultureInfo Comma = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Runs <paramref name="body"/> as if the machine were French.</summary>
    private static void AsCommaLocale(Action body)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = Comma;
        try
        {
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <remarks>
    /// Asserted as "no comma" rather than against exact text, because the exact text is a separate
    /// question this is not the place to settle: OpenGM answers <c>string(1.5)</c> with "1.5" while
    /// GameMaker answers "1.50". Pinning the digits here would quietly freeze that difference in as
    /// though it were intended.
    /// </remarks>
    [TestMethod]
    public void StringOfARealUsesAPoint()
    {
        AsCommaLocale(() =>
        {
            foreach (var value in new[] { 1.5, 1534.4650000000022, 1534.0, -0.25 })
            {
                var written = (string)MathFunctions.@string([value])!;
                StringAssert.DoesNotMatch(
                    written,
                    new Regex(","),
                    $"string({value}) produced {written} on a comma locale"
                );
            }
        });
    }

    [TestMethod]
    public void StringFormatUsesAPoint()
    {
        AsCommaLocale(() =>
            Assert.AreEqual("1.50000000", MathFunctions.string_format([1.5, 1, 8]))
        );
    }

    [TestMethod]
    public void RealReadsAPointBack()
    {
        AsCommaLocale(() =>
        {
            Assert.AreEqual(1.5, "1.5".Conv<double>());
            Assert.AreEqual(1534.4650000000022, "1534.4650000000022".Conv<double>());
        });
    }

    /// <summary>The whole point: what one side writes, the other reads.</summary>
    [TestMethod]
    public void ANumberSurvivesTheRoundTrip()
    {
        AsCommaLocale(() =>
        {
            var written = (string)MathFunctions.@string([1534.46])!;
            Assert.AreEqual(1534.46, written.Conv<double>());
        });
    }

    /// <summary>
    /// And the engine does not leave it to each conversion to remember. Touching <c>Entry</c> --
    /// which every launch does long before any GML runs -- installs the invariant culture, so a
    /// path nobody has audited still formats numbers the same way everywhere.
    /// </summary>
    [TestMethod]
    public void LoadingTheEngineInstallsAnInvariantCulture()
    {
        _ = Entry.DisplayCulture; // touches Entry, running its static constructor

        // The default every thread picks up, which is what a wasm runtime seeded from the browser's
        // locale would otherwise hand the game. Asserted rather than `CurrentCulture`, which a
        // caller (this test's own helper included) can always override for itself.
        Assert.AreEqual(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
    }
}
