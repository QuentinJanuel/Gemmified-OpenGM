using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenTK.Mathematics;

namespace OpenGM.Tests;

/// <summary>
/// view_angle rotates the whole view (games use it to keep the picture upright while flipping
/// gravity). The angle was previously carried on the view but never fed into the matrices that
/// actually draw the room, so nothing rotated. These cover the two bits of maths behind that:
/// where a room point lands on screen for a given angle, and how far a rotated view can see.
/// </summary>
[TestClass]
public class ViewAngleTests
{
    // A 1024x576 view onto the top-left of the room, i.e. what a GMS 1.4 game's view 0 usually is.
    private static readonly Vector4 View = new(0, 0, 1024, 576);

    /// <summary>
    /// Where a room-space point ends up in normalised device coordinates: (-1,-1) is one framebuffer
    /// corner and (1,1) the opposite one. Row-vector convention (p * world * view * proj), matching
    /// how <see cref="GraphicsManager.SetViewArea"/> chains its own matrices.
    /// </summary>
    private static Vector2 ToNdc(float roomX, float roomY, float angle, bool flipY = false)
    {
        var (view, proj) = GraphicsManager.BuildViewMatrices(View.X, View.Y, View.Z, View.W, angle, flipY);
        var clip = new Vector4(roomX, roomY, 0, 1) * view * proj;
        return new Vector2(clip.X, clip.Y);
    }

    private static void AssertNdc(Vector2 expected, Vector2 actual, string message)
    {
        Assert.AreEqual(expected.X, actual.X, 0.0001f, $"{message} (x)");
        Assert.AreEqual(expected.Y, actual.Y, 0.0001f, $"{message} (y)");
    }

    [TestMethod]
    public void UnrotatedViewFillsTheScreen()
    {
        // Room y grows downwards but NDC y grows upwards, hence the top of the room landing at -1:
        // that's the un-flipped (surface) orientation the room is rendered in, not a rotation.
        AssertNdc(new Vector2(-1, -1), ToNdc(0, 0, 0), "room top-left");
        AssertNdc(new Vector2(1, -1), ToNdc(1024, 0, 0), "room top-right");
        AssertNdc(new Vector2(-1, 1), ToNdc(0, 576, 0), "room bottom-left");
        AssertNdc(new Vector2(0, 0), ToNdc(512, 288, 0), "view centre");
    }

    [TestMethod]
    public void OneEightyDegreesTurnsTheImageUpsideDown()
    {
        // The case this whole thing exists for: every corner swaps with the one diagonally across.
        AssertNdc(new Vector2(1, 1), ToNdc(0, 0, 180), "room top-left");
        AssertNdc(new Vector2(-1, 1), ToNdc(1024, 0, 180), "room top-right");
        AssertNdc(new Vector2(1, -1), ToNdc(0, 576, 180), "room bottom-left");
        AssertNdc(new Vector2(0, 0), ToNdc(512, 288, 180), "view centre stays put");
    }

    [TestMethod]
    public void RotationHappensAboutTheViewCentre()
    {
        foreach (var angle in new float[] { 15, 90, 180, 275 })
        {
            AssertNdc(Vector2.Zero, ToNdc(512, 288, angle), $"view centre at {angle} degrees");
        }
    }

    [TestMethod]
    public void QuarterTurnSwapsTheAxes()
    {
        // Following the runner's own up-vector formula, a positive angle turns the view
        // anti-clockwise, so the room's +x axis leaves the screen through the bottom. NDC is
        // normalised per axis and the view isn't square, so the same 256 room-px offset reads as
        // 256/512 along x but 256/288 along y once it has swapped axes.
        AssertNdc(new Vector2(256f / 512f, 0), ToNdc(768, 288, 0), "256px right of centre, unrotated");
        AssertNdc(new Vector2(0, -256f / 288f), ToNdc(768, 288, 90), "256px right of centre, quarter turn");

        // ...and the opposite quarter turn mirrors it.
        AssertNdc(new Vector2(0, 256f / 288f), ToNdc(768, 288, -90), "256px right of centre, other way");
    }

    [TestMethod]
    public void BackbufferFlipStillAppliesWhenRotated()
    {
        // The backbuffer draws y-flipped relative to a surface; that has to survive the rotation
        // rather than replace it.
        AssertNdc(new Vector2(-1, 1), ToNdc(0, 0, 0, flipY: true), "room top-left, unrotated");
        AssertNdc(new Vector2(1, -1), ToNdc(0, 0, 180, flipY: true), "room top-left, upside down");
    }

    [TestMethod]
    public void CullBoundsAreTheViewRectWhenUnrotated()
    {
        var bounds = GraphicsManager.GetCullBounds(new Vector4(100, 200, 1024, 576), 0);

        Assert.AreEqual(100, bounds.Left);
        Assert.AreEqual(200, bounds.Top);
        Assert.AreEqual(1124, bounds.Right);
        Assert.AreEqual(776, bounds.Bottom);
    }

    [TestMethod]
    public void CullBoundsAreUnchangedByAHalfTurn()
    {
        // A rectangle rotated 180 degrees about its own centre covers exactly the same room area.
        var bounds = GraphicsManager.GetCullBounds(new Vector4(100, 200, 1024, 576), 180);

        Assert.AreEqual(100, bounds.Left, 0.001f);
        Assert.AreEqual(200, bounds.Top, 0.001f);
        Assert.AreEqual(1124, bounds.Right, 0.001f);
        Assert.AreEqual(776, bounds.Bottom, 0.001f);
    }

    [TestMethod]
    public void CullBoundsGrowForARotatedView()
    {
        // Turned on its side, a 1024x576 view sees 576 room-px across and 1024 down.
        var quarter = GraphicsManager.GetCullBounds(new Vector4(0, 0, 1024, 576), 90);

        Assert.AreEqual(576, quarter.Right - quarter.Left, 0.001f);
        Assert.AreEqual(1024, quarter.Bottom - quarter.Top, 0.001f);
        Assert.AreEqual(512, (quarter.Left + quarter.Right) / 2, 0.001f, "centred on the view centre");
        Assert.AreEqual(288, (quarter.Top + quarter.Bottom) / 2, 0.001f, "centred on the view centre");

        // At 45 degrees both axes need the diagonal's worth of slack, or geometry that's still
        // visible in the rotated corners gets culled away.
        var diagonal = GraphicsManager.GetCullBounds(new Vector4(0, 0, 1024, 576), 45);
        var expected = (1024 + 576) * (float)System.Math.Cos(45 * CustomMath.Deg2Rad);

        Assert.AreEqual(expected, diagonal.Right - diagonal.Left, 0.01f);
        Assert.AreEqual(expected, diagonal.Bottom - diagonal.Top, 0.01f);
    }
}
