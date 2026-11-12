using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTK.Mathematics;

namespace OpenGM.Tests;

/// <summary>
/// The sign convention the fog shader depends on. gpu_set_fog's start/end are distances in FRONT of
/// the camera, but GraphicsManager.BuildViewMatrices builds a GL view matrix (eye at z = +16000
/// looking down -z), so everything visible has NEGATIVE view-space z -- GM's own D3D view matrix is
/// left-handed and measures the same distance as a positive number. shader.vsh's CalcFogFactor
/// therefore has to negate view z, and did not: every fog factor came out hugely negative, clamped
/// to zero, so gpu_set_fog silently did nothing at all.
///
/// The shader itself is GLSL and can't be run here, so these pin the matrix half of it and evaluate
/// CalcFogFactor's arithmetic on the result.
/// </summary>
[TestClass]
public class FogDistanceTests
{
    private static readonly Vector4 View = new(0, 0, 800, 608);

    /// <summary>View-space z of a room point, exactly as gm_Matrices[MATRIX_WORLD_VIEW] * pos gives it.</summary>
    private static float ViewZ(float roomX, float roomY, float roomZ = 0)
    {
        var (view, _) = GraphicsManager.BuildViewMatrices(View.X, View.Y, View.Z, View.W, 0, flipY: true);
        return (new Vector4(roomX, roomY, roomZ, 1) * view).Z;
    }

    /// <summary>CalcFogFactor, as the shader computes it from a view-space z.</summary>
    private static float FogFactor(float viewZ, float fogStart, float fogEnd)
        => (-viewZ - fogStart) * (1f / (fogEnd - fogStart));

    [TestMethod]
    public void OrdinaryTwoDGeometrySitsSixteenThousandInFrontOfTheCamera()
    {
        // Negative because this is a GL view matrix; 16000 because that's where BuildViewMatrices
        // puts the eye. Both halves matter: the magnitude is what makes fog ranges behave the way
        // they do in 2D, and the sign is what the shader has to undo.
        Assert.AreEqual(-16000f, ViewZ(0, 0), 0.01f, "room origin");
        Assert.AreEqual(-16000f, ViewZ(400, 304), 0.01f, "view centre");
        Assert.AreEqual(-16000f, ViewZ(5000, 5000), 0.01f, "well outside the view");
    }

    [TestMethod]
    public void ATightFogRangeFullyRecoloursTwoDGeometry()
    {
        // tijit engine 2 tints its tile mesh by calling gpu_set_fog(true, colour, -1, 1): 2D
        // geometry is 16000 away, far past a range that ends at 1, so the fog colour completely
        // replaces the sampled texel. That trick is load-bearing for the game's whole look, and
        // with the sign wrong the factor was -7999.5, i.e. no fog whatsoever.
        var factor = FogFactor(ViewZ(400, 304), -1, 1);

        Assert.IsTrue(factor >= 1f, $"expected fully fogged, got {factor}");
    }

    [TestMethod]
    public void AFogRangeSpanningTheTwoDDepthBlendsHalfway()
    {
        // The other side of the same coin: a game asking for fog over 0..32000 in 2D gets a 50%
        // haze, because that's where 2D geometry sits in the range. Real GM does this too -- it is
        // not a bug to "fix" by special-casing 2D.
        var factor = FogFactor(ViewZ(400, 304), 0, 32000);

        Assert.AreEqual(0.5f, factor, 0.001f);
    }

    [TestMethod]
    public void GeometryNearerThanTheFogStartIsUnfogged()
    {
        // Anything closer to the camera than fog start gives a negative factor, which the shader
        // clamps to 0. Fed a view-space z directly rather than a room z, because which way round
        // room z runs relative to the eye is a separate question from this arithmetic.
        var factor = FogFactor(-100, 500, 4000);

        Assert.IsTrue(factor < 0f, $"expected unfogged, got {factor}");
    }
}
