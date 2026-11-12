using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// The motion planning stepping functions and the mp_grid region operations.
///
/// Only the grid *construction* half of mp_* existed (create/destroy/add_cell/add_instances/path);
/// everything a game actually steers an instance with -- mp_linear_step, mp_potential_step, their
/// _object variants, the four path generators and mp_potential_settings -- was missing entirely,
/// along with mp_grid_add_rectangle/_clear_rectangle/_clear_all. An unresolved call takes the whole
/// event with it, so an enemy calling mp_potential_step in its Step event did nothing else either.
///
/// Ported from OpenGMK's game/pathfinding.rs.
/// </summary>
[TestClass]
public class MotionPlanningTests
{
    private const int SpriteId = 9400;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        DrawManager.ResetCollisionCaches();
        MotionPlanningManager.MPGrids.Clear();
        MotionPlanningFunctions.ResetPotentialSettings();
        PathManager.Paths.Clear();

        // A 32x32 axis-aligned box, so collisions are plain rectangle overlaps.
        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprMpTestBox",
            Width = 32,
            Height = 32,
            MarginLeft = 0,
            MarginRight = 31,
            MarginTop = 0,
            MarginBottom = 31,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
            Textures = [new SpritePageItem()]
        };
    }

    [TestCleanup]
    public void TearDown()
    {
        foreach (var instance in InstanceManager.instances.Values.ToList())
        {
            DrawManager.Unregister(instance);
        }

        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        SpriteManager._spriteDict.Remove(SpriteId);
        DrawManager.ResetCollisionCaches();
        MotionPlanningManager.MPGrids.Clear();
        MotionPlanningFunctions.ResetPotentialSettings();
        PathManager.Paths.Clear();
    }

    private static ObjectDefinition Define(int assetId, string name, bool solid = false)
    {
        var def = new ObjectDefinition { AssetId = assetId, Name = name, solid = solid, sprite = SpriteId };
        InstanceManager.ObjectDefinitions.Add(assetId, def);
        return def;
    }

    private static GamemakerObject Spawn(ObjectDefinition def, double x, double y)
    {
        var instance = new GamemakerObject(def, x, y, def.depth, InstanceManager.NextInstanceID++, SpriteId, true, false, -1);
        instance.xprevious = x;
        instance.yprevious = y;
        return instance;
    }

    /// <summary>The mp_* stepping functions all move <c>VMExecutor.Self</c>.</summary>
    private static void Become(GamemakerObject instance)
        => VMExecutor.EnvStack.Push(new VMEnvFrame { Self = instance, ObjectDefinition = instance.Definition });

    private static void Unbecome() => VMExecutor.EnvStack.Pop();

    /*
     * mp_linear_step
     */

    [TestMethod]
    public void LinearStepWalksStraightAndStopsOnArrival()
    {
        var def = Define(1, "walker");
        InstanceManager.InitObjectMap();
        var walker = Spawn(def, 0, 0);
        Become(walker);

        try
        {
            Assert.IsFalse(MotionPlanningFunctions.mp_linear_step([100, 0, 10, false]).Conv<bool>());
            Assert.AreEqual(10.0, walker.x, 1e-9);
            Assert.AreEqual(0.0, walker.y, 1e-9);
            Assert.AreEqual(0.0, walker.direction, 1e-9);

            // The step is capped at stepsize along the straight line to the goal.
            MotionPlanningFunctions.mp_linear_step([100, 0, 10, false]);
            Assert.AreEqual(20.0, walker.x, 1e-9);

            // Within one step of the goal: lands exactly on it and reports arrival.
            walker.x = 95;
            Assert.IsTrue(MotionPlanningFunctions.mp_linear_step([100, 0, 10, false]).Conv<bool>());
            Assert.AreEqual(100.0, walker.x, 1e-9);

            // Already there.
            Assert.IsTrue(MotionPlanningFunctions.mp_linear_step([100, 0, 10, false]).Conv<bool>());
        }
        finally
        {
            Unbecome();
        }
    }

    [TestMethod]
    public void LinearStepStepsDiagonallyAtTheRightSpeed()
    {
        var def = Define(1, "walker");
        InstanceManager.InitObjectMap();
        var walker = Spawn(def, 0, 0);
        Become(walker);

        try
        {
            // The step is capped by *distance along the line*, not per axis -- a diagonal step must
            // still be exactly stepsize long, not stepsize on each axis.
            MotionPlanningFunctions.mp_linear_step([100, 100, 10, false]);

            var travelled = Math.Sqrt((walker.x * walker.x) + (walker.y * walker.y));
            Assert.AreEqual(10.0, travelled, 1e-9);
            Assert.AreEqual(315.0, walker.direction, 1e-9, "down-right is 315 degrees in GML");
        }
        finally
        {
            Unbecome();
        }
    }

    [TestMethod]
    public void LinearStepRefusesToEnterASolid()
    {
        var walkerDef = Define(1, "walker");
        var wallDef = Define(2, "wall", solid: true);
        InstanceManager.InitObjectMap();

        var walker = Spawn(walkerDef, 0, 0);
        Spawn(wallDef, 20, 0);
        Become(walker);

        try
        {
            // A 32-wide walker at x=10 would overlap the wall's box at x=20.
            MotionPlanningFunctions.mp_linear_step([100, 0, 10, false]);

            Assert.AreEqual(0.0, walker.x, 1e-9, "blocked steps are not taken at all");
            // mp_linear_step doesn't slide -- getting stuck against a wall is the documented
            // behaviour, and the reason mp_potential_step exists.
            MotionPlanningFunctions.mp_linear_step([100, 0, 10, false]);
            Assert.AreEqual(0.0, walker.x, 1e-9);
        }
        finally
        {
            Unbecome();
        }
    }

    [TestMethod]
    public void LinearStepObjectOnlyChecksThatObject()
    {
        var walkerDef = Define(1, "walker");
        var wallDef = Define(2, "wall", solid: true);
        var decorDef = Define(3, "decor");
        InstanceManager.InitObjectMap();

        var walker = Spawn(walkerDef, 0, 0);
        Spawn(decorDef, 20, 0);
        Become(walker);

        try
        {
            // Blocked by the wall object... which isn't there, so it walks straight through decor.
            MotionPlanningFunctions.mp_linear_step_object([100, 0, 10, wallDef.AssetId]);
            Assert.AreEqual(10.0, walker.x, 1e-9);

            walker.x = 0;
            MotionPlanningFunctions.mp_linear_step_object([100, 0, 10, decorDef.AssetId]);
            Assert.AreEqual(0.0, walker.x, 1e-9, "and is blocked by the object it was asked about");
        }
        finally
        {
            Unbecome();
        }
    }

    /*
     * mp_potential_step
     */

    [TestMethod]
    public void PotentialStepSteersAroundAnObstacle()
    {
        var walkerDef = Define(1, "walker");
        var wallDef = Define(2, "wall", solid: true);
        InstanceManager.InitObjectMap();

        var walker = Spawn(walkerDef, 0, 0);
        Spawn(wallDef, 40, 0);
        Become(walker);

        try
        {
            // It does *not* get round in one step, and shouldn't: max_rotation caps the turn at 30
            // degrees a step, and every direction within 30 degrees of straight-ahead still runs
            // into the wall. The first couple of steps are spent turning on the spot -- which is
            // exactly what rotate_on_spot is for.
            Assert.IsFalse(MotionPlanningFunctions.mp_potential_step([200, 0, 8, false]).Conv<bool>());
            Assert.AreEqual(0.0, walker.x, 1e-9, "nowhere to go on the first step");
            Assert.AreEqual(30.0, walker.direction, 1e-9, "so it turns on the spot instead");

            // Given room to steer over several steps, it works its way around and past the wall,
            // which is the whole difference from mp_linear_step.
            for (var i = 0; i < 100 && walker.x < 80; i++)
            {
                MotionPlanningFunctions.mp_potential_step([200, 0, 8, false]);

                // The wall's box is (40,0)-(71,31) and the walker's is 32x32 from its own position.
                var overlaps = walker.x + 31 >= 40 && walker.x <= 71
                               && walker.y + 31 >= 0 && walker.y <= 31;
                Assert.IsFalse(overlaps, $"stepped into the wall at ({walker.x}, {walker.y})");
            }

            Assert.IsTrue(walker.x > 80, $"never got past the wall (ended at x={walker.x})");
        }
        finally
        {
            Unbecome();
        }
    }

    [TestMethod]
    public void PotentialStepRespectsMaxRotation()
    {
        var walkerDef = Define(1, "walker");
        InstanceManager.InitObjectMap();
        var walker = Spawn(walkerDef, 0, 0);
        // Facing right; the goal is straight up, i.e. a 90 degree turn.
        walker.direction = 0;
        Become(walker);

        try
        {
            // Default max_rotation is 30 degrees a step, so it cannot turn straight at the goal.
            MotionPlanningFunctions.mp_potential_step([0, -200, 10, false]);
            Assert.AreEqual(30.0, walker.direction, 1e-9, "capped at max_rotation");

            // It keeps turning toward the goal, but never by more than max_rotation in one step.
            // Note the candidate directions are goal_direction +/- k*rotate_step, and max_rotation
            // only *filters* those -- so the turn lands on the nearest allowed candidate rather
            // than on exactly max_rotation.
            for (var i = 0; i < 5; i++)
            {
                var before = walker.direction;
                MotionPlanningFunctions.mp_potential_step([0, -200, 10, false]);

                var turn = Math.Abs(walker.direction - before);
                Assert.IsTrue(turn <= 30 + 1e-9, $"turned {turn} degrees in one step, over the 30 cap");
            }

            // It doesn't home in monotonically -- each step re-derives the goal direction from the
            // position the last step moved it to, so it can overshoot and correct -- but over
            // several steps it ends up pointing broadly at the goal (90 degrees, straight up).
            Assert.IsTrue(walker.direction is > 60 and < 120,
                $"should be heading roughly at the goal, is facing {walker.direction}");

            // Widening the cap lets it turn all the way to the goal in one go.
            walker.x = 0;
            walker.y = 0;
            walker.direction = 0;
            MotionPlanningFunctions.mp_potential_settings([180, 10, 3, true]);
            MotionPlanningFunctions.mp_potential_step([0, -200, 10, false]);
            Assert.AreEqual(90.0, walker.direction, 1e-9, "straight up, in one step");
        }
        finally
        {
            Unbecome();
        }
    }

    [TestMethod]
    public void PotentialStepTurnsOnTheSpotWhenCompletelyBoxedIn()
    {
        var walkerDef = Define(1, "walker");
        var wallDef = Define(2, "wall", solid: true);
        InstanceManager.InitObjectMap();

        var walker = Spawn(walkerDef, 0, 0);
        // Walled in on all four sides, within a 10-pixel step of each.
        Spawn(wallDef, 25, 0);
        Spawn(wallDef, -25, 0);
        Spawn(wallDef, 0, 25);
        Spawn(wallDef, 0, -25);
        Become(walker);

        try
        {
            walker.direction = 0;
            var reached = MotionPlanningFunctions.mp_potential_step([200, 0, 10, false]).Conv<bool>();

            Assert.IsFalse(reached);
            Assert.AreEqual(0.0, walker.x, 1e-9, "nowhere to go");
            Assert.AreEqual(0.0, walker.y, 1e-9);
            // rotate_on_spot is on by default: it spins so that next step it probes new angles
            // instead of re-deriving the same blocked ones forever.
            Assert.AreEqual(30.0, walker.direction, 1e-9);

            walker.direction = 0;
            MotionPlanningFunctions.mp_potential_settings([30, 10, 3, false]);
            MotionPlanningFunctions.mp_potential_step([200, 0, 10, false]);
            Assert.AreEqual(0.0, walker.direction, 1e-9, "and holds still when rotate_on_spot is off");
        }
        finally
        {
            Unbecome();
        }
    }

    /*
     * Path generation
     */

    [TestMethod]
    public void LinearPathPlansWithoutMovingTheInstance()
    {
        var def = Define(1, "walker");
        InstanceManager.InitObjectMap();
        var walker = Spawn(def, 0, 0);
        var path = ResourceFunctions.path_add([]).Conv<int>();
        Become(walker);

        try
        {
            var reached = MotionPlanningFunctions.mp_linear_path([path, 100, 0, 10, false]).Conv<bool>();

            Assert.IsTrue(reached);
            // The whole point of a *path* function: it works out the route and leaves the instance
            // exactly where it was, for path_start to walk later.
            Assert.AreEqual(0.0, walker.x, 1e-9);
            Assert.AreEqual(0.0, walker.y, 1e-9);

            Assert.AreEqual(10, ResourceFunctions.path_get_number([path]).Conv<int>());
            Assert.AreEqual(100.0, ResourceFunctions.path_get_point_x([path, 9]).Conv<double>(), 1e-9);
        }
        finally
        {
            Unbecome();
        }
    }

    [TestMethod]
    public void LinearPathReportsFailureWhenItCannotGetThere()
    {
        var walkerDef = Define(1, "walker");
        var wallDef = Define(2, "wall", solid: true);
        InstanceManager.InitObjectMap();

        var walker = Spawn(walkerDef, 0, 0);
        Spawn(wallDef, 40, 0);
        var path = ResourceFunctions.path_add([]).Conv<int>();
        Become(walker);

        try
        {
            // It walks up to the wall and then stops making progress, which has to terminate the
            // generator rather than loop forever.
            var reached = MotionPlanningFunctions.mp_linear_path([path, 200, 0, 10, false]).Conv<bool>();

            Assert.IsFalse(reached);
            Assert.AreEqual(0.0, walker.x, 1e-9, "and it still gets put back where it started");
        }
        finally
        {
            Unbecome();
        }
    }

    /*
     * mp_grid regions
     */

    private static int Cell(int grid, int h, int v)
        => MotionPlanningFunctions.mp_grid_get_cell([grid, h, v]).Conv<int>();

    [TestMethod]
    public void GridRectanglesMarkAndClearCellsInRoomCoordinates()
    {
        // 10x10 grid of 32px cells with its origin at (0,0).
        var grid = MotionPlanningManager.GridCreate(0, 0, 10, 10, 32, 32);

        MotionPlanningFunctions.mp_grid_add_rectangle([grid, 0, 0, 64, 64]);

        Assert.AreEqual(-1, Cell(grid, 0, 0));
        Assert.AreEqual(-1, Cell(grid, 1, 1));
        Assert.AreEqual(0, Cell(grid, 2, 2), "the rectangle ends at 64, i.e. covers cells 0 and 1");
        Assert.AreEqual(0, Cell(grid, 5, 5));

        MotionPlanningFunctions.mp_grid_clear_rectangle([grid, 0, 0, 32, 32]);
        Assert.AreEqual(0, Cell(grid, 0, 0));
        Assert.AreEqual(-1, Cell(grid, 1, 1), "clearing one cell must not clear its neighbour");

        MotionPlanningFunctions.mp_grid_clear_all([grid]);
        Assert.AreEqual(0, Cell(grid, 1, 1));
    }

    [TestMethod]
    public void GridRectanglesAreClampedAndOffsetByTheGridOrigin()
    {
        // Origin at (100,100), so room coordinate 100 is cell 0.
        var grid = MotionPlanningManager.GridCreate(100, 100, 4, 4, 32, 32);

        // Deliberately overruns the grid on both ends -- must clamp, not throw or wrap.
        MotionPlanningFunctions.mp_grid_add_rectangle([grid, -500, -500, 500, 500]);

        for (var h = 0; h < 4; h++)
        {
            for (var v = 0; v < 4; v++)
            {
                Assert.AreEqual(-1, Cell(grid, h, v), $"cell {h},{v}");
            }
        }

        MotionPlanningFunctions.mp_grid_clear_all([grid]);
        MotionPlanningFunctions.mp_grid_add_rectangle([grid, 100, 100, 132, 132]);
        Assert.AreEqual(-1, Cell(grid, 0, 0), "room (100,100) is cell (0,0) on a grid that starts there");
        Assert.AreEqual(0, Cell(grid, 1, 0));
    }

    [TestMethod]
    public void GridQueriesOutsideTheGridReadAsBlocked()
    {
        var grid = MotionPlanningManager.GridCreate(0, 0, 4, 4, 32, 32);

        // Not 0 ("free"): nothing should ever be routed off the edge of the grid.
        Assert.AreEqual(-1, Cell(grid, -1, 0));
        Assert.AreEqual(-1, Cell(grid, 4, 0));
        Assert.AreEqual(-1, Cell(grid, 0, 99));
        Assert.AreEqual(-1, MotionPlanningFunctions.mp_grid_get_cell([9999, 0, 0]).Conv<int>());
    }

    [TestMethod]
    public void MissingGridsAreIgnoredRatherThanThrowing()
    {
        MotionPlanningFunctions.mp_grid_add_rectangle([9999, 0, 0, 10, 10]);
        MotionPlanningFunctions.mp_grid_clear_rectangle([9999, 0, 0, 10, 10]);
        MotionPlanningFunctions.mp_grid_clear_all([9999]);
    }
}
