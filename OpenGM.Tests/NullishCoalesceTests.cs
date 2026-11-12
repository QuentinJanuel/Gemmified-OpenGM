using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// GML's <c>??</c> operator, which compiles to CHKNULLISH plus a branch. CHKNULLISH *peeks* -- the
/// value it tests is also the operator's result when that value isn't nullish -- and it used to pop,
/// which left both paths of every <c>a ?? b</c> one value short of what the compiler emitted. The
/// symptom is one instruction later and looks unrelated: "InvalidOperation_EmptyStack" on the popz,
/// which aborts the whole calling event.
///
/// The asm below is the shape UndertaleModTool disassembles tijit engine 2's settings accessor to
/// (<c>arg1 ?? variable_struct_get(global.settingsStruct, arg0)</c>), with the argument replaced by
/// a global so it can be driven from a test.
/// </summary>
[TestClass]
public class NullishCoalesceTests
{
    private const string Asm = """
        :[0]
        pushglb.v global.maybe
        chknullish.e
        bf [1]
        popz.v
        pushi.e 42
        conv.i.v

        :[1]
        pop.v.v global.result
        """;

    [TestMethod]
    public void TheLeftHandSideWinsWhenItIsNotNullish()
    {
        VariableResolver.GlobalVariables["maybe"] = 7;

        TestUtils.ExecuteScript("global.result = global.maybe ?? 42", Asm);

        Assert.AreEqual(7, VariableResolver.GlobalVariables["result"].Conv<int>());
    }

    [TestMethod]
    public void TheRightHandSideIsUsedWhenTheLeftIsUndefined()
    {
        VariableResolver.GlobalVariables["maybe"] = null;

        TestUtils.ExecuteScript("global.result = global.maybe ?? 42", Asm);

        Assert.AreEqual(42, VariableResolver.GlobalVariables["result"].Conv<int>());
    }

    [TestMethod]
    public void ZeroAndEmptyStringAreNotNullish()
    {
        // ?? is about undefined, not falsiness -- a game reading a saved volume of 0 must get 0
        // back, not the default it would fall back to.
        VariableResolver.GlobalVariables["maybe"] = 0;
        TestUtils.ExecuteScript("global.result = global.maybe ?? 42", Asm);
        Assert.AreEqual(0, VariableResolver.GlobalVariables["result"].Conv<int>());

        VariableResolver.GlobalVariables["maybe"] = "";
        TestUtils.ExecuteScript("global.result = global.maybe ?? 42", Asm);
        Assert.AreEqual("", VariableResolver.GlobalVariables["result"].Conv<string>());
    }
}
