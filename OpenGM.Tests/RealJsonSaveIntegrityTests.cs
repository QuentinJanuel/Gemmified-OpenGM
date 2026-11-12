using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// Whether a save can move between this engine and a real GameMaker install for the *other* save
/// idiom -- the one built on json_encode rather than ds_map_write (see
/// <see cref="RealSaveIntegrityTests"/> for that one).
/// </summary>
/// <remarks>
/// <para>
/// The fixture is a save file a real GMS 1.4 install wrote: Madoka Needle, played through the
/// desktop app's native install. Its load path, from the decompiled bytecode, is:
/// </para>
/// <code>
///     saveMap = json_decode(base64_decode(file_text_read_string(f)));
///     var mapMd5 = ds_map_find_value(saveMap, "mapMd5");
///     ds_map_delete(saveMap, "mapMd5");
///     var genMd5 = md5_string_unicode(json_encode(saveMap) + global.md5StrAdd);
///     if (mapMd5 != genMd5) { show_message("Save invalid!"); scrRestartGame(); }
/// </code>
/// <para>
/// So json_encode is a *text* format the game checksums, and passing this needs three things to
/// match the real runner exactly: the key order (its hash-bucket order, not insertion), the
/// spacing (<c>{ "k": v, "k2": v2 }</c>), and the number format (six decimals, always). This
/// engine used to get all three wrong -- compact, insertion-ordered, and rendering a number by
/// whichever CLR type happened to be boxing it, so the same value came out <c>0</c> on one key and
/// <c>0.0</c> on the next.
/// </para>
/// </remarks>
[TestClass]
public class RealJsonSaveIntegrityTests
{
    /// <summary>The game's own salt, left at the fangame template's default.</summary>
    private const string Salt = "Put something here";

    /// <summary>A real save file's contents, verbatim (the base64 the game wrote to Data\save2).</summary>
    private const string RealSaveBase64 =
        "eyAic2F2ZUdyYXYiOiAxLjAwMDAwMCwgImRpZmZpY3VsdHkiOiAwLjAwMDAwMCwgInNhdmVCb3NzSXRlbVs0XSI6IDAuMDAwMD" +
        "AwLCAic2F2ZUJvc3NJdGVtWzBdIjogMC4wMDAwMDAsICJzYXZlUGxheWVyWCI6IDU0LjAwMDAwMCwgInNhdmVTZWNyZXRJdGVt" +
        "WzVdIjogMC4wMDAwMDAsICJzYXZlU2VjcmV0SXRlbVsxXSI6IDAuMDAwMDAwLCAic2F2ZUJvc3NJdGVtWzVdIjogMC4wMDAwMD" +
        "AsICJzYXZlQm9zc0l0ZW1bMV0iOiAwLjAwMDAwMCwgInRpbWVNaWNybyI6IDg2NDg2OC4wMDAwMDAsICJzYXZlU2VjcmV0SXRl" +
        "bVs0XSI6IDAuMDAwMDAwLCAic2F2ZVNlY3JldEl0ZW1bMF0iOiAwLjAwMDAwMCwgInNhdmVCb3NzSXRlbVs2XSI6IDAuMDAwMD" +
        "AwLCAic2F2ZUJvc3NJdGVtWzJdIjogMC4wMDAwMDAsICJtYXBNZDUiOiAiOWRmYTc5Y2I3YWViYTdjMzBjYmRjN2UwY2ZmZjky" +
        "OWEiLCAic2F2ZVNlY3JldEl0ZW1bN10iOiAwLjAwMDAwMCwgInNhdmVTZWNyZXRJdGVtWzNdIjogMC4wMDAwMDAsICJ0aW1lIj" +
        "ogNDcuMDAwMDAwLCAic2F2ZVBsYXllclkiOiA4Ny4wMDAwMDAsICJkZWF0aCI6IDYuMDAwMDAwLCAic2F2ZUJvc3NJdGVtWzdd" +
        "IjogMC4wMDAwMDAsICJzYXZlQm9zc0l0ZW1bM10iOiAwLjAwMDAwMCwgInNhdmVSb29tIjogInIxXzIiLCAic2F2ZUdhbWVDbG" +
        "VhciI6IDAuMDAwMDAwLCAic2F2ZVNlY3JldEl0ZW1bNl0iOiAwLjAwMDAwMCwgInNhdmVTZWNyZXRJdGVtWzJdIjogMC4wMDAw" +
        "MDAgfQ==";

    /// <summary>What the real runner stored in it.</summary>
    private const string StoredMd5 = "9dfa79cb7aeba7c30cbdc7e0cfff929a";

    /// <summary>
    /// That same file's text with the "mapMd5" member spliced out -- the exact string the real
    /// runner hashes on load, and so the exact string json_encode has to produce. Cut from the
    /// fixture rather than copied out, so the two cannot drift.
    /// </summary>
    private static string RealJsonWithoutMd5()
    {
        var json = MathFunctions.base64_decode([RealSaveBase64]).Conv<string>();
        var member = $", \"mapMd5\": \"{StoredMd5}\"";
        var at = json.IndexOf(member, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, at, "the fixture should carry the checksum the game stored");
        return json.Remove(at, member.Length);
    }

    [TestInitialize]
    public void SetUp()
    {
        DataStructuresFunctions.Clear();
        CompatFlags.LegacyDataStructures = false;
    }

    [TestCleanup]
    public void TearDown()
    {
        DataStructuresFunctions.Clear();
        CompatFlags.LegacyDataStructures = false;
    }

    private static string Encode(int map) => DataStructuresFunctions.json_encode([map]).Conv<string>();

    /// <summary>
    /// The load direction: a real install's save, opened here, passes the game's own check.
    /// </summary>
    [TestMethod]
    public void ARealInstallsSaveLoadsHere()
    {
        var json = MathFunctions.base64_decode([RealSaveBase64]).Conv<string>();
        var map = FileFunctions.json_decode([json]).Conv<int>();

        var storedMd5 = DataStructuresFunctions.ds_map_find_value([map, "mapMd5"]).Conv<string>();
        Assert.AreEqual(StoredMd5, storedMd5);

        DataStructuresFunctions.ds_map_delete([map, "mapMd5"]);
        var genMd5 = MathFunctions.md5_string_unicode([Encode(map) + Salt]).Conv<string>();

        Assert.AreEqual(storedMd5, genMd5, "the game would show \"Save invalid!\" and start over");
    }

    /// <summary>
    /// The save direction: the map the game builds, encoded here, is byte-for-byte what the real
    /// runner writes -- so the install accepts a save made in the emulator.
    /// </summary>
    [TestMethod]
    public void ASaveWrittenHereIsByteForByteTheRealRunnersText()
    {
        // Built exactly the way the game's scrSaveGame builds it: ds_map_add in source order, which
        // is nothing like the order it has to come back out in.
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "death", 6.0);
        DataStructuresFunctions.ds_map_add(map, "time", 47.0);
        DataStructuresFunctions.ds_map_add(map, "timeMicro", 864868.0);
        DataStructuresFunctions.ds_map_add(map, "difficulty", 0.0);
        DataStructuresFunctions.ds_map_add(map, "saveRoom", "r1_2");
        DataStructuresFunctions.ds_map_add(map, "savePlayerX", 54.0);
        DataStructuresFunctions.ds_map_add(map, "savePlayerY", 87.0);
        DataStructuresFunctions.ds_map_add(map, "saveGrav", 1.0);
        for (var i = 0; i < 8; i++)
        {
            DataStructuresFunctions.ds_map_add(map, $"saveSecretItem[{i}]", 0.0);
        }
        for (var i = 0; i < 8; i++)
        {
            DataStructuresFunctions.ds_map_add(map, $"saveBossItem[{i}]", 0.0);
        }
        DataStructuresFunctions.ds_map_add(map, "saveGameClear", 0.0);

        Assert.AreEqual(RealJsonWithoutMd5(), Encode(map));

        // ...and therefore the checksum the game stores alongside it is the one the install expects.
        Assert.AreEqual(StoredMd5, MathFunctions.md5_string_unicode([Encode(map) + Salt]).Conv<string>());
    }

    /// <summary>
    /// A number renders the same whatever this engine happens to be boxing it as. GML has one
    /// number type; a save showing <c>0</c> on one key and <c>0.0</c> on the next is this engine's
    /// CLR types leaking into a file a real runner has to read.
    /// </summary>
    [TestMethod]
    public void NumbersDoNotRenderByTheirClrType()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "a", 0.0);
        DataStructuresFunctions.ds_map_add(map, "b", 0L);
        DataStructuresFunctions.ds_map_add(map, "c", (short)0);
        DataStructuresFunctions.ds_map_add(map, "d", true);
        DataStructuresFunctions.ds_map_add(map, "e", 7.5);

        var json = Encode(map);
        StringAssert.Contains(json, "\"a\": 0.000000");
        StringAssert.Contains(json, "\"b\": 0.000000");
        StringAssert.Contains(json, "\"c\": 0.000000");
        StringAssert.Contains(json, "\"d\": 1.000000", "GML has no bool: true is 1");
        StringAssert.Contains(json, "\"e\": 7.500000");

        // A bare int is the one thing that stays ambiguous: this engine boxes a live ds_map/ds_list
        // id as an int and nothing else, so json_encode reads `2` as "the map with id 2". That is
        // the pre-existing nested-structure heuristic, not something this format change decides.
    }

    /// <summary>What this engine writes, it reads back -- the within-one-runtime case.</summary>
    [TestMethod]
    public void ItRoundTripsThroughJsonDecode()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "death", 6.0);
        DataStructuresFunctions.ds_map_add(map, "saveRoom", "r1_2");

        var reloaded = FileFunctions.json_decode([Encode(map)]).Conv<int>();

        Assert.AreEqual(Encode(map), Encode(reloaded));
        Assert.AreEqual(6.0, DataStructuresFunctions.ds_map_find_value([reloaded, "death"]).Conv<double>());
        Assert.AreEqual("r1_2", DataStructuresFunctions.ds_map_find_value([reloaded, "saveRoom"]).Conv<string>());
    }
}
