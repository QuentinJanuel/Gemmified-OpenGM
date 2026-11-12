using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// "I wanna CoinStack 1000" draws its level-name plaque with draw_roundrect_ext, which OpenGM never
/// registered. An unresolved call aborts the whole event, so the Draw event died between its
/// surface_set_target and its surface_reset_target -- which is where the "Unbalanced surface stack"
/// spam in the same log came from. The plaque never drew either.
///
/// Registering the _ext pair also turned up the reason nobody had noticed draw_roundrect_colour was
/// broken: it read `outline` from argument 4 and its two colours from 5 and 6, but GameMaker puts
/// the colours first and `outline` LAST, the same as draw_rectangle_colour/draw_triangle_colour
/// right next to it in the same file. So it took col1 as the outline flag (any colour but c_black
/// is truthy -> outline) and outline as col2.
///
/// Filled polygons never touch GL -- they queue in GraphicsManager's batch -- so the geometry and
/// the colours these calls produce can be read straight back out.
/// </summary>
[TestClass]
public class RoundRectTests
{
    private const int Red = 255;        // c_red, BGR
    private const int Blue = 16711680;  // c_blue, BGR

    [TestInitialize]
    public void SetUp()
    {
        ScriptResolver.InitGMLFunctions();
        SpriteManager.DrawColor = 16777215;
        SpriteManager.DrawAlpha = 1;
        GraphicsManager.DiscardPolygonBatch();
    }

    [TestCleanup]
    public void TearDown() => GraphicsManager.DiscardPolygonBatch();

    private record struct Box(double Left, double Top, double Right, double Bottom);

    private static Box BatchBounds()
    {
        var batch = GraphicsManager.PeekPolygonBatch();
        Assert.IsTrue(batch.Length >= 3, "expected queued geometry");

        var box = new Box(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        foreach (var v in batch)
        {
            box.Left = Math.Min(box.Left, v.pos.X);
            box.Top = Math.Min(box.Top, v.pos.Y);
            box.Right = Math.Max(box.Right, v.pos.X);
            box.Bottom = Math.Max(box.Bottom, v.pos.Y);
        }

        return box;
    }

    /// <summary>
    /// How far in from <paramref name="edge"/> the shape's outermost vertex sits along the OTHER
    /// axis -- i.e. the corner inset, which is what the radius controls. Measured at the extreme
    /// row/column so only the corner arcs contribute.
    /// </summary>
    private static double CornerInsetX(double left, double top)
    {
        var batch = GraphicsManager.PeekPolygonBatch();
        var min = double.MaxValue;
        foreach (var v in batch)
        {
            // Vertices sitting on the top edge; their distance in from the left edge is the x radius.
            if (Math.Abs(v.pos.Y - top) < 1e-3)
            {
                min = Math.Min(min, v.pos.X - left);
            }
        }

        Assert.AreNotEqual(double.MaxValue, min, "no vertex on the top edge");
        return min;
    }

    [TestMethod]
    public void RoundRectExtIsRegistered()
    {
        // Through real bytecode -- the same `call.i draw_roundrect_ext(argc=7)` path that logged
        // "Can't resolve script draw_roundrect_ext !". Registration is the whole point here.
        foreach (var (name, argc) in new[]
                 {
                     ("draw_roundrect", 5), ("draw_roundrect_ext", 7),
                     ("draw_roundrect_color", 7), ("draw_roundrect_colour", 7),
                     ("draw_roundrect_color_ext", 9), ("draw_roundrect_colour_ext", 9),
                 })
        {
            Assert.IsTrue(ScriptResolver.BuiltInFunctions.ContainsKey(name), $"{name} is not registered");

            var asm = ":[0]\n";
            // Arguments go on the stack in REVERSE order (last one first) -- see MissingBuiltinsTests.
            for (var i = argc - 1; i >= 0; i--)
            {
                // A real 10,20..30,40 rect with nonzero radii/colours after it, and `outline` --
                // always the last argument -- false. Both the outline path and the degenerate
                // draw_rectangle fallback go through CustomWindow.Draw, which needs a GL context;
                // only the filled path stays in the batch, and filled is what we can run headless.
                var value = i < 4 ? 10 * (i + 1) : i == argc - 1 ? 0 : 10 * (i + 1);
                asm += $"push.d {value}\nconv.d.v\n";
            }
            asm += $"call.i {name}(argc={argc})\nret.v";

            GraphicsManager.DiscardPolygonBatch();
            TestUtils.ExecuteScript(name, asm);
        }
    }

    [TestMethod]
    public void ExtRadiiApplyPerAxis()
    {
        // A 100x100 rect with a 20x5 corner: wide, shallow arcs.
        GraphicFunctions.draw_roundrect_ext([0.0, 0.0, 100.0, 100.0, 20.0, 5.0, false]);

        var box = BatchBounds();
        Assert.AreEqual(0, box.Left, 1e-6, "left edge");
        Assert.AreEqual(0, box.Top, 1e-6, "top edge");
        Assert.AreEqual(100, box.Right, 1e-6, "right edge");
        Assert.AreEqual(100, box.Bottom, 1e-6, "bottom edge");

        // The topmost vertices are the tops of the arcs, inset by the X radius, not the Y one.
        Assert.AreEqual(20, CornerInsetX(0, 0), 1e-6, "corner inset follows xrad");
    }

    [TestMethod]
    public void ExtRadiiClampToHalfTheSide()
    {
        // Radii larger than the rect would make the corners overlap; each axis clamps on its own.
        GraphicFunctions.draw_roundrect_ext([0.0, 0.0, 10.0, 40.0, 50.0, 50.0, false]);

        var box = BatchBounds();
        Assert.AreEqual(0, box.Left, 1e-6, "left edge stays put");
        Assert.AreEqual(10, box.Right, 1e-6, "right edge stays put");
        Assert.AreEqual(5, CornerInsetX(0, 0), 1e-6, "xrad clamps to half the width");
    }

    [TestMethod]
    public void ColourVariantsTakeOutlineLast()
    {
        // draw_roundrect_colour(x1, y1, x2, y2, col1, col2, outline) -- filled, red centre, blue rim.
        GraphicFunctions.draw_roundrect_color([0.0, 0.0, 40.0, 40.0, Red, Blue, false]);

        var batch = GraphicsManager.PeekPolygonBatch();
        Assert.IsTrue(batch.Length >= 3, "outline was misread as true, so nothing filled");

        // The fan's first vertex is the centre; it carries col1.
        var expectedCentre = Red.ABGRToCol4(1);
        var expectedRim = Blue.ABGRToCol4(1);
        Assert.AreEqual(expectedCentre.R, batch[0].color.X, 1e-6, "centre is col1 (red)");
        Assert.AreEqual(expectedCentre.B, batch[0].color.Z, 1e-6, "centre is col1 (red)");

        // ...and some later vertex carries col2, which only happens if the colours were read from
        // arguments 4 and 5 rather than 5 and 6.
        var sawRim = false;
        foreach (var v in batch)
        {
            if (Math.Abs(v.color.Z - expectedRim.B) < 1e-6 && Math.Abs(v.color.X - expectedRim.R) < 1e-6)
            {
                sawRim = true;
            }
        }

        Assert.IsTrue(sawRim, "rim is col2 (blue)");
    }

    [TestMethod]
    public void ColourExtTakesRadiiThenColoursThenOutline()
    {
        // draw_roundrect_colour_ext(x1, y1, x2, y2, xrad, yrad, col1, col2, outline).
        GraphicFunctions.draw_roundrect_color_ext(
            [0.0, 0.0, 100.0, 100.0, 20.0, 5.0, Red, Blue, false]);

        Assert.AreEqual(20, CornerInsetX(0, 0), 1e-6, "radii come before the colours");

        var batch = GraphicsManager.PeekPolygonBatch();
        var expectedCentre = Red.ABGRToCol4(1);
        Assert.AreEqual(expectedCentre.R, batch[0].color.X, 1e-6, "centre is col1 (red)");
        Assert.AreEqual(expectedCentre.B, batch[0].color.Z, 1e-6, "centre is col1 (red)");
    }
}
