using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// draw_healthbar's `direction` argument only names four bars (0/1/2/3); the runner's switch sends
/// everything else to the left-to-right default (OpenGMK's kernel.rs matches). OpenGM had no
/// default, and left barx1/bary1/barx2/bary2 at the 0 they were declared with -- so an unrecognised
/// direction drew the bar as a degenerate rect at the room origin instead, leaving only the
/// backdrop. I Wanna Spook Jam's oDB_GunSpike (dribix's witch boss) and objFrickWitchSpike both
/// pass 180, reading the argument as degrees:
///
///     draw_healthbar(x - 16, y + 18, x + 16, y + 23, alarm[0], c_green, c_black, c_black, 180, 1, 0)
///
/// The bar is the *cooldown*: a black overlay on a green backdrop, full right after the spike is
/// shot and shrinking as alarm[0] counts down, so the green underneath refills. With the bar rect
/// collapsed to nothing, the green backdrop was all that ever drew and the spike looked permanently
/// ready.
///
/// Filled polygons never touch GL -- they queue in GraphicsManager's batch -- so the geometry these
/// calls actually produce can be read straight back out.
/// </summary>
[TestClass]
public class HealthbarDirectionTests
{
    private const int Black = 0;
    private const int Green = 32768; // c_green, BGR

    // The spike's bar, verbatim from oDB_GunSpike's Draw End event.
    private const double X1 = -16;
    private const double Y1 = 18;
    private const double X2 = 16;
    private const double Y2 = 23;

    private const double SpanX = X2 - X1;
    private const double SpanY = Y2 - Y1;

    [TestInitialize]
    public void SetUp() => GraphicsManager.DiscardPolygonBatch();

    [TestCleanup]
    public void TearDown() => GraphicsManager.DiscardPolygonBatch();

    private static void DrawBar(double amount, int direction, bool showback = false)
        => GraphicFunctions.draw_healthbar(
            [X1, Y1, X2, Y2, amount, Green, Black, Black, direction, showback, false]);

    private record struct Rect(double Left, double Top, double Right, double Bottom)
    {
        // draw_rectangle_colour extends x2/y2 a pixel past what it was handed, to make the rect
        // inclusive of its far edge -- so an empty bar is still 1px and a full one is 1px over.
        public double FilledX => (Right - 1 - Left) / SpanX;
        public double FilledY => (Bottom - 1 - Top) / SpanY;
    }

    /// <summary>
    /// The bounding box of the last rect queued -- each one is a 4-vertex fan expanded to 2
    /// triangles, so the bar itself is always the final 6 vertices.
    /// </summary>
    private static Rect LastRect()
    {
        var batch = GraphicsManager.PeekPolygonBatch();
        Assert.IsTrue(batch.Length >= 6, "expected at least one queued rect");

        var rect = new Rect(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        for (var i = batch.Length - 6; i < batch.Length; i++)
        {
            rect.Left = Math.Min(rect.Left, batch[i].pos.X);
            rect.Top = Math.Min(rect.Top, batch[i].pos.Y);
            rect.Right = Math.Max(rect.Right, batch[i].pos.X);
            rect.Bottom = Math.Max(rect.Bottom, batch[i].pos.Y);
        }

        return rect;
    }

    private static Rect DrawAndRead(double amount, int direction, bool showback = false)
    {
        GraphicsManager.DiscardPolygonBatch();
        DrawBar(amount, direction, showback);
        return LastRect();
    }

    [TestMethod]
    public void AnUnrecognisedDirectionFallsBackToLeftToRight()
    {
        var bar = DrawAndRead(amount: 50, direction: 180);

        Assert.AreEqual(X1, bar.Left, 0.001, "anchored at the left edge, like direction 0");
        Assert.AreEqual(0.5, bar.FilledX, 0.001, "half full, so half as wide");
        Assert.AreEqual(1, bar.FilledY, 0.001, "and the bar's full height");
    }

    [TestMethod]
    public void AnUnrecognisedDirectionStillTracksTheAmount()
    {
        // The bug wasn't "the bar is in the wrong place", it's that it never moved: every amount
        // collapsed to the same empty rect, so the spike's cooldown was invisible.
        foreach (var amount in new double[] { 0, 25, 50, 75, 100 })
        {
            Assert.AreEqual(amount / 100, DrawAndRead(amount, direction: 180).FilledX, 0.001,
                $"amount {amount} should fill {amount}% of the bar");
        }
    }

    [TestMethod]
    public void TheFourRealDirectionsEachAnchorOnTheirOwnEdge()
    {
        var leftToRight = DrawAndRead(amount: 25, direction: 0);
        Assert.AreEqual(X1, leftToRight.Left, 0.001);
        Assert.AreEqual(0.25, leftToRight.FilledX, 0.001);

        var rightToLeft = DrawAndRead(amount: 25, direction: 1);
        Assert.AreEqual(X2 + 1, rightToLeft.Right, 0.001);
        Assert.AreEqual(0.25, rightToLeft.FilledX, 0.001);

        var topToBottom = DrawAndRead(amount: 25, direction: 2);
        Assert.AreEqual(Y1, topToBottom.Top, 0.001);
        Assert.AreEqual(0.25, topToBottom.FilledY, 0.001);

        var bottomToTop = DrawAndRead(amount: 25, direction: 3);
        Assert.AreEqual(Y2 + 1, bottomToTop.Bottom, 0.001);
        Assert.AreEqual(0.25, bottomToTop.FilledY, 0.001);
    }

    [TestMethod]
    public void TheBackdropIsDrawnFullSizeUnderTheBar()
    {
        DrawBar(amount: 40, direction: 180, showback: true);

        var batch = GraphicsManager.PeekPolygonBatch();
        Assert.AreEqual(12, batch.Length, "two rects: the backdrop, then the bar over it");

        // The backdrop is the first one, and always spans the whole region regardless of amount.
        var backdrop = new Rect(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        for (var i = 0; i < 6; i++)
        {
            backdrop.Left = Math.Min(backdrop.Left, batch[i].pos.X);
            backdrop.Right = Math.Max(backdrop.Right, batch[i].pos.X);
        }

        Assert.AreEqual(X1, backdrop.Left, 0.001);
        Assert.AreEqual(X2 + 1, backdrop.Right, 0.001);
        Assert.AreEqual(0.4, LastRect().FilledX, 0.001, "the bar on top isn't");
    }

    [TestMethod]
    public void TheSpikeCooldownRefillsAsItsAlarmRunsDown()
    {
        // What the player actually sees. oDB_GunSpike sets alarm[0] = 250 when shot and hands the
        // raw countdown to draw_healthbar, so the black bar sits full for the first 150 frames
        // (amount clamps at 100) and then retreats, revealing green as the spike comes back up.
        static double GreenFraction(int alarm)
            => 1 - DrawAndRead(alarm, direction: 180, showback: true).FilledX;

        Assert.AreEqual(0, GreenFraction(250), 0.001, "just shot: covered");
        Assert.AreEqual(0, GreenFraction(150), 0.001, "still covered while the count is above 100");
        Assert.AreEqual(0.5, GreenFraction(50), 0.001, "half refilled");
        Assert.AreEqual(1, GreenFraction(0), 0.001, "alarm about to fire: full");
        Assert.AreEqual(1, GreenFraction(-1), 0.001, "idle and ready: stays full, not wrapped around");
    }
}
