using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// A struct member whose name collides with an INSTANCE builtin -- `x` and `y` above all, which is
/// what any vector struct is made of. The compiler emits those names in builtin scope no matter what
/// self turns out to be at runtime, so reading one inside a struct method used to run the
/// GamemakerObject getter against a struct and die on the cast (InvalidCastException), taking the
/// calling event with it.
///
/// tijit engine 2's whole vector library is `vec2` structs with x/y members, and its menus, spikes
/// and vines are built on them: this was ~450 aborted events in a single run.
/// </summary>
[TestClass]
public class StructBuiltinNameTests
{
    /// <summary>
    /// `{x: 7, y: 9}` built by a constructor that then reads its own x and y back through builtin
    /// scope -- which is exactly how those reads compile inside the struct's own methods.
    /// </summary>
    private const string Asm = """
        :[0]
        push.i [function]gml_Script___vecstruct__
        conv.i.v
        call.i @@NullObject@@(argc=0)
        call.i method(argc=2)
        call.i @@NewGMLObject@@(argc=1)
        pop.v.v global.vec
        b [end]

        > gml_Script___vecstruct__ (locals=0, argc=0)
        :[7]
        pushi.e 7
        pop.v.i self.x
        pushi.e 9
        pop.v.i self.y
        pushbltn.v builtin.x
        pushbltn.v builtin.y
        add.v.v
        pop.v.v global.sum
        exit.i

        :[end]
        """;

    [TestMethod]
    public void AStructsOwnXAndYAreReadableFromBuiltinScope()
    {
        TestUtils.ExecuteScript("struct member x/y read through builtin scope", Asm);

        Assert.AreEqual(16, VariableResolver.GlobalVariables["sum"].Conv<int>());
    }

    [TestMethod]
    public void TheStructKeepsItsOwnValues()
    {
        TestUtils.ExecuteScript("struct member x/y read through builtin scope", Asm);

        var vec = (GMLObject)VariableResolver.GlobalVariables["vec"]!;
        Assert.AreEqual(7, vec["x"].Conv<int>());
        Assert.AreEqual(9, vec["y"].Conv<int>());
    }
}
