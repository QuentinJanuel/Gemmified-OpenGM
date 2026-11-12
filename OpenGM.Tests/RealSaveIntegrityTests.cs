using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The one test that says whether a save can actually move between this engine and a real
/// GameMaker install: it replays a real game's own integrity check over a real save file.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is the literal contents of a save written by I Wanna Spook Jam (GMS 1.4.1757)
/// running under Wine. The game's load path, from its decompiled bytecode, is:
/// </para>
/// <code>
///     ds_map_delete(saveMap, "mapMd5");
///     var genMd5 = md5_string_unicode(ds_map_write(saveMap) + global.md5StrAdd);
///     if (mapMd5 != genMd5) { show_message("Save invalid!"); scrRestartGame(); }
/// </code>
/// <para>
/// <c>ds_map_write</c> serialises in iteration order, so passing this needs the format *and* the
/// order to match what the real runner produced. Insertion order does not: for these 33 keys the
/// two orders agree in almost no positions, which is why a byte-perfect save still came back as
/// "Save invalid!". Replaying the order the file was read in is what makes it work.
/// </para>
/// <para>
/// <c>md5StrAdd</c> is the game's own salt, left at the template's default.
/// </para>
/// </remarks>
[TestClass]
public class RealSaveIntegrityTests
{
    private const string Salt = "Put something here!!!";

    /// <summary>A real save file's contents, verbatim (base64-decoded from the file).</summary>
    private const string RealSave =
        "1:646966666963756C7479:,1:736176655365637265744974656D5B355D:,1:73617665436C65617254696D65:,1:70" +
        "726F67726573734D6170:,1:736176655365637265744974656D5B345D:,3:7361766553746167654E616D65:3132382" +
        "D5570,1:736176655365637265744974656D5B375D:,1:736176655365637265744974656D5B365D:,1:73617665426F" +
        "73734974656D5B385D:,1:70726F6772657373496E63:,1:7361766547726176:3FF0,1:5A656243726F7373436F6C6C" +
        "6563746564:,1:736176655365637265744974656D5B315D:,1:73617665436C656172446561746873:,1:7361766552" +
        "6F6F6D:402C,1:736176655365637265744974656D5B325D:,1:74696D654D6963726F:410F0328,1:73617665536563" +
        "7265744974656D5B335D:,1:74696D65:404E80,3:6D61704D6435:65316536343662393261303334643533663961616" +
        "13335353531356134646264,1:73617665426F73734974656D5B375D:,1:73617665426F73734974656D5B365D:,1:73" +
        "617665426F73734974656D5B355D:,3:73617665526F6F6D4E616D65:723132385F537461676531,1:6465617468:401" +
        "0,1:7361766547616D65436C656172:,1:73617665426F73734974656D5B345D:,1:73617665426F73734974656D5B32" +
        "5D:,1:73617665506C6179657258:406280,1:73617665506C6179657259:405DC0,1:73617665426F73734974656D5B" +
        "335D:,1:736176655365637265744974656D5B385D:,1:73617665426F73734974656D5B315D:";

    /// <summary>What the real runner stored in it.</summary>
    private const string StoredMd5 = "e1e646b92a034d53f9aaa355515a4dbd";

    [TestInitialize]
    public void SetUp() => DataStructuresFunctions.Clear();

    [TestCleanup]
    public void TearDown() => DataStructuresFunctions.Clear();

    [TestMethod]
    public void ARealSavePassesTheGameOwnIntegrityCheck()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(map, RealSave);

        Assert.AreEqual(33, DataStructuresFunctions.ds_map_size([map]).Conv<int>());
        Assert.AreEqual(StoredMd5,
            DataStructuresFunctions.ds_map_find_value([map, "mapMd5"]).Conv<string>());

        // --- exactly what scrLoadGame does ---
        DataStructuresFunctions.ds_map_delete([map, "mapMd5"]);
        var written = DataStructuresFunctions.ds_map_write(map).Conv<string>();
        var genMd5 = MathFunctions.md5_string_unicode([written + Salt]).Conv<string>();

        Assert.AreEqual(StoredMd5, genMd5,
            "the game would show \"Save invalid!\" and start a new game");
    }

    [TestMethod]
    public void ARealSaveIsWrittenBackByteForByte()
    {
        // The other direction: what this engine hands back has to be what the real runner would
        // have written, or the install rejects it on the next load.
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(map, RealSave);

        Assert.AreEqual(RealSave, DataStructuresFunctions.ds_map_write(map).Conv<string>());
    }

    [TestMethod]
    public void TheValuesSurviveTheRoundTrip()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(map, RealSave);

        object? Find(string key) => DataStructuresFunctions.ds_map_find_value([map, key]);

        Assert.AreEqual("r128_Stage1", Find("saveRoomName").Conv<string>());
        Assert.AreEqual(148.0, Find("savePlayerX").Conv<double>());
        Assert.AreEqual(119.0, Find("savePlayerY").Conv<double>());
        Assert.AreEqual(4.0, Find("death").Conv<double>());
        Assert.AreEqual(61.0, Find("time").Conv<double>());
        Assert.AreEqual(0.0, Find("difficulty").Conv<double>(), "an empty payload is zero");
    }
}
