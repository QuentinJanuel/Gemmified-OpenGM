using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Mathematics;

namespace OpenGM.Tests;

/// <summary>
/// The off-view cull is an optimisation that compares vertex positions against the view area, so it
/// is only valid while those positions are view-space positions. Once a game draws through a world
/// matrix, they are not, and culling on them discards draws that do land on screen.
///
/// tijit engine 2 renders its tile layer into a camera-sized surface under matrix_build(-x, -y, ...):
/// on the first screenful room and surface coordinates coincide and everything passed, but as soon
/// as the camera scrolled right, every sprite drawn that way sat past the surface's right edge and
/// vanished -- the player standing on invisible blocks, a gravity switch left uncovered.
/// </summary>
[TestClass]
public class CullingWorldMatrixTests
{
    /// <summary>An 800x608 view sitting at the room's origin.</summary>
    private static readonly (float Left, float Top, float Right, float Bottom) View = (0, 0, 800, 608);

    private static GraphicsManager.Vertex V(double x, double y)
        => new(new(x, y, 0), new(1, 1, 1, 1), new(0, 0));

    /// <summary>A quad far to the right of an 800x608 view: room x 1600..1632.</summary>
    private static readonly GraphicsManager.Vertex[] OffToTheRight =
        [V(1600, 100), V(1632, 100), V(1632, 132), V(1600, 132)];

    [TestInitialize]
    public void SetUp()
    {
        GraphicsManager.EnableCulling = true;
        _3DFunctions.WorldMatrix = Matrix4.Identity;
    }

    [TestCleanup]
    public void TearDown() => _3DFunctions.WorldMatrix = Matrix4.Identity;

    [TestMethod]
    public void GeometryOffTheViewIsCulledWhenThereIsNoWorldMatrix()
    {
        Assert.IsTrue(GraphicsManager.ShouldCull(OffToTheRight, View));
    }

    [TestMethod]
    public void TheSameGeometryIsKeptWhenAWorldMatrixWillBringItOnScreen()
    {
        // What the game does: draw room-space geometry into a surface that covers the camera,
        // translating by the camera's position. At camera x = 1400 this quad lands at x 200..232 --
        // firmly on screen -- even though its own coordinates are past the view's right edge.
        _3DFunctions.WorldMatrix = Matrix4.CreateTranslation(-1400, 0, 0);

        Assert.IsFalse(GraphicsManager.ShouldCull(OffToTheRight, View));
    }

    [TestMethod]
    public void GeometryInsideTheViewIsAlwaysKept()
    {
        Assert.IsFalse(GraphicsManager.ShouldCull([V(100, 100), V(132, 100), V(132, 132), V(100, 132)], View));
    }

    [TestMethod]
    public void CullingCanStillBeTurnedOffEntirely()
    {
        GraphicsManager.EnableCulling = false;
        try
        {
            Assert.IsFalse(GraphicsManager.ShouldCull(OffToTheRight, View));
        }
        finally
        {
            GraphicsManager.EnableCulling = true;
        }
    }
}
