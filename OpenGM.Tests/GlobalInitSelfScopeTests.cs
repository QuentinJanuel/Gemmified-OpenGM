using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// What <c>self.x = ...</c> means while global init scripts are running. A global script has no
/// instance, and GM's self there is the global scope, so a bare assignment at that level really is a
/// global -- that redirect is deliberate.
///
/// What it must not do is apply to everything else that runs during the same phase. A global script
/// building a struct literal calls a constructor whose self IS the object under construction, and
/// sending its field writes to globals leaves the struct empty with nothing logged. Every struct a
/// GMS 2.3+ game creates from a global script hits this: tijit engine 2's settings struct and all
/// its input binds came out empty, so reading any setting gave undefined.
/// </summary>
[TestClass]
public class GlobalInitSelfScopeTests
{
    [TestCleanup]
    public void TearDown() => VMExecutor.GlobalInit = false;

    [TestMethod]
    public void ATopLevelAssignmentDuringGlobalInitIsGlobal()
    {
        VMExecutor.GlobalInit = true;

        TestUtils.ExecuteScript(
            "someGlobal = 5   // at the top level of a global init script",
            """
            :[0]
            pushi.e 5
            conv.i.v
            pop.v.v self.someGlobal
            """
        );

        Assert.IsTrue(VariableResolver.GlobalVariables.ContainsKey("someGlobal"));
        Assert.AreEqual(5, VariableResolver.GlobalVariables["someGlobal"].Conv<int>());
    }

    [TestMethod]
    public void AStructBuiltDuringGlobalInitKeepsItsOwnFields()
    {
        VMExecutor.GlobalInit = true;
        VariableResolver.GlobalVariables.Remove("volumeMusic");

        TestUtils.ExecuteScript(
            "global.settings = {volumeMusic: 50}   // from a global init script",
            """
            :[0]
            push.i [function]gml_Script___struct_init__
            conv.i.v
            call.i @@NullObject@@(argc=0)
            call.i method(argc=2)
            call.i @@NewGMLObject@@(argc=1)
            pop.v.v global.settings
            b [end]

            > gml_Script___struct_init__ (locals=0, argc=0)
            :[7]
            call.i @@SetStatic@@(argc=0)
            pushi.e 50
            pop.v.i self.volumeMusic
            exit.i

            :[end]
            """
        );

        var settings = (GMLObject)VariableResolver.GlobalVariables["settings"]!;
        Assert.AreEqual(50, settings["volumeMusic"].Conv<int>(), "the field belongs to the struct");
        Assert.IsFalse(VariableResolver.GlobalVariables.ContainsKey("volumeMusic"),
            "and must not have leaked into globals");
    }
}
