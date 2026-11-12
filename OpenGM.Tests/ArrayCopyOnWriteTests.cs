using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using System.Collections;

namespace OpenGM.Tests;

/// <summary>
/// Pre-2.3 GML arrays are value types (see <see cref="GMLArray"/>). The assembly in these tests is
/// the shape real bytecode-16 games actually produce -- it's taken from TweenGMS, the tweening
/// library a lot of GMS1 games ship, whose whole tween pipeline depends on both halves of this
/// behaviour: it stamps out each tween record by assigning one shared template array and writing an
/// element of the copy, then mutates the record it stored in a ds_list through array_set.
/// </summary>
[TestClass]
public class ArrayCopyOnWriteTests
{
    private bool _previousFlag;

    [TestInitialize]
    public void SetUp()
    {
        // Nothing loads a game here, so CompatFlags.Init never runs and the flag keeps its default.
        _previousFlag = CompatFlags.LegacyArrayCopyOnWrite;
        VariableResolver.GlobalVariables.Clear();
    }

    [TestCleanup]
    public void TearDown()
    {
        CompatFlags.LegacyArrayCopyOnWrite = _previousFlag;
        VariableResolver.GlobalVariables.Clear();
    }

    private static IList Global(string name) => VariableResolver.GlobalVariables[name].Conv<IList>();

    /// <summary>
    /// global.template[0] = 10; global.copy = global.template; global.copy[0] = 99;
    /// </summary>
    private const string AssignThenWriteElement =
        """
        :[0]
        pushi.i 10
        conv.i.v
        pushi.e -5
        pushi.e 0
        pop.v.v [array]global.template

        pushglb.v global.template
        pop.v.v global.copy

        pushi.i 99
        conv.i.v
        pushi.e -5
        pushi.e 0
        pop.v.v [array]global.copy
        """;

    [TestMethod]
    public void PlainWriteToAnAssignedArrayCopiesItFirst()
    {
        CompatFlags.LegacyArrayCopyOnWrite = true;

        TestUtils.ExecuteScript("global.copy = global.template; global.copy[0] = 99", AssignThenWriteElement);

        Assert.AreEqual(99, Global("copy")[0].Conv<int>());
        Assert.AreEqual(10, Global("template")[0].Conv<int>(), "assigning an array must not alias it");
        Assert.IsFalse(ReferenceEquals(Global("copy"), Global("template")));
    }

    [TestMethod]
    public void PlainWriteToAnAssignedArrayAliasesItFrom23Onwards()
    {
        CompatFlags.LegacyArrayCopyOnWrite = false;

        TestUtils.ExecuteScript("global.copy = global.template; global.copy[0] = 99", AssignThenWriteElement);

        Assert.AreEqual(99, Global("copy")[0].Conv<int>());
        Assert.AreEqual(99, Global("template")[0].Conv<int>(), "2.3+ arrays are references");
        Assert.IsTrue(ReferenceEquals(Global("copy"), Global("template")));
    }

    [TestMethod]
    public void ArraySetWritesThroughAnAssignedArrayInPlace()
    {
        CompatFlags.LegacyArrayCopyOnWrite = true;
        ScriptResolver.InitGMLFunctions();

        // Same as above, except the final write goes through array_set -- i.e. the `arr[@ 0] = 99`
        // accessor, which exists precisely to skip the copy. This is how a library mutates a record
        // it handed to a ds_list/ds_map, so it has to stay visible through every reference to it.
        TestUtils.ExecuteScript(
            "global.copy = global.template; array_set(global.copy, 0, 99)",
            """
            :[0]
            pushi.i 10
            conv.i.v
            pushi.e -5
            pushi.e 0
            pop.v.v [array]global.template

            pushglb.v global.template
            pop.v.v global.copy

            pushi.i 99
            conv.i.v
            pushi.i 0
            conv.i.v
            pushglb.v global.copy
            call.i array_set(argc=3)
            popz.v
            """
        );

        Assert.AreEqual(99, Global("copy")[0].Conv<int>());
        Assert.AreEqual(99, Global("template")[0].Conv<int>(), "array_set must not copy");
        Assert.IsTrue(ReferenceEquals(Global("copy"), Global("template")));
    }

    [TestMethod]
    public void ConsecutiveWritesToAnArrayAVariableOwnsDontKeepCopying()
    {
        CompatFlags.LegacyArrayCopyOnWrite = true;

        // Building up an array element by element is the other half of the TweenGMS pattern (that's
        // how it initialises the shared template), and there's nothing to copy away from there --
        // only the first write after an assignment may copy, or a loop filling an array would be
        // quadratic.
        TestUtils.ExecuteScript(
            "global.template[0] = 10; global.template[1] = 20",
            """
            :[0]
            pushi.i 10
            conv.i.v
            pushi.e -5
            pushi.e 0
            pop.v.v [array]global.template
            """
        );

        var beforeSecondWrite = Global("template");

        TestUtils.ExecuteScript(
            "global.template[1] = 20",
            """
            :[0]
            pushi.i 20
            conv.i.v
            pushi.e -5
            pushi.e 1
            pop.v.v [array]global.template
            """
        );

        Assert.IsTrue(ReferenceEquals(beforeSecondWrite, Global("template")));
        Assert.AreEqual(10, Global("template")[0].Conv<int>());
        Assert.AreEqual(20, Global("template")[1].Conv<int>());
    }
}
