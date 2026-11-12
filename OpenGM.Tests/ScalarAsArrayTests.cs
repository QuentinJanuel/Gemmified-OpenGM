using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// Before 2.3, GML has no separate array type: a variable and element 0 of an array of the same
/// name are one and the same storage, so <c>v[0]</c> reads <c>v</c> even when nothing ever made it
/// an array. OpenGMK says as much in its own accessor -- "Variables with 0D arrays, and ones with
/// no array accessor, implicitly refer to `x[0]`".
///
/// The assembly here is I Wanna Be The Ocean Princess' scrButtonCheck (bytecode 16, GameMaker
/// Studio 1), which takes a button and indexes it immediately. Converting the plain number in
/// global.upButton to an IList threw, and a throw out of an instruction aborts the calling event --
/// so the title menu and the world controller both lost their Step event on every frame, for every
/// input they polled.
/// </summary>
[TestClass]
public class ScalarAsArrayTests
{
    private bool _previousFlag;

    [TestInitialize]
    public void SetUp()
    {
        // Nothing loads a game here, so CompatFlags.Init never runs and the flag keeps its default.
        _previousFlag = CompatFlags.ScalarIsFirstArrayElement;
        VariableResolver.GlobalVariables.Clear();
    }

    [TestCleanup]
    public void TearDown()
    {
        CompatFlags.ScalarIsFirstArrayElement = _previousFlag;
        VariableResolver.GlobalVariables.Clear();
    }

    /// <summary>
    /// The failing instruction from scrButtonCheck, in its own shape: a local holding a plain
    /// value, indexed at 0. (The game puts argument0 in that local first; how the value got there
    /// is not what breaks, so these set it directly rather than dragging a self context in.)
    /// </summary>
    private static string IndexLocalAt(int index) =>
        $"""
        :[0]
        pushi.i 90
        conv.i.v
        pop.v.v local.buttonIn
        pushi.e -7
        pushi.e {index}
        push.v [array]local.buttonIn
        pop.v.v global.result
        """;

    /// <summary>The same for a global, which is where the game's buttons actually live.</summary>
    private const string IndexGlobalAtZero =
        """
        :[0]
        pushi.i 90
        conv.i.v
        pop.v.v global.upButton
        pushi.e -5
        pushi.e 0
        push.v [array]global.upButton
        pop.v.v global.result
        """;

    private static void Run(string name, string asm, params object?[] args)
    {
        var code = GameConverter.ConvertAssembly(null, asm);
        code.Name = name;
        ScriptResolver.InitGMLFunctions();
        VMExecutor.ExecuteCode(code, null, args: args.Length == 0 ? null : args);
    }

    [TestMethod]
    public void ElementZeroOfAPlainValueIsTheValue()
    {
        CompatFlags.ScalarIsFirstArrayElement = true;

        Run("buttonIn[0] where buttonIn is a number", IndexLocalAt(0));

        Assert.AreEqual(90, VariableResolver.GlobalVariables["result"].Conv<int>());
    }

    /// <summary>
    /// A one-element array has nothing at index 1, and reading past the end is undefined rather
    /// than an error -- the same answer SafeArrayIndex gives for a real array.
    /// </summary>
    [TestMethod]
    public void AnyOtherIndexOfAPlainValueIsUndefined()
    {
        CompatFlags.ScalarIsFirstArrayElement = true;

        Run("buttonIn[1] where buttonIn is a number", IndexLocalAt(1));

        Assert.IsNull(VariableResolver.GlobalVariables["result"]);
    }

    /// <summary>
    /// A global holding a plain value reads the same way -- this is where the game's buttons live.
    /// </summary>
    [TestMethod]
    public void ElementZeroOfAPlainGlobalIsTheGlobal()
    {
        CompatFlags.ScalarIsFirstArrayElement = true;

        Run("global.upButton[0] where upButton is a number", IndexGlobalAtZero);

        Assert.AreEqual(90, VariableResolver.GlobalVariables["result"].Conv<int>());
    }

    /// <summary>
    /// A real array is untouched by any of this.
    /// </summary>
    [TestMethod]
    public void AnActualArrayStillIndexesNormally()
    {
        CompatFlags.ScalarIsFirstArrayElement = true;

        Run("arr[0] where arr really is an array",
            """
            :[0]
            pushi.i 65
            conv.i.v
            pushi.e -5
            pushi.e 0
            pop.v.v [array]global.arr
            pushi.i 66
            conv.i.v
            pushi.e -5
            pushi.e 1
            pop.v.v [array]global.arr

            pushi.e -5
            pushi.e 1
            push.v [array]global.arr
            pop.v.v global.result
            """);

        Assert.AreEqual(66, VariableResolver.GlobalVariables["result"].Conv<int>());
    }

    /// <summary>
    /// From 2.3 an array is a real type and indexing a number is a genuine error, so the engine
    /// must not quietly answer something -- that would hide a real bug in a modern game.
    /// </summary>
    [TestMethod]
    public void From23IndexingAPlainValueIsStillAnError()
    {
        CompatFlags.ScalarIsFirstArrayElement = false;

        Run("buttonIn[0] on 2.3+", IndexLocalAt(0));

        Assert.IsFalse(VariableResolver.GlobalVariables.ContainsKey("result"),
            "the instruction should have failed rather than storing a value");
    }
}
