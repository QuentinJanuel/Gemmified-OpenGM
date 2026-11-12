using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// That a map built from scratch is iterated the way a real GameMaker runner iterates one.
/// </summary>
/// <remarks>
/// <para>
/// The rule -- buckets from the highest index down, bucket = <c>crc32(key) mod tableSize</c>, ties
/// in insertion order -- was reverse-engineered from orderings printed by a real runner
/// (a probe project). The fixtures here are that runner's literal output.
/// </para>
/// <para>
/// It matters because <c>ds_map_write</c> serialises in this order and games checksum the result,
/// so a map iterated in insertion order fails the game's own save validation even when every byte
/// of the format is right.
/// </para>
/// </remarks>
[TestClass]
public class DsMapOrderTests
{
    [TestInitialize]
    public void SetUp()
    {
        DataStructuresFunctions.Clear();
        CompatFlags.LegacyDataStructures = false;   // the 256-bucket table of 1.4.9999
    }

    [TestCleanup]
    public void TearDown()
    {
        DataStructuresFunctions.Clear();
        CompatFlags.LegacyDataStructures = false;
    }

    private static string[] IterationOrder(params string[] insertInThisOrder)
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        foreach (var key in insertInThisOrder)
        {
            DataStructuresFunctions.ds_map_add(map, key, 0);
        }

        // ds_map_write serialises in iteration order, so its key sequence *is* the order. Both
        // formats have to be read here: the legacy runtime writes the text one.
        var written = DataStructuresFunctions.ds_map_write(map).Conv<string>();
        if (written.Contains(':'))
        {
            return written.Split(',')
                .Select(entry => System.Text.Encoding.UTF8.GetString(
                    Convert.FromHexString(entry.Split(':')[1])))
                .ToArray();
        }

        var keys = new List<string>();
        for (var i = 16; i < written.Length; )
        {
            // [type][len][utf8] for the key, then [type][8 bytes] or [type][len][utf8] for the value
            var keyLen = int.Parse(written.Substring(i + 8, 2), System.Globalization.NumberStyles.HexNumber);
            keys.Add(System.Text.Encoding.UTF8.GetString(Convert.FromHexString(written.Substring(i + 16, keyLen * 2))));
            i += 16 + (keyLen * 2);
            i += written.Substring(i, 8) == "00000000" ? 8 + 16 : 8 + 8 +
                (int.Parse(written.Substring(i + 8, 2), System.Globalization.NumberStyles.HexNumber) * 2);
        }

        return keys.ToArray();
    }

    private static string[] Numbered(int count) =>
        Enumerable.Range(0, count).Select(i => $"k{i}").ToArray();

    [TestMethod]
    public void FourKeys() =>
        CollectionAssert.AreEqual(new[] { "k1", "k3", "k0", "k2" }, IterationOrder(Numbered(4)));

    [TestMethod]
    public void EightKeys() =>
        CollectionAssert.AreEqual(
            new[] { "k5", "k1", "k7", "k3", "k0", "k4", "k2", "k6" }, IterationOrder(Numbered(8)));

    [TestMethod]
    public void SeventeenKeys() =>
        CollectionAssert.AreEqual(
            "k16 k12 k14 k10 k5 k1 k7 k9 k3 k13 k11 k15 k0 k4 k2 k8 k6".Split(' '),
            IterationOrder(Numbered(17)));

    [TestMethod]
    public void ThirtyThreeKeys() =>
        CollectionAssert.AreEqual(
            ("k18 k16 k12 k31 k14 k10 k23 k5 k27 k29 k1 k7 k9 k21 k3 k25 k13 k17 k19 k32 " +
             "k11 k30 k15 k0 k28 k26 k4 k22 k24 k2 k20 k8 k6").Split(' '),
            IterationOrder(Numbered(33)));

    /// <summary>
    /// Real save-field names rather than k0..kN -- the case the rule was checked against after it
    /// was derived, not one it was fitted to.
    /// </summary>
    [TestMethod]
    public void ARealGamesKeySet() =>
        CollectionAssert.AreEqual(
            new[]
            {
                "saveGrav",
        "difficulty",
        "saveBossItem[4]",
        "ZebCrossCollected",
        "saveBossItem[8]",
        "savePlayerX",
        "saveSecretItem[5]",
        "saveSecretItem[1]",
        "saveBossItem[5]",
        "saveBossItem[1]",
        "progressMap",
        "timeMicro",
        "saveSecretItem[4]",
        "saveSecretItem[8]",
        "saveClearDeaths",
        "progressInc",
        "saveBossItem[6]",
        "saveBossItem[2]",
        "saveClearTime",
        "saveStageName",
        "saveSecretItem[7]",
        "saveSecretItem[3]",
        "saveRoomName",
        "time",
        "savePlayerY",
        "death",
        "saveBossItem[7]",
        "saveBossItem[3]",
        "saveRoom",
        "saveGameClear",
        "saveSecretItem[6]",
        "saveSecretItem[2]"
            },
            IterationOrder(
                "death",
        "time",
        "timeMicro",
        "difficulty",
        "saveRoom",
        "savePlayerX",
        "savePlayerY",
        "saveGrav",
        "ZebCrossCollected",
        "progressInc",
        "progressMap",
        "saveStageName",
        "saveRoomName",
        "saveClearTime",
        "saveClearDeaths",
        "saveSecretItem[1]",
        "saveSecretItem[2]",
        "saveSecretItem[3]",
        "saveSecretItem[4]",
        "saveSecretItem[5]",
        "saveSecretItem[6]",
        "saveSecretItem[7]",
        "saveSecretItem[8]",
        "saveBossItem[1]",
        "saveBossItem[2]",
        "saveBossItem[3]",
        "saveBossItem[4]",
        "saveBossItem[5]",
        "saveBossItem[6]",
        "saveBossItem[7]",
        "saveBossItem[8]",
        "saveGameClear"));

    /// <summary>
    /// The older runtime buckets into 4096 rather than 256, which reorders everything. Same key
    /// set as above; the table size alone accounts for the difference.
    /// </summary>
    [TestMethod]
    public void TheOlderRuntimeOrdersDifferently()
    {
        var modern = IterationOrder(
                "death",
        "time",
        "timeMicro",
        "difficulty",
        "saveRoom",
        "savePlayerX",
        "savePlayerY",
        "saveGrav",
        "ZebCrossCollected",
        "progressInc",
        "progressMap",
        "saveStageName",
        "saveRoomName",
        "saveClearTime",
        "saveClearDeaths",
        "saveSecretItem[1]",
        "saveSecretItem[2]",
        "saveSecretItem[3]",
        "saveSecretItem[4]",
        "saveSecretItem[5]",
        "saveSecretItem[6]",
        "saveSecretItem[7]",
        "saveSecretItem[8]",
        "saveBossItem[1]",
        "saveBossItem[2]",
        "saveBossItem[3]",
        "saveBossItem[4]",
        "saveBossItem[5]",
        "saveBossItem[6]",
        "saveBossItem[7]",
        "saveBossItem[8]",
        "saveGameClear");
        DataStructuresFunctions.Clear();
        CompatFlags.LegacyDataStructures = true;
        var legacy = IterationOrder(
                "death",
        "time",
        "timeMicro",
        "difficulty",
        "saveRoom",
        "savePlayerX",
        "savePlayerY",
        "saveGrav",
        "ZebCrossCollected",
        "progressInc",
        "progressMap",
        "saveStageName",
        "saveRoomName",
        "saveClearTime",
        "saveClearDeaths",
        "saveSecretItem[1]",
        "saveSecretItem[2]",
        "saveSecretItem[3]",
        "saveSecretItem[4]",
        "saveSecretItem[5]",
        "saveSecretItem[6]",
        "saveSecretItem[7]",
        "saveSecretItem[8]",
        "saveBossItem[1]",
        "saveBossItem[2]",
        "saveBossItem[3]",
        "saveBossItem[4]",
        "saveBossItem[5]",
        "saveBossItem[6]",
        "saveBossItem[7]",
        "saveBossItem[8]",
        "saveGameClear");

        CollectionAssert.AreNotEqual(modern, legacy);
        CollectionAssert.AreEquivalent(modern, legacy, "same keys, different order");
    }
}
