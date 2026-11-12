using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;

namespace OpenGM.Tests;

/// <summary>
/// The GLSL ES 1.00 -> 3.00 rewrite that a game shader gets as a second chance when it doesn't
/// compile as shipped (see ShaderManager.CompileGameShader). The sources here are shaped like
/// GameMaker's own GLSL ES output, which is what the browser build actually feeds WebGL2.
/// </summary>
[TestClass]
public class GlslEs300UpgradeTests
{
    // GameMaker's default fragment shader, verbatim in shape.
    private const string PassthroughFragment = """
        //
        // Simple passthrough fragment shader
        //
        varying vec2 v_vTexcoord;
        varying vec4 v_vColour;

        void main()
        {
            gl_FragColor = v_vColour * texture2D( gm_BaseTexture, v_vTexcoord );
        }
        """;

    private const string PassthroughVertex = """
        attribute vec3 in_Position;
        attribute vec4 in_Colour;
        attribute vec2 in_TextureCoord;

        varying vec2 v_vTexcoord;
        varying vec4 v_vColour;

        void main()
        {
            gl_Position = gm_Matrices[MATRIX_WORLD_VIEW_PROJECTION] * vec4(in_Position, 1.0);
            v_vColour = in_Colour;
            v_vTexcoord = in_TextureCoord;
        }
        """;

    [TestMethod]
    public void AFragmentShaderGetsTheVersionInsOutsAndSamplerCallsOfEs300()
    {
        Assert.IsTrue(GlslEs300Upgrade.TryUpgrade(PassthroughFragment, fragment: true, out var upgraded));

        StringAssert.StartsWith(upgraded, "#version 300 es\n");
        StringAssert.Contains(upgraded, "in vec2 v_vTexcoord;");
        StringAssert.Contains(upgraded, "in vec4 v_vColour;");
        StringAssert.Contains(upgraded, "out vec4 gm_FragColor;");
        StringAssert.Contains(upgraded, "gm_FragColor = v_vColour * texture( gm_BaseTexture, v_vTexcoord );");
        Assert.IsFalse(upgraded.Contains("varying"), "varying is not a thing in ES 3.00");
        Assert.IsFalse(upgraded.Contains("gl_FragColor"), "gl_FragColor is not a thing in ES 3.00");
        Assert.IsFalse(upgraded.Contains("texture2D"), "texture2D is not a thing in ES 3.00");
    }

    [TestMethod]
    public void AVertexShadersAttributesBecomeInsAndItsVaryingsBecomeOuts()
    {
        Assert.IsTrue(GlslEs300Upgrade.TryUpgrade(PassthroughVertex, fragment: false, out var upgraded));

        StringAssert.StartsWith(upgraded, "#version 300 es\n");
        StringAssert.Contains(upgraded, "in vec3 in_Position;");
        StringAssert.Contains(upgraded, "out vec2 v_vTexcoord;");
        StringAssert.Contains(upgraded, "out vec4 v_vColour;");
        // The vertex stage keeps gl_Position; only the fragment output went away.
        StringAssert.Contains(upgraded, "gl_Position =");
        Assert.IsFalse(upgraded.Contains("attribute"));
        Assert.IsFalse(upgraded.Contains("varying"));
    }

    /// <summary>
    /// The whole point of the exercise: ES 1.00 rejects indexing an array with anything but a
    /// constant or a for-loop index ("'i' : Index expression can only contain const or loop
    /// symbols"), which is what killed shd_geo. The rewrite has to leave that code alone -- it is
    /// the version bump that makes it legal, not an edit.
    /// </summary>
    [TestMethod]
    public void TheDynamicallyIndexedLoopThisExistsForSurvivesUntouched()
    {
        const string source = """
            precision highp float;
            varying vec2 v_vTexcoord;
            uniform int u_count;
            uniform vec4 u_samples[16];

            void main()
            {
                vec4 sum = vec4(0.0);
                for (int i = 0; i < 16; i++)
                {
                    if (i >= u_count) break;
                    sum += u_samples[i];
                }
                gl_FragColor = sum;
            }
            """;

        Assert.IsTrue(GlslEs300Upgrade.TryUpgrade(source, fragment: true, out var upgraded));

        StringAssert.Contains(upgraded, "sum += u_samples[i];");
        StringAssert.Contains(upgraded, "for (int i = 0; i < 16; i++)");
        StringAssert.Contains(upgraded, "uniform vec4 u_samples[16];");
    }

    /// <summary>
    /// The out declaration is a vec4, so it is only legal once a default float precision is in
    /// scope -- it has to land after the shader's own precision statements, not at the top.
    /// </summary>
    [TestMethod]
    public void TheFragmentOutputIsDeclaredAfterThePrecisionStatements()
    {
        const string source = """
            precision mediump float;
            precision mediump int;
            varying vec4 v_vColour;
            void main() { gl_FragColor = v_vColour; }
            """;

        Assert.IsTrue(GlslEs300Upgrade.TryUpgrade(source, fragment: true, out var upgraded));

        var precision = upgraded.LastIndexOf("precision mediump int;", System.StringComparison.Ordinal);
        var declaration = upgraded.IndexOf("out vec4 gm_FragColor;", System.StringComparison.Ordinal);
        Assert.IsTrue(precision >= 0 && declaration > precision,
            $"the output must be declared after the precision statements:\n{upgraded}");
        // And exactly one default float precision -- adding a second would not compile.
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(upgraded, @"precision \w+ float;").Count);
    }

    [TestMethod]
    public void AFragmentShaderWithNoPrecisionOfItsOwnGetsOne()
    {
        Assert.IsTrue(GlslEs300Upgrade.TryUpgrade(PassthroughFragment, fragment: true, out var upgraded));

        var precision = upgraded.IndexOf("precision highp float;", System.StringComparison.Ordinal);
        var declaration = upgraded.IndexOf("out vec4 gm_FragColor;", System.StringComparison.Ordinal);
        Assert.IsTrue(precision >= 0, "a fragment shader needs a default float precision");
        Assert.IsTrue(declaration > precision);
    }

    /// <summary>
    /// Word boundaries, not substrings: rewriting the middle of an identifier would break a shader
    /// that needed nothing but the version bump.
    /// </summary>
    [TestMethod]
    public void IdentifiersThatMerelyContainAKeywordAreLeftAlone()
    {
        const string source = """
            precision highp float;
            varying vec2 varyingScale;
            uniform float attributeWeight;
            void main() { gl_FragColor = vec4(varyingScale * attributeWeight, 0.0, 1.0); }
            """;

        Assert.IsTrue(GlslEs300Upgrade.TryUpgrade(source, fragment: true, out var upgraded));

        StringAssert.Contains(upgraded, "in vec2 varyingScale;");
        StringAssert.Contains(upgraded, "uniform float attributeWeight;");
    }

    [TestMethod]
    public void AnExplicitVersion100IsReplacedRatherThanLeftToClashWithTheNewOne()
    {
        Assert.IsTrue(GlslEs300Upgrade.TryUpgrade("#version 100\n" + PassthroughFragment, fragment: true, out var upgraded));

        StringAssert.StartsWith(upgraded, "#version 300 es\n");
        Assert.IsFalse(upgraded.Contains("#version 100"));
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(upgraded, "#version").Count);
    }

    /// <summary>
    /// A shader that already states a version this doesn't understand is not ours to rewrite --
    /// ShaderManager then goes straight to the default-shader fallback.
    /// </summary>
    [TestMethod]
    public void AShaderThatIsAlreadySomeOtherVersionIsDeclined()
    {
        Assert.IsFalse(GlslEs300Upgrade.TryUpgrade("#version 330\nvoid main() {}", fragment: true, out _));
        Assert.IsFalse(GlslEs300Upgrade.TryUpgrade("#version 300 es\nvoid main() {}", fragment: true, out _));
        Assert.IsFalse(GlslEs300Upgrade.TryUpgrade("", fragment: true, out _));
    }
}
