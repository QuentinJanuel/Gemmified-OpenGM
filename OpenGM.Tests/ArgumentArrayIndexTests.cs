using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.VirtualMachine;
using System.Collections;

namespace OpenGM.Tests;

/// <summary>
/// Indexing an array that arrived as a script argument: <c>argument0[0]</c>.
///
/// GameMaker compiles that to <c>[array]self.argument0</c> -- instance type *self*, not the
/// argument pseudo-instance -- so it lands on the self-array path, where argumentN used to be
/// answered by the built-in variable table. That table registers argumentN as
/// <c>(_) =&gt; get_argument(0)</c>, a getter which discards the index it is handed, so
/// <c>argument0[0]</c> returned the whole of argument 0. Silently: no error, no warning, just the
/// array itself flowing onwards until something downstream wanted a number and got a GMLArray.
///
/// The assembly here is copied from a real game (The Farewell Medley's scrGetBind, which does
/// <c>scrGetKeybind(argument0[0])</c> to read the first key of a binding), where this drew a black
/// screen every frame: the Draw event died on the array long after the script that should have
/// unwrapped it.
/// </summary>
[TestClass]
public class ArgumentArrayIndexTests
{
    [TestInitialize]
    public void SetUp()
    {
        VariableResolver.GlobalVariables.Clear();
        // 2.3+ reference semantics, so a write through an argument stays visible on the array the
        // caller passed rather than copying it first (see GMLArray).
        CompatFlags.LegacyArrayCopyOnWrite = false;
    }

    [TestCleanup]
    public void TearDown() => VariableResolver.GlobalVariables.Clear();

    private static object? Global(string name) => VariableResolver.GlobalVariables[name];

    private static void Run(string name, string asm, params object?[] args)
    {
        var code = GameConverter.ConvertAssembly(null, asm);
        code.Name = name;
        ScriptResolver.InitGMLFunctions();
        VMExecutor.ExecuteCode(code, null, args: args);
    }

    /// <summary>
    /// Exactly the instruction sequence the game emits: push the owner (-1, self), push the index,
    /// then the array-tagged argument reference.
    /// </summary>
    [TestMethod]
    public void ReadingAnElementOfAnArrayArgumentGivesTheElementNotTheArray()
    {
        Run("argument0[0]",
            """
            :[0]
            pushi.e -1
            pushi.e 0
            push.v [array]self.argument0
            pop.v.v global.result
            """,
            new GMLArray { 65, 66 });

        Assert.AreEqual(65, Global("result").Conv<int>());
        Assert.IsNotInstanceOfType<IList>(Global("result"), "the whole argument came through instead of its element");
    }

    [TestMethod]
    public void ASecondElementReadsAsItself()
    {
        Run("argument0[1]",
            """
            :[0]
            pushi.e -1
            pushi.e 1
            push.v [array]self.argument0
            pop.v.v global.result
            """,
            new GMLArray { 65, 66 });

        Assert.AreEqual(66, Global("result").Conv<int>());
    }

    /// <summary>
    /// The write side was worse than the read: argumentN's registered setter is
    /// <c>(val, _) =&gt; PopToArgument(0, val)</c>, which discards the index too -- so
    /// <c>argument0[1] = 99</c> replaced the entire argument with 99 rather than writing element 1.
    /// Element 0 surviving is what proves it.
    /// </summary>
    [TestMethod]
    public void WritingAnElementOfAnArrayArgumentLeavesTheRestOfItAlone()
    {
        Run("argument0[1] = 99",
            """
            :[0]
            pushi.i 99
            conv.i.v
            pushi.e -1
            pushi.e 1
            pop.v.v [array]self.argument0

            pushi.e -1
            pushi.e 1
            push.v [array]self.argument0
            pop.v.v global.written

            pushi.e -1
            pushi.e 0
            push.v [array]self.argument0
            pop.v.v global.untouched
            """,
            new GMLArray { 65, 66 });

        Assert.AreEqual(99, Global("written").Conv<int>());
        Assert.AreEqual(65, Global("untouched").Conv<int>(), "the whole argument was replaced instead of one element");
    }

    /// <summary>
    /// Bare <c>argument</c> really is an indexable built-in -- <c>argument[i]</c> selects the i'th
    /// argument -- so it must keep going through the built-in table, unlike argumentN.
    /// </summary>
    [TestMethod]
    public void BareArgumentStillIndexesTheArgumentList()
    {
        Run("argument[1]",
            """
            :[0]
            pushi.e -1
            pushi.e 1
            push.v [array]self.argument
            pop.v.v global.result
            """,
            11, 22);

        Assert.AreEqual(22, Global("result").Conv<int>());
    }

    /// <summary>
    /// argument_count and argument_relative start with "argument" but are not numbered arguments;
    /// parsing the tail as an index would throw on them.
    /// </summary>
    [TestMethod]
    public void OnlyNumberedArgumentsAreTreatedAsArguments()
    {
        Assert.IsTrue(VMExecutor.TryGetArgumentIndex("argument0", out var zero) && zero == 0);
        Assert.IsTrue(VMExecutor.TryGetArgumentIndex("argument15", out var fifteen) && fifteen == 15);
        Assert.IsFalse(VMExecutor.TryGetArgumentIndex("argument", out _));
        Assert.IsFalse(VMExecutor.TryGetArgumentIndex("argument_count", out _));
        Assert.IsFalse(VMExecutor.TryGetArgumentIndex("argument_relative", out _));
    }
}
