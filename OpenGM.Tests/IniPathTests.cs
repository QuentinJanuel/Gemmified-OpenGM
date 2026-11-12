using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using System.Globalization;

namespace OpenGM.Tests;

/// <summary>
/// That <c>ini_open</c>/<c>ini_close</c> put a file where every other file builtin would.
/// </summary>
/// <remarks>
/// <para>
/// The ini family kept its own path handling -- <c>Path.GetFileName</c>, a bare basename -- long
/// after the <c>file_text_*</c> family was moved onto <c>PersistedName</c>. Its own summary names
/// the two things that go wrong with a basename, and the ini family had both.
/// </para>
/// <para>
/// It is not an internal detail. A real GameMaker runner resolves a relative path into the game's
/// save area (<c>%LOCALAPPDATA%\&lt;Name&gt;</c>), so it writes <c>config.ini</c> there; a bare
/// basename files it next to the game instead. The two locations never meet, which is why a game's
/// settings -- volume, keybindings -- did not follow a player between the emulator and a real
/// install even once its save file did.
/// </para>
/// </remarks>
[TestClass]
public class IniPathTests
{
    [TestInitialize]
    public void SetUp() => Cleanup();

    [TestCleanup]
    public void TearDown() => Cleanup();

    private static void Cleanup()
    {
        foreach (var name in new[] { "config.ini", @"Data\config.ini", @"Backup\config.ini" })
        {
            FileFunctions.file_delete([name]);
        }
    }

    [TestMethod]
    public void AnIniLandsWhereTheOtherFileBuiltinsLookForIt()
    {
        // Pathed deliberately. A top-level name cannot tell the two implementations apart on the
        // desktop resolver -- it resolves to itself, which is also what the basename produced. The
        // difference the browser build actually suffered (a relative path re-rooted into the save
        // area) is only reachable under #if BROWSER, so this asserts the property that *is* visible
        // here: the resolver is the shared one, and it keeps the directory.
        FileFunctions.ini_open([@"Data\config.ini"]);
        FileFunctions.ini_write_real(["Settings", "Volume_level", 100]);
        FileFunctions.ini_close([]);

        Assert.IsTrue(FileFunctions.file_exists([@"Data\config.ini"]).Conv<bool>(),
            "written through the ini family, invisible to the rest of the file API");
    }

    [TestMethod]
    public void AnIniRoundTripsThroughItsOwnFamily()
    {
        FileFunctions.ini_open([@"Data\config.ini"]);
        FileFunctions.ini_write_real(["Settings", "Volume_level", 100]);
        FileFunctions.ini_write_string(["Controls", "Jump", "16"]);
        FileFunctions.ini_close([]);

        FileFunctions.ini_open([@"Data\config.ini"]);
        var volume = FileFunctions.ini_read_real(["Settings", "Volume_level", 0]).Conv<double>();
        var jump = FileFunctions.ini_read_string(["Controls", "Jump", ""]).Conv<string>();
        FileFunctions.ini_close([]);

        Assert.AreEqual(100.0, volume);
        Assert.AreEqual("16", jump);
    }

    [TestMethod]
    public void TwoInisWithTheSameNameInDifferentFoldersStaySeparate()
    {
        // The other half of the basename bug: `Save\game1` and `Backup\game1` collapsed into one
        // stored file, so writing either overwrote the other.
        FileFunctions.ini_open([@"Data\config.ini"]);
        FileFunctions.ini_write_real(["Settings", "Volume_level", 100]);
        FileFunctions.ini_close([]);

        FileFunctions.ini_open([@"Backup\config.ini"]);
        FileFunctions.ini_write_real(["Settings", "Volume_level", 20]);
        FileFunctions.ini_close([]);

        FileFunctions.ini_open([@"Data\config.ini"]);
        var kept = FileFunctions.ini_read_real(["Settings", "Volume_level", 0]).Conv<double>();
        FileFunctions.ini_close([]);

        Assert.AreEqual(100.0, kept, "writing one folder's ini overwrote another's");
        Assert.IsFalse(FileFunctions.file_exists(["config.ini"]).Conv<bool>(),
            "a pathed ini must not also appear at the top level");
    }

    /// <summary>
    /// A real runner writes every ini real as <c>&lt;digits&gt;.000000</c>. Under a locale where
    /// "." groups digits instead of separating the fraction, parsing that with the current culture
    /// multiplies it by a million -- so a game's saved volume came back as 1000000, which the game
    /// clamps to its maximum. The settings appeared to reset every time they arrived from a real
    /// install, while the reverse direction worked, because what this engine wrote had no "." in it.
    /// </summary>
    [TestMethod]
    public void ARealRunnersNumbersReadTheSameUnderAnyLocale()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            FileFunctions.ini_open_from_string([
                "[Settings]\r\nVolume_level=\"1.000000\"\r\nJump=\"32769.000000\"\r\n"]);
            var volume = FileFunctions.ini_read_real(["Settings", "Volume_level", 0]).Conv<double>();
            var jump = FileFunctions.ini_read_real(["Settings", "Jump", 0]).Conv<double>();
            FileFunctions.ini_close([]);

            Assert.AreEqual(1.0, volume, "read as one million under a comma-decimal locale");
            Assert.AreEqual(32769.0, jump);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// And the write side of the same thing: a decimal comma is unreadable by a real install.
    /// </summary>
    [TestMethod]
    public void NumbersAreWrittenTheWayARealRunnerWritesThem()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            FileFunctions.ini_open_from_string(["[Settings]\r\n"]);
            FileFunctions.ini_write_real(["Settings", "Volume_level", 1]);
            FileFunctions.ini_write_real(["Settings", "Half", 1.5]);
            var text = FileFunctions.ini_close([]).Conv<string>();

            StringAssert.Contains(text, "Volume_level=\"1.000000\"");
            StringAssert.Contains(text, "Half=\"1.500000\"");
            Assert.IsFalse(text.Contains(','), "a decimal comma would be unreadable by a real install");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void OpeningAnIniThatIsNotThereStartsAnEmptyOne()
    {
        // A game's first run: ini_open on a missing file is not an error, it just has no keys.
        FileFunctions.ini_open(["config.ini"]);
        var volume = FileFunctions.ini_read_real(["Settings", "Volume_level", 42]).Conv<double>();
        FileFunctions.ini_close([]);

        Assert.AreEqual(42.0, volume, "the default should have come back");
    }
}
