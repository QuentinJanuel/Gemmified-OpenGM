using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace OpenGM.Tests;

/// <summary>
/// That the virtual keys above F12 are keys this engine can actually see.
/// </summary>
/// <remarks>
/// They were missing from both halves of the keyboard path -- the browser's code table stopped at
/// F12, and <c>Convert</c> fell through to casting the raw number to a <c>Keys</c> value that means
/// something else entirely. A press on one arrived as nothing.
///
/// Controller-mapping mods depend on them: one maps a controller onto a key no keyboard has, so
/// that binding an action to a pad does not leave the game's own key working as well. They are also
/// ordinary virtual keys, and a game is free to read one.
/// </remarks>
[TestClass]
public class FunctionKeyRangeTests
{
    [TestMethod]
    public void EveryFunctionKeyToF24Converts()
    {
        // F1 is 0x70 and they run consecutively to F24 at 0x87.
        for (var vk = 0x70; vk <= 0x87; vk++)
        {
            var keys = InputHandler.Convert(vk);
            Assert.AreEqual(1, keys.Length, $"vk 0x{vk:X2} should convert to exactly one key");
            var expected = (Keys)((int)Keys.F1 + (vk - 0x70));
            Assert.AreEqual(expected, keys[0], $"vk 0x{vk:X2} converted to the wrong key");
        }
    }

    [TestMethod]
    public void TheKeysTheControlsModUsesAreDistinct()
    {
        // One per action, and nothing may collide: two actions sharing a key would make a pad
        // binding fire both.
        var seen = new HashSet<Keys>();
        for (var vk = 0x7C; vk <= 0x82; vk++)
        {
            Assert.IsTrue(seen.Add(InputHandler.Convert(vk)[0]), $"vk 0x{vk:X2} is not distinct");
        }
    }
}
