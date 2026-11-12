using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// That <c>ds_*_write</c> produces, and <c>ds_*_read</c> accepts, what a <b>real</b> GameMaker
/// runner does -- not merely something this engine can read back.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is invented. The binary fixtures are the literal output of a real GMS1 install on
/// Windows, from the <c>ds_*_write formats</c> probe; the text fixtures are
/// entries copied out of a save file written by a real install of I Wanna Spook Jam.
/// </para>
/// <para>
/// The assertions deliberately look at bytes rather than at round trips. An encoding of our own
/// passed every round-trip test there was for as long as it existed -- which is exactly how four
/// wrong header constants survived until a save written here was handed to a real install and it
/// crashed on load.
/// </para>
/// </remarks>
[TestClass]
public class DsMapWireFormatTests
{
    // -- what a real GMS1 install wrote, verbatim ---------------------------------------------
    //
    // Each is ds_*_write over the same fixed pair of values, so the header is the only thing that
    // differs between them and the count/tag/payload layout is visible three times over.

    /// <summary>ds_map_write of { "k": 1, 2: "v" }.</summary>
    private const string RealMap =
        "920100000200000000000000000000000000004001000000010000007601000000010000006B00000000000000000000F03F";

    /// <summary>ds_list_write of [ 1, "x" ].</summary>
    private const string RealList = "2E0100000200000000000000000000000000F03F010000000100000078";

    /// <summary>ds_stack_write of a stack pushed 1 then "two" -- written bottom first.</summary>
    private const string RealStack = "660000000200000000000000000000000000F03F010000000300000074776F";

    // -- the pre-Studio text format, from a real save file ------------------------------------

    private const string LegacySaveRoomName = "3:73617665526F6F6D4E616D65:723132385F537461676531";
    private const string LegacySaveGrav = "1:7361766547726176:3FF0";
    private const string LegacySaveRoom = "1:73617665526F6F6D:402C";
    private const string LegacyDifficulty = "1:646966666963756C7479:";

    [TestInitialize]
    public void SetUp() => DataStructuresFunctions.Clear();

    [TestCleanup]
    public void TearDown() => DataStructuresFunctions.Clear();

    private static int ReadIntoNewMap(string text)
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(map, text);
        return map;
    }

    private static object? Find(int map, string key) =>
        DataStructuresFunctions.ds_map_find_value([map, key]);

    [TestMethod]
    public void AMapIsWrittenExactlyAsARealRunnerWritesIt()
    {
        // Inserted in the order the real runner emitted them -- its hash table's order, which is
        // arbitrary but which insertion order reproduces here, so the comparison can be exact
        // rather than a set comparison that would let a layout change slip through.
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, 2, "v");
        DataStructuresFunctions.ds_map_add(map, "k", 1);

        Assert.AreEqual(RealMap, DataStructuresFunctions.ds_map_write(map).Conv<string>());
    }

    [TestMethod]
    public void AListAndAStackAreWrittenExactlyAsARealRunnerWritesThem()
    {
        var list = DataStructuresFunctions.ds_list_create().Conv<int>();
        DataStructuresFunctions.ds_list_add(list, 1);
        DataStructuresFunctions.ds_list_add(list, "x");
        Assert.AreEqual(RealList, DataStructuresFunctions.ds_list_write(list).Conv<string>());

        var stack = DataStructuresFunctions.ds_stack_create().Conv<int>();
        DataStructuresFunctions.ds_stack_push([stack, 1]);
        DataStructuresFunctions.ds_stack_push([stack, "two"]);
        Assert.AreEqual(RealStack, DataStructuresFunctions.ds_stack_write([stack]).Conv<string>());
    }

    [TestMethod]
    public void AMapWrittenByARealRunnerLoads()
    {
        var map = ReadIntoNewMap(RealMap);

        Assert.AreEqual(1.0, Find(map, "k").Conv<double>());
        Assert.AreEqual("v", DataStructuresFunctions.ds_map_find_value([map, 2]).Conv<string>());
    }

    [TestMethod]
    public void AListWrittenByARealRunnerLoads()
    {
        var list = DataStructuresFunctions.ds_list_create().Conv<int>();
        DataStructuresFunctions.ds_list_read(list, RealList);

        Assert.AreEqual(2, DataStructuresFunctions.ds_list_size([list]).Conv<int>());
        Assert.AreEqual(1.0, DataStructuresFunctions.ds_list_find_value([list, 0]).Conv<double>());
        Assert.AreEqual("x", DataStructuresFunctions.ds_list_find_value([list, 1]).Conv<string>());
    }

    // -- the legacy text format ---------------------------------------------------------------

    [TestMethod]
    public void ASaveInThePreStudioTextFormatLoads()
    {
        // I Wanna Spook Jam's save file is in this format, so games really do ask for it. Its
        // grammar is entry := tag ":" hex(key) ":" hex(value), joined by commas, where bit 0 of the
        // tag says the key is a string and bit 1 says the value is.
        var map = ReadIntoNewMap(string.Join(",",
            LegacyDifficulty, LegacySaveGrav, LegacySaveRoom, LegacySaveRoomName));

        // A double, big-endian, with its trailing zero bytes dropped: "3FF0" is 1.0.
        Assert.AreEqual(1.0, Find(map, "saveGrav").Conv<double>());
        Assert.AreEqual(14.0, Find(map, "saveRoom").Conv<double>());
        // Dropping *every* byte is how zero is written, so an empty payload is 0.0 rather than a
        // parse failure -- which matters, because a save is mostly zeroes.
        Assert.AreEqual(0.0, Find(map, "difficulty").Conv<double>());
        Assert.AreEqual("r128_Stage1", Find(map, "saveRoomName").Conv<string>());
    }

    [TestMethod]
    public void AskingForTheLegacyFormatGetsIt()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "saveGrav", 1.0);
        DataStructuresFunctions.ds_map_add(map, "saveRoom", 14.0);
        DataStructuresFunctions.ds_map_add(map, "difficulty", 0.0);
        DataStructuresFunctions.ds_map_add(map, "saveRoomName", "r128_Stage1");

        var written = DataStructuresFunctions.ds_map_write(map, true).Conv<string>();

        // Byte-for-byte against the real save file, entry by entry.
        CollectionAssert.AreEquivalent(
            new[] { LegacySaveGrav, LegacySaveRoom, LegacyDifficulty, LegacySaveRoomName },
            written.Split(','));
    }

    [TestMethod]
    public void WithoutBeingAskedTheCurrentFormatIsWritten()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "k", 1);

        Assert.IsFalse(DataStructuresFunctions.ds_map_write(map).Conv<string>().Contains(':'),
            "the legacy format is opt-in; the default is the binary one a real GMS1 runner writes");
    }

    [TestMethod]
    public void ALegacyTagSaysWhichOfTheKeyAndValueAreStrings()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "s", "x");   // string key, string value
        DataStructuresFunctions.ds_map_add(map, "n", 1.0);   // string key, real value
        DataStructuresFunctions.ds_map_add(map, 7.0, "x");   // real key, string value
        DataStructuresFunctions.ds_map_add(map, 8.0, 1.0);   // real key, real value

        var tags = DataStructuresFunctions.ds_map_write(map, true).Conv<string>()
            .Split(',')
            .Select(entry => entry.Split(':')[0])
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "3", "1", "2", "0" }, tags);
    }

    // -- compatibility and robustness ---------------------------------------------------------

    [TestMethod]
    public void SavesWrittenByOlderBuildsOfThisEngineStillLoad()
    {
        // Header 0x12F, which nothing writes any more. Players have these in their browsers and in
        // their cloud save rows, and the read path never checked the header, so they keep working.
        var map = ReadIntoNewMap("2F0100000100000001000000020000006870000000000000000000001440");

        Assert.AreEqual(5.0, Find(map, "hp").Conv<double>());
    }

    [TestMethod]
    public void AnEmptyMapRoundTripsInEitherFormat()
    {
        var empty = DataStructuresFunctions.ds_map_create().Conv<int>();

        Assert.AreEqual("", DataStructuresFunctions.ds_map_write(empty, true).Conv<string>());
        Assert.AreEqual(0, DataStructuresFunctions.ds_map_size([ReadIntoNewMap("")]).Conv<int>());
        Assert.AreEqual(0, DataStructuresFunctions.ds_map_size([
            ReadIntoNewMap(DataStructuresFunctions.ds_map_write(empty).Conv<string>())]).Conv<int>());
    }

    [TestMethod]
    public void RubbishLeavesTheMapAloneRatherThanHalfFillingIt()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "kept", 1.0);

        // Second entry is malformed. A parser that applied entries as it went would leave the game
        // with a save that is half its own and half the file's, and no way to tell.
        DataStructuresFunctions.ds_map_read(map, "1:6E:3FF0,1:zz:3FF0");

        Assert.AreEqual(1, DataStructuresFunctions.ds_map_size([map]).Conv<int>());
        Assert.AreEqual(1.0, Find(map, "kept").Conv<double>());
    }

    [TestMethod]
    public void NeitherFormatThrowsOutOfALoadRoutine()
    {
        // A game has no way to catch an exception from ds_map_read, so a corrupt save has to come
        // back as an empty map and a line in the log.
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(map, "not hex and not the text format either");

        Assert.AreEqual(0, DataStructuresFunctions.ds_map_size([map]).Conv<int>());
    }

    [TestMethod]
    public void EveryValueKindSurvivesTheLegacyFormat()
    {
        // The widths matter: the VM hands out whichever numeric type an opcode produced, and a
        // literal small enough for a 16-bit immediate arrives as a short. Listing only int and long
        // is what once wrote `global.stage = 0` as NaN.
        var values = new (string Key, object? Value, double AsReal)[]
        {
            ("short", (short)3, 3),
            ("sbyte", (sbyte)-7, -7),
            ("byte", (byte)7, 7),
            ("ushort", (ushort)7, 7),
            ("int", 3, 3),
            ("uint", 3u, 3),
            ("long", 9000000000L, 9000000000d),
            ("double", 1.5, 1.5),
            ("negative", -1.5, -1.5),
            ("bigdouble", 1234567890123.0, 1234567890123.0),
            ("float", 2.5f, 2.5),
            ("bool", true, 1),
            ("zero", 0.0, 0),
        };

        var saved = DataStructuresFunctions.ds_map_create().Conv<int>();
        foreach (var (key, value, _) in values)
        {
            DataStructuresFunctions.ds_map_add(saved, key, value);
        }

        var loaded = ReadIntoNewMap(DataStructuresFunctions.ds_map_write(saved, true).Conv<string>());

        foreach (var (key, _, expected) in values)
        {
            Assert.AreEqual(expected, Find(loaded, key).Conv<double>(), $"'{key}' changed value");
        }
    }

    [TestMethod]
    public void AStringKeepsItsBytesWhateverIsInIt()
    {
        var saved = DataStructuresFunctions.ds_map_create().Conv<int>();
        // Every character that would break a format built on delimiters, plus non-ASCII. They are
        // safe only because the payload is hex -- which is why the format hex-encodes what is
        // already text.
        DataStructuresFunctions.ds_map_add(saved, "punctuation", "a,b:c");
        DataStructuresFunctions.ds_map_add(saved, "unicode", "ぷ­ぷ");
        DataStructuresFunctions.ds_map_add(saved, "empty", "");

        var loaded = ReadIntoNewMap(DataStructuresFunctions.ds_map_write(saved, true).Conv<string>());

        Assert.AreEqual("a,b:c", Find(loaded, "punctuation").Conv<string>());
        Assert.AreEqual("ぷ­ぷ", Find(loaded, "unicode").Conv<string>());
        Assert.AreEqual("", Find(loaded, "empty").Conv<string>());
    }
}
