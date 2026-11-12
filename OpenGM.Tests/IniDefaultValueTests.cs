using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// What the <c>ini_*_real</c> pair does with a value that isn't a number.
///
/// GML is loosely typed and a runner does not type-check this argument, so the default comes back
/// as whatever the game passed. I Wanna Be The Ocean Princess reads its music folder with
/// <c>ini_read_real("Settings", "MusicPath", working_directory + "music/")</c> -- meaning to call
/// ini_read_string -- and converting that path to a double up front threw before the key was even
/// looked up, aborting the whole of its scrLoadConfig. Its scrSaveConfig writes the same value
/// back with ini_write_real, which threw for the same reason.
/// </summary>
[TestClass]
public class IniDefaultValueTests
{
    [TestInitialize]
    public void SetUp() => FileFunctions.file_delete(["defaults.ini"]);

    [TestCleanup]
    public void TearDown()
    {
        FileFunctions.ini_close([]);
        FileFunctions.file_delete(["defaults.ini"]);
    }

    [TestMethod]
    public void AMissingKeyGivesBackTheDefaultEvenWhenItIsAString()
    {
        FileFunctions.ini_open(["defaults.ini"]);

        var result = FileFunctions.ini_read_real(["Settings", "MusicPath", "C:\\game\\music/"]);

        Assert.AreEqual("C:\\game\\music/", result);
    }

    [TestMethod]
    public void AMissingSectionGivesBackTheDefaultToo()
    {
        FileFunctions.ini_open(["defaults.ini"]);
        FileFunctions.ini_write_real(["Other", "Something", 1d]);

        Assert.AreEqual("fallback", FileFunctions.ini_read_real(["Settings", "MusicPath", "fallback"]));
    }

    /// <summary>A number default is still a number, and a real value still reads as one.</summary>
    [TestMethod]
    public void NumbersAreUnaffected()
    {
        FileFunctions.ini_open(["defaults.ini"]);
        FileFunctions.ini_write_real(["Settings", "Volume", 7d]);

        Assert.AreEqual(7d, FileFunctions.ini_read_real(["Settings", "Volume", 0d]).Conv<double>());
        Assert.AreEqual(3d, FileFunctions.ini_read_real(["Settings", "Missing", 3d]).Conv<double>());
    }

    /// <summary>
    /// A key that is present but not a number falls back to the default, which is also returned
    /// as-is.
    /// </summary>
    [TestMethod]
    public void AKeyHoldingSomethingUnparseableFallsBackToTheDefault()
    {
        FileFunctions.ini_open(["defaults.ini"]);
        FileFunctions.ini_write_string(["Settings", "MusicPath", "music/"]);

        Assert.AreEqual("fallback", FileFunctions.ini_read_real(["Settings", "MusicPath", "fallback"]));
    }

    /// <summary>
    /// The write half of the same pair: an ini holds text, so a string is stored as it stands.
    /// Together with the read above, that makes the game's save/load of its music folder a round
    /// trip rather than two aborted scripts.
    /// </summary>
    [TestMethod]
    public void WritingAStringWithIniWriteRealStoresItVerbatim()
    {
        FileFunctions.ini_open(["defaults.ini"]);
        FileFunctions.ini_write_real(["Settings", "MusicPath", "C:\\game\\music/"]);

        Assert.AreEqual("C:\\game\\music/", FileFunctions.ini_read_string(["Settings", "MusicPath", ""]));
    }

    /// <summary>A real still round-trips as a real, in the runner's own `<digits>.000000` form.</summary>
    [TestMethod]
    public void WritingANumberIsUnchanged()
    {
        FileFunctions.ini_open(["defaults.ini"]);
        FileFunctions.ini_write_real(["Settings", "Volume", 1d]);

        Assert.AreEqual("1.000000", FileFunctions.ini_read_string(["Settings", "Volume", ""]));
        Assert.AreEqual(1d, FileFunctions.ini_read_real(["Settings", "Volume", 0d]).Conv<double>());
    }
}
