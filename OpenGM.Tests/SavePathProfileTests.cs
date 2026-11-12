using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenGM.Tests;

/// <summary>
/// The GEMS save-path rules for gms1/gms2 games, as the browser build resolves writes: the rows of
/// the GEMS save-path conformance table, plus the fallback that keeps saves the legacy mapping wrote.
/// </summary>
[TestClass]
public class SavePathProfileTests
{
    private string _save = "";
    private string _file = "";

    [TestInitialize]
    public void SetUp()
    {
        (_save, _file) = (LoadSave.g_pSavePrePend, LoadSave.g_pFilePrePend);
        (LoadSave.g_pSavePrePend, LoadSave.g_pFilePrePend) = ("/appdata/Probe/", "/game");
    }

    [TestCleanup]
    public void TearDown() => (LoadSave.g_pSavePrePend, LoadSave.g_pFilePrePend) = (_save, _file);

    private static string? Resolve(string path, params string[] stored) =>
        LoadSave.ResolveSavePath(path, p => stored.Contains(p, StringComparer.OrdinalIgnoreCase));

    [TestMethod]
    [DataRow("p_rel.txt", "/appdata/Probe/p_rel.txt")]
    [DataRow("PSub/p_fwd.txt", "/appdata/Probe/PSub/p_fwd.txt")]
    [DataRow(@"..\p_up.txt", "/appdata/Probe/../p_up.txt")] // the VFS cleans this to appdata:p_up.txt
    [DataRow("/game/p_wd.txt", "/appdata/Probe/p_wd.txt")]
    [DataRow("/appdata/Probe/p_sid.txt", "/appdata/Probe/p_sid.txt")]
    [DataRow(@"C:\game\Data\save1", "/appdata/Probe/Data/save1")]
    [DataRow(@"C:\Users\player\AppData\Roaming\probe\x.txt", "/appdata/Probe/x.txt")]
    [DataRow(@"\p_rooted.txt", null)]
    [DataRow(@"C:\probe\p_drive.txt", null)]
    [DataRow(@"C:\Users\player\AppData\Local\Temp\p_tmp.txt", null)]
    [DataRow(@"C:\Users\player\AppData\Roaming\other\x.txt", null)]
    [DataRow("/appdata/Other/x.txt", null)]
    public void WritesFollowTheProfile(string path, string? expected) =>
        Assert.AreEqual(expected, Resolve(path));

    [TestMethod]
    public void ASaveTheOldMappingWroteIsStillFoundAndUpdated()
    {
        const string legacy = "/appdata/Probe/C:/probe/p_drive.txt";
        Assert.AreEqual(legacy, Resolve(@"C:\probe\p_drive.txt", legacy));
        // A new file beside an old one keeps going where its folder already is.
        Assert.AreEqual("/appdata/Probe/C:/probe/slot2", Resolve(@"C:\probe\slot2", "/appdata/Probe/C:/probe"));
        // The save area's own root is not such a folder: a rooted write stays refused.
        Assert.IsNull(Resolve(@"\p_rooted.txt", "/appdata/Probe"));
    }
}
