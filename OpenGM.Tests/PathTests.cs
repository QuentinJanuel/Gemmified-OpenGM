using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The path runtime API. Everything that *edits* a path at runtime was missing -- duplicate,
/// assign, append, insert/change/delete point, and all six geometric transforms -- so a game that
/// built or reshaped a path in code lost the whole event on the first call.
///
/// The transforms all pivot around the centre of the path's bounding box, which is the part worth
/// pinning: pivoting around the origin instead still "works" for a path that happens to be centred
/// there, and is wildly wrong for every other one.
/// </summary>
[TestClass]
public class PathTests
{
    [TestInitialize]
    public void SetUp() => PathManager.Paths.Clear();

    [TestCleanup]
    public void TearDown() => PathManager.Paths.Clear();

    /// <summary>A straight, open, 3-point path from (0,0) to (100,0) with a bend at (50,20).</summary>
    private static int MakePath(params (double X, double Y)[] points)
    {
        var id = ResourceFunctions.path_add([]).Conv<int>();
        // Linear and open, so the interpolated points are exactly the control points and the
        // geometry assertions below stay readable.
        ResourceFunctions.path_set_kind([id, 0]);
        ResourceFunctions.path_set_closed([id, false]);

        foreach (var (x, y) in points)
        {
            ResourceFunctions.path_add_point([id, x, y, 100]);
        }

        return id;
    }

    private static (double X, double Y) Point(int path, int n) =>
        (ResourceFunctions.path_get_point_x([path, n]).Conv<double>(),
         ResourceFunctions.path_get_point_y([path, n]).Conv<double>());

    private static int Count(int path) => ResourceFunctions.path_get_number([path]).Conv<int>();

    [TestMethod]
    public void PositionQueriesReadTheirOwnArgument()
    {
        // path_get_x/path_get_y both read args[0] for the *position* as well as the path index, so
        // every query answered for offset `index` -- which for the usual single path 0 meant they
        // always returned the start of the path, no matter what position was asked for.
        var path = MakePath((0, 0), (100, 0));

        Assert.AreEqual(0.0, ResourceFunctions.path_get_x([path, 0.0]).Conv<double>(), 1e-6);
        Assert.AreEqual(50.0, ResourceFunctions.path_get_x([path, 0.5]).Conv<double>(), 1e-6);
        Assert.AreEqual(100.0, ResourceFunctions.path_get_x([path, 1.0]).Conv<double>(), 1e-6);

        var diagonal = MakePath((0, 0), (100, 100));
        Assert.AreEqual(50.0, ResourceFunctions.path_get_y([diagonal, 0.5]).Conv<double>(), 1e-6);
    }

    [TestMethod]
    public void PointEditing()
    {
        var path = MakePath((0, 0), (100, 0));
        Assert.AreEqual(2, Count(path));

        ResourceFunctions.path_insert_point([path, 1, 50, 20, 100]);
        Assert.AreEqual(3, Count(path));
        Assert.AreEqual((50.0, 20.0), Point(path, 1));
        Assert.AreEqual((100.0, 0.0), Point(path, 2), "insert must shift the rest along");

        ResourceFunctions.path_change_point([path, 1, 60, 30, 50]);
        Assert.AreEqual((60.0, 30.0), Point(path, 1));
        Assert.AreEqual(50.0, ResourceFunctions.path_get_point_speed([path, 1]).Conv<double>(), 1e-6);
        Assert.AreEqual(3, Count(path), "change must not add a point");

        ResourceFunctions.path_delete_point([path, 1]);
        Assert.AreEqual(2, Count(path));
        Assert.AreEqual((100.0, 0.0), Point(path, 1));

        // Out-of-range indices are ignored rather than throwing.
        ResourceFunctions.path_delete_point([path, 99]);
        ResourceFunctions.path_change_point([path, -1, 0, 0, 0]);
        Assert.AreEqual(2, Count(path));
    }

    [TestMethod]
    public void EditingAPathRecomputesItsLength()
    {
        // The trap: mutating the control points without rebuilding the interpolated ones leaves
        // path_get_length and path_get_x answering for the old shape.
        var path = MakePath((0, 0), (100, 0));
        Assert.AreEqual(100.0, ResourceFunctions.path_get_length([path]).Conv<double>(), 1e-4);

        ResourceFunctions.path_change_point([path, 1, 200, 0, 100]);
        Assert.AreEqual(200.0, ResourceFunctions.path_get_length([path]).Conv<double>(), 1e-4);

        ResourceFunctions.path_shift([path, 10, 0]);
        Assert.AreEqual(200.0, ResourceFunctions.path_get_length([path]).Conv<double>(), 1e-4);
        Assert.AreEqual(10.0, ResourceFunctions.path_get_x([path, 0.0]).Conv<double>(), 1e-4);
    }

    [TestMethod]
    public void DuplicateAndAssignDeepCopy()
    {
        var source = MakePath((0, 0), (100, 0));

        var copy = ResourceFunctions.path_duplicate([source]).Conv<int>();
        Assert.AreNotEqual(source, copy);
        Assert.AreEqual(2, Count(copy));
        Assert.AreEqual((100.0, 0.0), Point(copy, 1));

        // Sharing the PathPoint objects instead of cloning them is silent until someone edits one.
        ResourceFunctions.path_change_point([copy, 1, 999, 999, 1]);
        Assert.AreEqual((100.0, 0.0), Point(source, 1), "editing the copy must not move the original");

        var target = MakePath((5, 5));
        ResourceFunctions.path_assign([target, source]);
        Assert.AreEqual(2, Count(target));
        Assert.AreEqual((0.0, 0.0), Point(target, 0));
        Assert.AreEqual((100.0, 0.0), Point(target, 1));
    }

    [TestMethod]
    public void AppendConcatenatesAndSurvivesSelfAppend()
    {
        var a = MakePath((0, 0), (10, 0));
        var b = MakePath((20, 0), (30, 0));

        ResourceFunctions.path_append([a, b]);
        Assert.AreEqual(4, Count(a));
        Assert.AreEqual((20.0, 0.0), Point(a, 2));
        Assert.AreEqual((30.0, 0.0), Point(a, 3));
        Assert.AreEqual(2, Count(b), "the source is not modified");

        // Appending a path to itself walks the same list it is growing -- without a snapshot this
        // never terminates.
        ResourceFunctions.path_append([b, b]);
        Assert.AreEqual(4, Count(b));
    }

    [TestMethod]
    public void TransformsPivotAroundTheBoundingBoxCentre()
    {
        // Deliberately not centred on the origin: a transform that pivoted around (0,0) instead
        // would pass every assertion on a path that happened to straddle it.
        var path = MakePath((100, 100), (200, 100), (200, 200));
        // Bounding box is (100,100)-(200,200), so the centre is (150,150).

        ResourceFunctions.path_mirror([path]);
        Assert.AreEqual((200.0, 100.0), Point(path, 0));
        Assert.AreEqual((100.0, 100.0), Point(path, 1));
        Assert.AreEqual((100.0, 200.0), Point(path, 2));
        ResourceFunctions.path_mirror([path]); // back again

        ResourceFunctions.path_flip([path]);
        Assert.AreEqual((100.0, 200.0), Point(path, 0));
        Assert.AreEqual((200.0, 200.0), Point(path, 1));
        Assert.AreEqual((200.0, 100.0), Point(path, 2));
        ResourceFunctions.path_flip([path]);

        ResourceFunctions.path_rescale([path, 2, 2]);
        Assert.AreEqual((50.0, 50.0), Point(path, 0));
        Assert.AreEqual((250.0, 50.0), Point(path, 1));
        Assert.AreEqual((250.0, 250.0), Point(path, 2));
    }

    [TestMethod]
    public void RotateTurnsCounterClockwise()
    {
        // A path centred on (0,0) so the rotation is easy to read off. GML angles run
        // counter-clockwise on a downward-growing y axis, so 90 degrees sends +x to -y (up).
        var path = MakePath((-10, 0), (10, 0));

        ResourceFunctions.path_rotate([path, 90]);

        var (x0, y0) = Point(path, 0);
        var (x1, y1) = Point(path, 1);
        Assert.AreEqual(0.0, x0, 1e-9);
        Assert.AreEqual(10.0, y0, 1e-9);
        Assert.AreEqual(0.0, x1, 1e-9);
        Assert.AreEqual(-10.0, y1, 1e-9, "+x rotates to -y, i.e. upwards on screen");

        // Four quarter turns is the identity.
        ResourceFunctions.path_rotate([path, 270]);
        Assert.AreEqual(-10.0, Point(path, 0).X, 1e-9);
        Assert.AreEqual(0.0, Point(path, 0).Y, 1e-9);
    }

    [TestMethod]
    public void ReverseSwapsTheEnds()
    {
        var path = MakePath((0, 0), (50, 20), (100, 0));
        ResourceFunctions.path_reverse([path]);

        Assert.AreEqual((100.0, 0.0), Point(path, 0));
        Assert.AreEqual((50.0, 20.0), Point(path, 1));
        Assert.AreEqual((0.0, 0.0), Point(path, 2));
    }

    [TestMethod]
    public void NameAndSpeedQueries()
    {
        var path = MakePath((0, 0), (100, 0));

        var name = ResourceFunctions.path_get_name([path]).Conv<string>();
        StringAssert.StartsWith(name, "__newpath");

        // A missing path names itself the same way every other *_get_name does.
        Assert.AreEqual("<undefined>", ResourceFunctions.path_get_name([9999]).Conv<string>());

        Assert.AreEqual(100.0, ResourceFunctions.path_get_speed([path, 0.5]).Conv<double>(), 1e-6);
    }

    [TestMethod]
    public void MissingPathsAreIgnoredRatherThanThrowing()
    {
        // Every one of these used to be an unresolved call, which aborted the caller's whole event.
        // Now they have to at least be harmless when handed a path that isn't there.
        ResourceFunctions.path_mirror([9999]);
        ResourceFunctions.path_flip([9999]);
        ResourceFunctions.path_rotate([9999, 45]);
        ResourceFunctions.path_rescale([9999, 2, 2]);
        ResourceFunctions.path_shift([9999, 1, 1]);
        ResourceFunctions.path_reverse([9999]);
        ResourceFunctions.path_append([9999, 9998]);
        ResourceFunctions.path_assign([9999, 9998]);
        ResourceFunctions.path_insert_point([9999, 0, 0, 0, 0]);

        Assert.AreEqual(-1, ResourceFunctions.path_get_speed([9999, 0]).Conv<int>());
    }
}
