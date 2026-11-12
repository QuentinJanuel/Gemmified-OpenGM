using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The filename/path family, pinned to a GMS 1.4.9999 capture rather than to intuition. Three of
/// the six do something you would not guess:
///
///   - <c>filename_ext</c> keeps the leading dot.
///   - <c>filename_path</c> and <c>filename_dir</c> differ *only* by a trailing separator -- and
///     the one filename_path appends is a backslash regardless of which slash the input used.
///   - <c>filename_change_ext</c> with an empty extension strips the extension rather than leaving
///     a trailing dot.
/// </summary>
[TestClass]
public class FilenameFunctionsTests
{
    private static string Call(Func<object?[], object> fn, params object?[] args)
        => fn(args).Conv<string>();

    [TestMethod]
    public void MatchesTheCapturedOutputs()
    {
        // Captured: ext=[.txt] name=[c.txt] path=[a/b\] dir=[a/b] drive=[C:] chg=[a/b/c.dat] chgnone=[a/b/c]
        Assert.AreEqual(".txt", Call(IntrospectionFunctions.filename_ext, "a/b/c.txt"));
        Assert.AreEqual("c.txt", Call(IntrospectionFunctions.filename_name, "a/b/c.txt"));
        Assert.AreEqual("a/b\\", Call(IntrospectionFunctions.filename_path, "a/b/c.txt"));
        Assert.AreEqual("a/b", Call(IntrospectionFunctions.filename_dir, "a/b/c.txt"));
        Assert.AreEqual("C:", Call(IntrospectionFunctions.filename_drive, "C:/b/c.txt"));
        Assert.AreEqual("a/b/c.dat", Call(IntrospectionFunctions.filename_change_ext, "a/b/c.txt", ".dat"));
        Assert.AreEqual("a/b/c", Call(IntrospectionFunctions.filename_change_ext, "a/b/c.txt", ""));
    }

    [TestMethod]
    public void HandlesBackslashesAndBareNames()
    {
        Assert.AreEqual("c.txt", Call(IntrospectionFunctions.filename_name, "a\\b\\c.txt"));
        Assert.AreEqual("a\\b", Call(IntrospectionFunctions.filename_dir, "a\\b\\c.txt"));

        // No directory at all.
        Assert.AreEqual("c.txt", Call(IntrospectionFunctions.filename_name, "c.txt"));
        Assert.AreEqual("", Call(IntrospectionFunctions.filename_dir, "c.txt"));
        Assert.AreEqual("", Call(IntrospectionFunctions.filename_path, "c.txt"));
        Assert.AreEqual("", Call(IntrospectionFunctions.filename_drive, "c.txt"));
    }

    [TestMethod]
    public void ADotInADirectoryNameIsNotAnExtension()
    {
        // "my.game/save" has a dot, but it's before the last separator so the *file* has no
        // extension. Scanning the whole path for the last '.' instead of the last path segment is
        // the easy mistake, and it would truncate the directory.
        Assert.AreEqual("", Call(IntrospectionFunctions.filename_ext, "my.game/save"));
        Assert.AreEqual("save", Call(IntrospectionFunctions.filename_name, "my.game/save"));
        Assert.AreEqual("my.game/save.ini", Call(IntrospectionFunctions.filename_change_ext, "my.game/save", ".ini"));
    }

    [TestMethod]
    public void NoExtensionCases()
    {
        Assert.AreEqual("", Call(IntrospectionFunctions.filename_ext, "a/b/c"));
        Assert.AreEqual("a/b/c.dat", Call(IntrospectionFunctions.filename_change_ext, "a/b/c", ".dat"));
    }
}

/// <summary>
/// A guard for the whole builtin table rather than for any one function.
/// </summary>
[TestClass]
public class BuiltinRegistrationTests
{
    [TestMethod]
    public void NoFunctionNameIsRegisteredTwice()
    {
        // Registering the same GML name from two methods doesn't fail loudly where the duplicate
        // is -- it takes down InitGMLFunctions, so the *first unrelated test that runs any
        // bytecode at all* is what goes red. That happened while adding object_set_depth and
        // object_set_persistent here when ResourceFunctions already had them, and the symptom was
        // `1 + 1 = 2` failing.
        var duplicates = typeof(GMLFunctionAttribute).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributes(typeof(GMLFunctionAttribute), false)
                .Cast<GMLFunctionAttribute>()
                .Select(a => new { a.FunctionName, Method = $"{m.DeclaringType!.Name}.{m.Name}" }))
            .GroupBy(x => x.FunctionName)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(x => x.Method))}")
            .ToList();

        Assert.AreEqual(0, duplicates.Count,
            "duplicate [GMLFunction] registrations:\n  " + string.Join("\n  ", duplicates));
    }
}

/// <summary>
/// The asset-introspection additions. These are individually trivial, so rather than restate each
/// implementation the assertions here cover the two things that actually go wrong with a family
/// like this: the "missing asset" answer, and confusing an asset index with a list position.
/// </summary>
[TestClass]
public class AssetIntrospectionTests
{
    [TestMethod]
    public void MissingAssetsAnswerConsistently()
    {
        // Every *_get_name in the engine answers "<undefined>" for an asset that isn't there, and
        // these have to match -- a game printing the name of a deleted asset should get GM's string,
        // not "" or a crash.
        Assert.AreEqual("<undefined>", AssetIntrospectionFunctions.font_name([9999]).Conv<string>());
        Assert.AreEqual("<undefined>", AssetIntrospectionFunctions.room_name([9999]).Conv<string>());
        Assert.AreEqual("<undefined>", AssetIntrospectionFunctions.sprite_name([9999]).Conv<string>());
        Assert.AreEqual("<undefined>", AssetIntrospectionFunctions.background_get_name([9999]).Conv<string>());

        Assert.IsFalse(AssetIntrospectionFunctions.font_exists([9999]).Conv<bool>());
        Assert.AreEqual(-1, AssetIntrospectionFunctions.font_get_first([9999]).Conv<int>());
        Assert.AreEqual(-1, AssetIntrospectionFunctions.font_get_last([9999]).Conv<int>());
    }

    [TestMethod]
    public void FontsAreFoundByAssetIndexNotListPosition()
    {
        // TextManager.FontAssets is a List, so the easy mistake is indexing it directly. Asset
        // indices are not list positions -- a game with fonts added at runtime, or any gap in the
        // asset table, would then get the wrong font (or an exception).
        TextManager.FontAssets.Clear();
        try
        {
            TextManager.FontAssets.Add(new FontAsset { AssetIndex = 40, name = "fntBig", spriteIndex = -1 });
            TextManager.FontAssets.Add(new FontAsset { AssetIndex = 7, name = "fntSmall", spriteIndex = -1 });

            Assert.AreEqual("fntSmall", AssetIntrospectionFunctions.font_name([7]).Conv<string>());
            Assert.AreEqual("fntBig", AssetIntrospectionFunctions.font_name([40]).Conv<string>());
            Assert.IsTrue(AssetIntrospectionFunctions.font_exists([40]).Conv<bool>());
            // Index 1 is a valid *list position* but not a valid asset index.
            Assert.IsFalse(AssetIntrospectionFunctions.font_exists([1]).Conv<bool>());
        }
        finally
        {
            TextManager.FontAssets.Clear();
        }
    }

    [TestMethod]
    public void FontGlyphRangeComesFromTheGlyphsPresent()
    {
        TextManager.FontAssets.Clear();
        try
        {
            var font = new FontAsset { AssetIndex = 3, name = "fnt", spriteIndex = -1 };
            font.entries.Add(new Glyph { characterIndex = 65 });
            font.entries.Add(new Glyph { characterIndex = 32 });
            font.entries.Add(new Glyph { characterIndex = 122 });
            TextManager.FontAssets.Add(font);

            // Min/max over the glyphs actually present, not first/last in the list.
            Assert.AreEqual(32, AssetIntrospectionFunctions.font_get_first([3]).Conv<int>());
            Assert.AreEqual(122, AssetIntrospectionFunctions.font_get_last([3]).Conv<int>());
        }
        finally
        {
            TextManager.FontAssets.Clear();
        }
    }
}
