using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// Verifies that the env-stack layout the collision dispatch builds lets VMExecutor.Other
/// resolve to the collided instance (not back to self). Regression test for jtool spikes not
/// reddening because `with (other)` inside a collision event targeted self.
/// </summary>
[TestClass]
public class CollisionOtherTests
{
    private static VMEnvFrame Frame() => new VMEnvFrame { Self = new GMLObject() };

    [TestInitialize]
    public void Reset() => VMExecutor.EnvStack.Clear();

    // Layout the FIXED collision dispatch produces while the event body runs, as seen mid-PUSHENV
    // for `with (other)` (PUSHENV has already pushed its own null marker on top):
    //   top -> [null(new block), self, null(dispatch marker), other] <- bottom
    [TestMethod]
    public void Other_WithNullMarker_ResolvesToOther()
    {
        var other = Frame();
        var self = Frame();

        VMExecutor.EnvStack.Push(other);      // collide (dispatch pushes this first)
        VMExecutor.EnvStack.Push(null);       // dispatch null marker (the fix)
        VMExecutor.EnvStack.Push(self);       // event self (ExecuteCode pushes this)
        VMExecutor.EnvStack.Push(null);       // PUSHENV marker for `with (other)`

        Assert.AreSame(other.Self, VMExecutor.Other.Self);
    }

    // Layout the OLD (buggy) dispatch produced -- no null marker between self and other. Documents
    // that Other wrongly walked past `other` and fell back to self.
    [TestMethod]
    public void Other_WithoutNullMarker_WronglyResolvesToSelf()
    {
        var other = Frame();
        var self = Frame();

        VMExecutor.EnvStack.Push(other);
        VMExecutor.EnvStack.Push(self);
        VMExecutor.EnvStack.Push(null);       // PUSHENV marker for `with (other)`

        Assert.AreSame(self.Self, VMExecutor.Other.Self);
    }

    // Direct read of `other` (no surrounding with-block) with the fixed layout.
    [TestMethod]
    public void Other_DirectRead_WithNullMarker_ResolvesToOther()
    {
        var other = Frame();
        var self = Frame();

        VMExecutor.EnvStack.Push(other);
        VMExecutor.EnvStack.Push(null);
        VMExecutor.EnvStack.Push(self);

        Assert.AreSame(other.Self, VMExecutor.Other.Self);
    }
}
