using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// That <c>keyboard_set_map</c> actually moves a key, which is what makes a game rebindable.
/// </summary>
/// <remarks>
/// <para>
/// GameMaker's own answer to "let the player choose the keys": the table is consulted where input is
/// read, so every way a game can ask -- <c>keyboard_check</c>, the keyboard events,
/// <c>keyboard_lastkey</c> -- agrees about which key was pressed. Controller-mapping mods are
/// built on it.
/// </para>
/// <para>
/// The fold is tested rather than the sweep around it. Reading the real keyboard needs a window,
/// while the part that is either right or silently wrong is <see cref="InputHandler.ApplyKeyMap"/>:
/// a mapping is many-to-one, and the suppression a rebind depends on is a key mapped to
/// <c>vk_nokey</c>.
/// </para>
/// </remarks>
[TestClass]
public class KeyboardMapTests
{
    private const int VkNokey = 0;
    private const int VkAnykey = 1;
    private const int VkShift = 0x10;
    private const int VkSpace = 0x20;
    private const int VkZ = 0x5A;
    private const int VkX = 0x58;

    [TestInitialize]
    public void SetUp() => InteractionFunctions.keyboard_unset_map([]);

    [TestCleanup]
    public void TearDown() => InteractionFunctions.keyboard_unset_map([]);

    private static bool[] Fold(params int[] down)
    {
        var raw = new bool[256];
        foreach (var vk in down)
        {
            raw[vk] = true;
        }

        var mapped = new bool[256];
        InputHandler.ApplyKeyMap(raw, mapped);
        return mapped;
    }

    [TestMethod]
    public void IdentityByDefault()
    {
        var mapped = Fold(VkZ);
        Assert.IsTrue(mapped[VkZ]);
        Assert.IsFalse(mapped[VkSpace]);
    }

    [TestMethod]
    public void MapsOneKeyOntoAnother()
    {
        InteractionFunctions.keyboard_set_map([VkSpace, VkShift]);
        var mapped = Fold(VkSpace);
        Assert.IsTrue(mapped[VkShift], "space should report as shift");
        Assert.IsFalse(mapped[VkSpace], "and no longer as itself");
    }

    /// <summary>
    /// The other half of a rebind. Without suppression the key an action moved off still does it,
    /// and swapping two actions -- jump and shoot, the commonest rebind there is -- is impossible.
    /// </summary>
    [TestMethod]
    public void MapsToNokeyToSuppressAKey()
    {
        InteractionFunctions.keyboard_set_map([VkShift, VkNokey]);
        var mapped = Fold(VkShift);
        Assert.IsFalse(mapped[VkShift]);
        // Not parked in the vk_nokey slot either: keyboard_check sweeps 2..255 to answer vk_nokey,
        // so a press stored there would make "nothing is down" report that something is.
        Assert.IsFalse(mapped[VkNokey]);
        Assert.IsFalse(mapped[VkAnykey]);
    }

    [TestMethod]
    public void SwapsTwoKeys()
    {
        InteractionFunctions.keyboard_set_map([VkZ, VkShift]);
        InteractionFunctions.keyboard_set_map([VkShift, VkZ]);

        var zDown = Fold(VkZ);
        Assert.IsTrue(zDown[VkShift]);
        Assert.IsFalse(zDown[VkZ]);

        var shiftDown = Fold(VkShift);
        Assert.IsTrue(shiftDown[VkZ]);
        Assert.IsFalse(shiftDown[VkShift]);
    }

    /// <summary>
    /// Many-to-one is legal: two bindings for one action is the whole reason the player's controls
    /// are a list per action rather than a single key.
    /// </summary>
    [TestMethod]
    public void TwoKeysCanReportAsOne()
    {
        InteractionFunctions.keyboard_set_map([VkZ, VkShift]);
        InteractionFunctions.keyboard_set_map([VkX, VkShift]);

        Assert.IsTrue(Fold(VkZ)[VkShift]);
        Assert.IsTrue(Fold(VkX)[VkShift]);
        // And one of them being up does not clear the other's press.
        Assert.IsTrue(Fold(VkZ, VkX)[VkShift]);
    }

    [TestMethod]
    public void UnsetMapPutsEveryKeyBack()
    {
        InteractionFunctions.keyboard_set_map([VkSpace, VkShift]);
        InteractionFunctions.keyboard_unset_map([]);

        var mapped = Fold(VkSpace);
        Assert.IsTrue(mapped[VkSpace]);
        Assert.IsFalse(mapped[VkShift]);
    }

    [TestMethod]
    public void GetMapReadsBackWhatWasSet()
    {
        Assert.AreEqual(VkSpace, InteractionFunctions.keyboard_get_map([VkSpace]));
        InteractionFunctions.keyboard_set_map([VkSpace, VkShift]);
        Assert.AreEqual(VkShift, InteractionFunctions.keyboard_get_map([VkSpace]));
    }

    /// <summary>
    /// A game doing arithmetic on a key code is not a reason to take it down, and there is no key
    /// outside the table to remap anyway.
    /// </summary>
    [TestMethod]
    public void OutOfRangeKeysAreIgnoredRatherThanFatal()
    {
        InteractionFunctions.keyboard_set_map([300, VkShift]);
        InteractionFunctions.keyboard_set_map([VkSpace, 300]);
        Assert.AreEqual(300, InteractionFunctions.keyboard_get_map([300]));
        Assert.AreEqual(VkSpace, InteractionFunctions.keyboard_get_map([VkSpace]));
    }
}
