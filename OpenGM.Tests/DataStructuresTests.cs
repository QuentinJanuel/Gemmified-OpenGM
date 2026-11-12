using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The ds_* module was nearly complete but had a few holes in it, and a GML call to a function
/// OpenGM doesn't have isn't a soft failure -- VMExecutor logs "Can't resolve script" and breaks out
/// of the whole event, so everything after that line silently doesn't happen. These cover the ones
/// real GMS1 games were hitting: TweenGMS alone reaches ds_map_replace (room end), ds_list_set
/// (TweenEventEnable) and ds_priority_destroy (tweener teardown).
/// </summary>
[TestClass]
public class DataStructuresTests
{
    private const int TypePriority = (int)DSType.Priority;

    [TestInitialize]
    public void SetUp() => DataStructuresFunctions.Clear();

    [TestCleanup]
    public void TearDown() => DataStructuresFunctions.Clear();

    [TestMethod]
    public void MapReplaceOverwritesAndReportsWhetherThereWasAnything()
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();

        Assert.IsFalse(DataStructuresFunctions.ds_map_replace([map, "key", 1]).Conv<bool>(),
            "replacing a key that isn't there yet reports nothing was replaced");
        Assert.AreEqual(1, DataStructuresFunctions.ds_map_find_value([map, "key"]).Conv<int>());

        Assert.IsTrue(DataStructuresFunctions.ds_map_replace([map, "key", 2]).Conv<bool>());
        Assert.AreEqual(2, DataStructuresFunctions.ds_map_find_value([map, "key"]).Conv<int>());
        Assert.AreEqual(1, DataStructuresFunctions.ds_map_size([map]).Conv<int>());
    }

    [TestMethod]
    public void ListSetWritesInRange()
    {
        var list = DataStructuresFunctions.ds_list_create().Conv<int>();
        DataStructuresFunctions.ds_list_add(list, "a");
        DataStructuresFunctions.ds_list_add(list, "b");

        DataStructuresFunctions.ds_list_set(list, 0, "z");

        Assert.AreEqual("z", DataStructuresFunctions.ds_list_find_value([list, 0]).Conv<string>());
        Assert.AreEqual("b", DataStructuresFunctions.ds_list_find_value([list, 1]).Conv<string>());
        Assert.AreEqual(2, DataStructuresFunctions.ds_list_size([list]).Conv<int>());
    }

    [TestMethod]
    public void ListSetGrowsTheListToReachThePosition()
    {
        // This is the half that ds_list_replace deliberately doesn't do, and it's what the
        // `list[| i] = v` accessor compiles to.
        var list = DataStructuresFunctions.ds_list_create().Conv<int>();

        DataStructuresFunctions.ds_list_set(list, 3, "far");

        Assert.AreEqual(4, DataStructuresFunctions.ds_list_size([list]).Conv<int>());
        Assert.AreEqual("far", DataStructuresFunctions.ds_list_find_value([list, 3]).Conv<string>());
        Assert.AreEqual(0, DataStructuresFunctions.ds_list_find_value([list, 0]).Conv<int>(),
            "the gap should be padded with 0");
    }

    [TestMethod]
    public void PriorityDestroyRemovesTheQueue()
    {
        var priority = DataStructuresFunctions.ds_priority_create([]).Conv<int>();
        DataStructuresFunctions.ds_priority_add([priority, "thing", 1]);

        Assert.IsTrue(DataStructuresFunctions.ds_exists(priority, TypePriority).Conv<bool>());

        DataStructuresFunctions.ds_priority_destroy([priority]);

        Assert.IsFalse(DataStructuresFunctions.ds_exists(priority, TypePriority).Conv<bool>(),
            "every other ds_*_destroy frees its structure; this one used to leak it");
    }

    [TestMethod]
    public void SecureSaveAndLoadRoundTripThroughTheNormalFileApi()
    {
        // Games write these with Windows paths and then check for them with file_exists, so the
        // secure map file has to land in the same persisted store under the same basename.
        const string path = @"Data\opengm_test_save";

        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "death", 42);
        DataStructuresFunctions.ds_map_add(map, "name", "player");

        try
        {
            Assert.IsTrue(DataStructuresFunctions.ds_map_secure_save([map, path]).Conv<bool>());

            var loaded = DataStructuresFunctions.ds_map_secure_load([path]).Conv<int>();

            Assert.AreNotEqual(-1, loaded);
            Assert.AreNotEqual(map, loaded, "loading builds a new map rather than reusing the source");
            Assert.AreEqual(42, DataStructuresFunctions.ds_map_find_value([loaded, "death"]).Conv<int>());
            Assert.AreEqual("player", DataStructuresFunctions.ds_map_find_value([loaded, "name"]).Conv<string>());
            Assert.AreEqual(2, DataStructuresFunctions.ds_map_size([loaded]).Conv<int>());
        }
        finally
        {
            FileFunctions.file_delete([path]);
        }

        // file_delete reaching it proves the secure save landed in the same persisted store, under
        // the same basename, as any other save file the game writes.
        Assert.AreEqual(-1, DataStructuresFunctions.ds_map_secure_load([path]).Conv<int>());
    }

    /// <summary>
    /// Writes a file byte-for-byte through the normal text-file API, which lands it in the same
    /// persisted store a real save would be imported into.
    /// </summary>
    private static void WriteRawFile(string path, string contents)
    {
        var handle = FileFunctions.file_text_open_write([path]).Conv<int>();
        FileFunctions.file_text_write_string([handle, contents]);
        FileFunctions.file_text_close([handle]);
    }

    [TestMethod]
    public void SecureLoadReadsASaveWrittenByRealGameMaker()
    {
        // A secure save is a 40-character machine-specific prefix followed by
        // base64(json_encode(map)). This is one built by hand exactly the way the real runner writes
        // it, with a prefix from some other machine -- loading has to ignore the prefix, which is
        // the entire reason a save can move between an install and here.
        const string path = @"Data\opengm_test_foreign";
        const string foreignPrefix = "0123456789abcdef0123456789abcdef01234567";
        const string json = """{"death":42,"name":"player"}""";

        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        WriteRawFile(path, foreignPrefix + payload);

        try
        {
            var loaded = DataStructuresFunctions.ds_map_secure_load([path]).Conv<int>();

            Assert.AreNotEqual(-1, loaded, "a save from a real install should load");
            Assert.AreEqual(42, DataStructuresFunctions.ds_map_find_value([loaded, "death"]).Conv<int>());
            Assert.AreEqual("player", DataStructuresFunctions.ds_map_find_value([loaded, "name"]).Conv<string>());
        }
        finally
        {
            FileFunctions.file_delete([path]);
        }
    }

    [TestMethod]
    public void SecureSaveWritesRealGameMakerSFormat()
    {
        const string path = @"Data\opengm_test_format";

        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        DataStructuresFunctions.ds_map_add(map, "death", 42);

        try
        {
            DataStructuresFunctions.ds_map_secure_save([map, path]);

            var handle = FileFunctions.file_text_open_read([path]).Conv<int>();
            var contents = FileFunctions.file_text_read_string([handle]).Conv<string>();
            FileFunctions.file_text_close([handle]);

            Assert.AreEqual(40, contents.Length - contents[40..].Length,
                "the payload should start after a 40-character prefix");

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(contents[40..]));

            Assert.IsTrue(json.StartsWith('{'), $"payload should be JSON, got {json}");
            Assert.IsTrue(json.Contains("\"death\""), $"payload should hold the map's keys, got {json}");
        }
        finally
        {
            FileFunctions.file_delete([path]);
        }
    }

    [TestMethod]
    public void ResavingAnImportedSaveKeepsTheOriginalMachineSPrefix()
    {
        // The prefix binds a save to the machine that wrote it, so a save imported from an install,
        // played here and written back has to carry that install's prefix or it won't be accepted
        // when it goes home again.
        const string path = @"Data\opengm_test_prefix";
        const string foreignPrefix = "0123456789abcdef0123456789abcdef01234567";

        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"death":1}"""));
        WriteRawFile(path, foreignPrefix + payload);

        try
        {
            var loaded = DataStructuresFunctions.ds_map_secure_load([path]).Conv<int>();
            DataStructuresFunctions.ds_map_replace([loaded, "death", 2]);
            DataStructuresFunctions.ds_map_secure_save([loaded, path]);

            var handle = FileFunctions.file_text_open_read([path]).Conv<int>();
            var contents = FileFunctions.file_text_read_string([handle]).Conv<string>();
            FileFunctions.file_text_close([handle]);

            StringAssert.StartsWith(contents, foreignPrefix);

            // ...and the edit really did get written, not just the prefix preserved.
            var reloaded = DataStructuresFunctions.ds_map_secure_load([path]).Conv<int>();
            Assert.AreEqual(2, DataStructuresFunctions.ds_map_find_value([reloaded, "death"]).Conv<int>());
        }
        finally
        {
            FileFunctions.file_delete([path]);
        }
    }

    [TestMethod]
    public void SecureLoadOfAMissingFileIsMinusOne()
    {
        // scrLoadGame-style code branches on `if (saveMap != -1)`, so a missing save must not come
        // back as an empty map that reads as a save full of zeroes.
        var loaded = DataStructuresFunctions.ds_map_secure_load([@"Data\opengm_no_such_save"]).Conv<int>();

        Assert.AreEqual(-1, loaded);
    }

    [TestMethod]
    public void FindIndexMatchesAcrossMismatchedBoxedNumericTypes()
    {
        // A compiled-code numeric literal (e.g. a room index in a game's own data table) reaches
        // ds_list_add boxed as a short (VMType.e / VMCodeInstruction.ShortData), while a runtime
        // variable read like the `room` builtin reaches ds_list_find_index boxed as an int -- raw
        // object.Equals (what List<T>.IndexOf uses) treats those as unequal even though they're the
        // same GML number, which is exactly the "can't find current room in a list built from room
        // literals" failure a real stage-transition script hit.
        var list = DataStructuresFunctions.ds_list_create().Conv<int>();
        DataStructuresFunctions.ds_list_add(list, (short)11);

        var index = DataStructuresFunctions.ds_list_find_index([list, 11]).Conv<int>();

        Assert.AreEqual(0, index);
    }

    [TestMethod]
    public void FindIndexWorksEvenWhenTheListsHandleIdExceedsItsElementCount()
    {
        // ds_list_find_index used to bail out with "if (id >= list.Count) return -1", comparing the
        // list's own handle id (an unrelated, ever-increasing counter) against how many elements are
        // currently in that same list. Any game that creates more than a handful of ds_lists before
        // populating this one (extremely common) would trip this and get a false "not found" no
        // matter what it searched for.
        for (var i = 0; i < 5; i++)
        {
            DataStructuresFunctions.ds_list_create();
        }

        var list = DataStructuresFunctions.ds_list_create().Conv<int>();
        Assert.IsTrue(list >= 5, "test setup should have pushed this list's handle id past its eventual element count");

        DataStructuresFunctions.ds_list_add(list, "only-entry");

        var index = DataStructuresFunctions.ds_list_find_index([list, "only-entry"]).Conv<int>();

        Assert.AreEqual(0, index);
    }
}
