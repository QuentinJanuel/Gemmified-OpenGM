using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// A GMS 2.3+ struct literal (<c>global.settings = {volumeMusic: 50}</c>), which does not compile to
/// anything as direct as it reads: the compiler emits an anonymous constructor function, binds it
/// with <c>method()</c>, and calls <c>@@NewGMLObject@@</c> on the result -- and the constructor body
/// opens with <c>@@SetStatic@@</c>. This is the shape tijit engine 2's settings struct uses, and
/// every read of a setting goes through the object it produces.
/// </summary>
[TestClass]
public class StructLiteralTests
{
    [TestMethod]
    public void AStructLiteralKeepsItsFields()
    {
        TestUtils.ExecuteScript(
            "global.settings = {volumeMusic: 50, name: \"tijit\"}",
            """
            :[0]
            push.i [function]gml_Script___struct__
            conv.i.v
            call.i @@NullObject@@(argc=0)
            call.i method(argc=2)
            dup.v 0
            pop.v.v global.__struct__ref
            call.i @@NewGMLObject@@(argc=1)
            pop.v.v global.settings
            b [end]

            > gml_Script___struct__ (locals=0, argc=0)
            :[9]
            call.i @@SetStatic@@(argc=0)
            pushi.e 50
            pop.v.i self.volumeMusic
            pushi.s "tijit"@0
            pop.v.s self.name
            exit.i

            :[end]
            """
        );

        Assert.IsTrue(VariableResolver.GlobalVariables.ContainsKey("settings"), "settings was never assigned");

        var settings = (GMLObject)VariableResolver.GlobalVariables["settings"]!;
        Assert.AreEqual(50, settings["volumeMusic"].Conv<int>());
        Assert.AreEqual("tijit", settings["name"].Conv<string>());
    }
}
