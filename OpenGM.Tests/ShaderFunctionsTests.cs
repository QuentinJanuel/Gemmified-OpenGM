using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// shaders_are_supported() used to be entirely unregistered. That's fatal well beyond "a shader
/// effect is missing": several real games gate their very first room transition behind
/// `if (shaders_are_supported() &amp;&amp; every shader_is_compiled(...)) room_goto(next);`, and a missing
/// function aborts the whole calling event (VMExecutor.cs) before that room_goto ever runs -- so
/// the game is stuck on a black screen forever, not just missing a visual effect.
/// </summary>
[TestClass]
public class ShaderFunctionsTests
{
    [TestMethod]
    public void ShadersAreSupportedIsTrue()
    {
        // We only ever run on top of WebGL, which guarantees GLSL ES shader support.
        Assert.AreEqual(true, ShaderFunctions.shaders_are_supported([]));
    }
}
