using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// json_stringify, the other half of json_parse. A GMS 2.3+ game's settings and save systems are
/// usually one struct through this pair, so a missing json_stringify is not a missing feature but a
/// dead settings file -- and since a failed call aborts the calling event, tijit engine 2's whole
/// __init__ stopped at its first saveSettingsToFile(), leaving every later global unset.
/// </summary>
[TestClass]
public class JsonStringifyTests
{
    private static string Stringify(object? value)
        => (string)FileFunctions.json_stringify([value])!;

    [TestMethod]
    public void AStructBecomesAJsonObject()
    {
        var settings = new GMLObject
        {
            ["volumeMaster"] = 100.0,
            ["fullscreen"] = 0.0,
            ["name"] = "player"
        };

        Assert.AreEqual("{\"volumeMaster\":100.0,\"fullscreen\":0.0,\"name\":\"player\"}", Stringify(settings));
    }

    [TestMethod]
    public void AnArrayBecomesAJsonArray()
    {
        Assert.AreEqual("[1.0,2.0,\"three\"]", Stringify(new List<object?> { 1.0, 2.0, "three" }));
    }

    [TestMethod]
    public void UndefinedBecomesNull()
    {
        // Which is what json_parse reads back as undefined, so a struct survives the round trip.
        Assert.AreEqual("{\"missing\":null}", Stringify(new GMLObject { ["missing"] = null }));
    }

    [TestMethod]
    public void BoolsAreWrittenAsGmlsOneAndZero()
    {
        // GML has no bool type: true is 1. Writing JSON's `true` here would come back from
        // json_parse as something a game's `if (setting)` still accepts but `setting == 1` doesn't.
        Assert.AreEqual("{\"on\":1,\"off\":0}", Stringify(new GMLObject { ["on"] = true, ["off"] = false }));
    }

    [TestMethod]
    public void NestingSurvivesARoundTripThroughJsonParse()
    {
        var original = new GMLObject
        {
            ["volume"] = 80.0,
            ["keys"] = new List<object?> { "left", "right" },
            ["window"] = new GMLObject { ["scale"] = 2.0 }
        };

        var parsed = (GMLObject)FileFunctions.json_parse([Stringify(original)])!;

        Assert.AreEqual(80.0, parsed["volume"].Conv<double>());
        Assert.AreEqual("right", ((IList<object?>)parsed["keys"]!)[1].Conv<string>());
        Assert.AreEqual(2.0, ((GMLObject)parsed["window"]!)["scale"].Conv<double>());
    }

    [TestMethod]
    public void OutputHasNoWhitespace()
    {
        // Games hash, compare and base64 this text (tijit engine 2 base64s it into Data\cfg), so
        // pretty-printing it would be a behaviour change, not a formatting preference.
        var json = Stringify(new GMLObject { ["a"] = 1.0, ["b"] = new GMLObject { ["c"] = 2.0 } });

        Assert.IsFalse(json.Contains('\n'), json);
        Assert.IsFalse(json.Contains(' '), json);
    }
}
