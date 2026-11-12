using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenGM.Tests;

/// <summary>
/// The untextured filled-polygon batch (GraphicsManager.BatchConvexPolygon / FlushPolygonBatch).
/// Its whole job is to turn N draw calls into 1, which under browser-wasm is N times fewer
/// [JSImport] boundary crossings -- I Wanna Spook Jam's RandomFangamer mansion draws its darkness
/// overlay as ~1000 concentric draw_circles a frame (50 per light source), and each of those was
/// costing a texture bind + a buffer upload + a draw.
///
/// The GL calls themselves need a context, so what's covered here is the part that doesn't: the
/// fan -> triangle-list expansion (getting this wrong silently renders the wrong shape) and the
/// queue's accounting.
/// </summary>
[TestClass]
public class PolygonBatchTests
{
    private static GraphicsManager.Vertex V(double x, double y)
        => new(new(x, y, 0), new(1, 1, 1, 1), new(0, 0));

    [TestCleanup]
    public void TearDown() => GraphicsManager.DiscardPolygonBatch();

    [TestInitialize]
    public void SetUp() => GraphicsManager.DiscardPolygonBatch();

    [TestMethod]
    public void ATriangleExpandsToItself()
    {
        GraphicsManager.BatchConvexPolygon([V(0, 0), V(1, 0), V(0, 1)]);

        Assert.AreEqual(3, GraphicsManager.PendingPolygonVertexCount);
    }

    [TestMethod]
    public void AFanOfNVerticesBecomesNMinusTwoTriangles()
    {
        // A 24-gon circle -- GameMaker's default draw_set_circle_precision -- is 22 triangles.
        GraphicsManager.BatchConvexPolygon(Fan(24));

        Assert.AreEqual(22 * 3, GraphicsManager.PendingPolygonVertexCount);
    }

    [TestMethod]
    public void PolygonsAccumulateAcrossCalls()
    {
        // The point of the whole thing: many separate draw_circle calls, one draw.
        for (var i = 0; i < 50; i++)
        {
            GraphicsManager.BatchConvexPolygon(Fan(24));
        }

        Assert.AreEqual(50 * 22 * 3, GraphicsManager.PendingPolygonVertexCount);
    }

    [TestMethod]
    public void ExpansionKeepsTheFansOwnWinding()
    {
        // Fan [a,b,c,d] is triangles (a,b,c) and (a,c,d) -- same coverage and winding as the
        // TriangleFan this replaces. A wrong pivot or a swapped pair silently changes the shape.
        var a = V(0, 0);
        var b = V(1, 0);
        var c = V(1, 1);
        var d = V(0, 1);

        GraphicsManager.BatchConvexPolygon([a, b, c, d]);

        var v = GraphicsManager.PeekPolygonBatch();
        Assert.AreEqual(6, v.Length);
        CollectionAssert.AreEqual(
            new[] { a.pos, b.pos, c.pos, a.pos, c.pos, d.pos },
            new[] { v[0].pos, v[1].pos, v[2].pos, v[3].pos, v[4].pos, v[5].pos });
    }

    [TestMethod]
    public void DegeneratePolygonsAreDropped()
    {
        GraphicsManager.BatchConvexPolygon([]);
        GraphicsManager.BatchConvexPolygon([V(0, 0)]);
        GraphicsManager.BatchConvexPolygon([V(0, 0), V(1, 1)]);

        Assert.AreEqual(0, GraphicsManager.PendingPolygonVertexCount, "nothing with fewer than 3 points has any area");
    }

    private static GraphicsManager.Vertex[] Fan(int points)
    {
        var fan = new GraphicsManager.Vertex[points];
        for (var i = 0; i < points; i++)
        {
            fan[i] = V(Math.Cos(i), Math.Sin(i));
        }

        return fan;
    }
}
