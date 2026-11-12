using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// Mirrors what a real GMS1 save/load pair does byte for byte -- build a ds_map, ds_map_write it,
/// base64 it into a text file, then read it back the other way round.
/// </summary>
[TestClass]
public class SaveRoundTripTests
{
    private const string Path = @"Data\opengm_test_roundtrip";

    [TestInitialize]
    public void SetUp() => DataStructuresFunctions.Clear();

    [TestCleanup]
    public void TearDown()
    {
        FileFunctions.file_delete([Path]);
        DataStructuresFunctions.Clear();
    }

    private static int BuildSaveMap()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();

        DataStructuresFunctions.ds_map_add(map, "death", 12);
        DataStructuresFunctions.ds_map_add(map, "time", 3456);
        DataStructuresFunctions.ds_map_add(map, "stage", 0);

        for (var i = 0; i < 6; i++)
        {
            DataStructuresFunctions.ds_map_add(map, $"saveRoom[{i}]", i == 0 ? "rHub" : "");
            DataStructuresFunctions.ds_map_add(map, $"savePlayerX[{i}]", 100 + i);
        }

        return map;
    }

    [TestMethod]
    public void MapSurvivesWriteBase64FileReadDecodeRead()
    {
        var saved = BuildSaveMap();

        // --- scrSaveGame ---
        var write = FileFunctions.file_text_open_write([Path]).Conv<int>();
        FileFunctions.file_text_write_string(
            [write, MathFunctions.base64_encode([DataStructuresFunctions.ds_map_write(saved)])]);
        FileFunctions.file_text_close([write]);

        // --- scrLoadGame ---
        var read = FileFunctions.file_text_open_read([Path]).Conv<int>();
        var contents = FileFunctions.file_text_read_string([read]).Conv<string>();
        FileFunctions.file_text_close([read]);

        var loaded = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(loaded, MathFunctions.base64_decode([contents]));

        Assert.AreEqual(12, DataStructuresFunctions.ds_map_find_value([loaded, "death"]).Conv<int>());
        Assert.AreEqual(0, DataStructuresFunctions.ds_map_find_value([loaded, "stage"]).Conv<int>(),
            "the key the loader indexes saveRoom with");
        Assert.AreEqual("rHub", DataStructuresFunctions.ds_map_find_value([loaded, "saveRoom[0]"]).Conv<string>());
        Assert.AreEqual(105, DataStructuresFunctions.ds_map_find_value([loaded, "savePlayerX[5]"]).Conv<int>());
    }

    [TestMethod]
    public void EveryValueKindASaveHoldsSurvivesTheRoundTrip()
    {
        // A real save mixes all of these: numbers a game did set, strings from room_get_name, and
        // undefined for slots it never reached. Whatever a value serialises as, the *keys* around it
        // have to stay findable -- a value that writes a different number of bytes than it reads
        // back would desync the stream and lose every key after it.
        // The widths matter, not just the values. The VM hands out whichever numeric type an opcode
        // produced -- a GML literal small enough for a 16-bit immediate arrives as a short -- so
        // writing these as plain C# int literals would test a type real saves rarely contain.
        var values = new (string Key, object? Value)[]
        {
            ("short", (short)3),
            ("shortzero", (short)0),
            ("shortnegative", (short)-12),
            ("sbyte", (sbyte)7),
            ("byte", (byte)7),
            ("ushort", (ushort)7),
            ("int", 3),
            ("uint", 3u),
            ("long", 9000000000L),
            ("double", 1.5),
            ("bigdouble", 1234567890123.0),
            ("float", 2.5f),
            ("bool", true),
            ("string", "rHub"),
            ("emptystring", ""),
            ("undefined", null),
            ("nan", double.NaN),
        };

        var saved = DataStructuresFunctions.ds_map_create().Conv<int>();
        foreach (var (key, value) in values)
        {
            DataStructuresFunctions.ds_map_add(saved, key, value);
        }

        // The key the loader actually indexes an array with, deliberately placed last so it only
        // survives if everything ahead of it round-tripped without desyncing the stream.
        DataStructuresFunctions.ds_map_add(saved, "stage", 0);

        var loaded = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(loaded, DataStructuresFunctions.ds_map_write(saved));

        foreach (var (key, _) in values)
        {
            Assert.IsTrue(DataStructuresFunctions.ds_map_exists([loaded, key]).Conv<bool>(),
                $"key '{key}' went missing");
        }

        Assert.AreEqual(0, DataStructuresFunctions.ds_map_find_value([loaded, "stage"]).Conv<int>(),
            "the key after every other value kind");
        Assert.AreEqual("rHub", DataStructuresFunctions.ds_map_find_value([loaded, "string"]).Conv<string>());

        foreach (var (key, value) in values)
        {
            // undefined and NaN are the two that are *supposed* to come back as NaN.
            if (value is null || value is double nan && double.IsNaN(nan))
            {
                continue;
            }

            var loadedValue = DataStructuresFunctions.ds_map_find_value([loaded, key]);

            Assert.IsFalse(loadedValue is double d && double.IsNaN(d),
                $"'{key}' came back as NaN -- its type has no case in WriteDsValue");

            if (value is string text)
            {
                Assert.AreEqual(text, loadedValue.Conv<string>(), $"'{key}' changed value");
            }
            else
            {
                Assert.AreEqual(Convert.ToDouble(value), Convert.ToDouble(loadedValue), $"'{key}' changed value");
            }
        }
    }

    [TestMethod]
    public void AShortSurvivesTheRoundTripAsANumber()
    {
        // The actual "Save invalid!" bug. WriteDsValue listed int and long but not short, so a
        // 16-bit GML literal fell through to the default branch and was written as the NaN tag --
        // which reads back as a number-shaped NaN, keeps the stream in sync, and so loses the value
        // while leaving the map structurally perfect. "I wanna be the Continuity" stored its save
        // slot's stage as a short, and loading it indexed an array with NaN.
        var saved = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(saved, "stage", (short)0);

        var loaded = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(loaded, DataStructuresFunctions.ds_map_write(saved));

        var value = DataStructuresFunctions.ds_map_find_value([loaded, "stage"]);

        Assert.IsFalse(value is double d && double.IsNaN(d), "a short must not serialise as NaN");
        Assert.AreEqual(0, value.Conv<int>());
    }

    [TestMethod]
    public void ListWriteAndReadRoundTrip()
    {
        // ds_list_write/read had their own copy of the encoding, which had drifted twice over: the
        // same missing integer widths (with no default branch at all, so a short wrote *nothing* and
        // desynced everything after it), and a read that never skipped the 4-byte header and so took
        // the header itself as the entry count. Both now share the map's helpers.
        var saved = DataStructuresFunctions.ds_list_create().Conv<int>();
        DataStructuresFunctions.ds_list_add(saved, (short)7);
        DataStructuresFunctions.ds_list_add(saved, "middle");
        DataStructuresFunctions.ds_list_add(saved, 1.5);
        DataStructuresFunctions.ds_list_add(saved, (short)9);

        var loaded = DataStructuresFunctions.ds_list_create().Conv<int>();
        DataStructuresFunctions.ds_list_read(loaded, DataStructuresFunctions.ds_list_write(saved));

        Assert.AreEqual(4, DataStructuresFunctions.ds_list_size([loaded]).Conv<int>());
        Assert.AreEqual(7, DataStructuresFunctions.ds_list_find_value([loaded, 0]).Conv<int>());
        Assert.AreEqual("middle", DataStructuresFunctions.ds_list_find_value([loaded, 1]).Conv<string>());
        Assert.AreEqual(1.5, DataStructuresFunctions.ds_list_find_value([loaded, 2]).Conv<double>());
        Assert.AreEqual(9, DataStructuresFunctions.ds_list_find_value([loaded, 3]).Conv<int>(),
            "a value after a short must still land in the right slot");
    }

    [TestMethod]
    public void AnUndefinedValueComesBackAsNaNRatherThanUndefined()
    {
        // Documents a wart rather than endorsing it. WriteDsValue has no tag for undefined, so it
        // shares the NaN one -- which means a game that saved a variable it never set reads it back
        // as a number. That's how a save whose "stage" was undefined at write time turns into an
        // array index of int.MinValue on load instead of a clean "undefined" error. Changing the tag
        // would invalidate every save already written, so it stays until there's a reason worth that.
        var saved = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(saved, "never_set", null);

        var loaded = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(loaded, DataStructuresFunctions.ds_map_write(saved));

        var value = DataStructuresFunctions.ds_map_find_value([loaded, "never_set"]);

        Assert.IsInstanceOfType<double>(value);
        Assert.IsTrue(double.IsNaN((double)value!));
    }

    [TestMethod]
    public void WriteIsStableAcrossASaveLoadCycleSoTheMd5ChecksOut()
    {
        // The save embeds md5(ds_map_write(map)) and the loader recomputes it after deleting that
        // key back off, so ds_map_write has to produce the identical string for a map that was
        // rebuilt by ds_map_read.
        var saved = BuildSaveMap();
        var expected = DataStructuresFunctions.ds_map_write(saved).Conv<string>();

        DataStructuresFunctions.ds_map_add(saved, "mapMd5", "whatever");

        var loaded = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_read(loaded, DataStructuresFunctions.ds_map_write(saved));
        DataStructuresFunctions.ds_map_delete([loaded, "mapMd5"]);

        Assert.AreEqual(expected, DataStructuresFunctions.ds_map_write(loaded).Conv<string>());
    }
}
