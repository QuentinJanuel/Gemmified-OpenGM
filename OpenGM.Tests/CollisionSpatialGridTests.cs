using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// DrawManager.UpdateCollisions checked every driver instance against every instance of each of
/// its target objects, every frame, with no spatial pruning -- an O(drivers x targets) cost that
/// visibly lags once instance counts climb (a death effect leaving hundreds of stationary blood
/// particles behind, each re-checked against every solid block in the room, forever). Ported the
/// same fix an earlier OpenGMK session already applied to the same class of bug
/// (gm8emulator/src/spatial_grid.rs's broad-phase): a transient, per-call SpatialGrid that prunes
/// pairs whose bounding boxes can't overlap before the existing precise check ever runs, without
/// changing which pairs actually collide.
///
/// These cover both layers: SpatialGrid in isolation (insert/query/remove/reinsert), and the real
/// DrawManager.UpdateCollisions path with enough scattered instances that a broad-phase bug
/// (dropping a real collision, or inventing a fake one) would actually show up.
/// </summary>
[TestClass]
public class CollisionSpatialGridTests
{
    private const int SpriteId = 9200;

    [TestInitialize]
    public void SetUp()
    {
        _fired.Clear();
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        DrawManager.ResetCollisionCaches();

        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprGridTestBox",
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
    }

    private static ObjectDefinition Define(int assetId, string name, bool solid = false, ObjectDefinition? parent = null)
    {
        var def = new ObjectDefinition { AssetId = assetId, Name = name, solid = solid, parent = parent, sprite = SpriteId };
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

    // ---- SpatialGrid in isolation ----

    [TestMethod]
    public void QueryFindsOnlyOverlappingMembers()
    {
        var wall = Define(1, "wall");
        InstanceManager.InitObjectMap();
        var near = Spawn(wall, 0, 0);
        var far = Spawn(wall, 1000, 1000);

        var grid = new SpatialGrid();
        grid.Insert(near, near.bbox);
        grid.Insert(far, far.bbox);

        CollectionAssert.AreEqual(new[] { near }, grid.Query(near.bbox));
        CollectionAssert.AreEqual(new[] { far }, grid.Query(far.bbox));
        Assert.AreEqual(0, grid.Query(new BBox { left = 500, top = 500, right = 510, bottom = 510 }).Count);
    }

    [TestMethod]
    public void QuerySortsResultsByAscendingInstanceId()
    {
        // Matches InstanceManager.FindByAssetId's own explicit sort -- inserted out of instanceId
        // order, must still come back in it.
        var wall = Define(1, "wall");
        InstanceManager.InitObjectMap();
        var third = Spawn(wall, 0, 0);
        var first = Spawn(wall, 0, 0);
        var second = Spawn(wall, 0, 0);

        var grid = new SpatialGrid();
        grid.Insert(third, third.bbox);
        grid.Insert(first, first.bbox);
        grid.Insert(second, second.bbox);

        var result = grid.Query(first.bbox);
        Assert.IsTrue(result.SequenceEqual(result.OrderBy(o => o.instanceId)));
        CollectionAssert.AreEquivalent(new[] { first, second, third }, result);
    }

    [TestMethod]
    public void ReinsertMovesAMemberToItsNewCell()
    {
        var wall = Define(1, "wall");
        InstanceManager.InitObjectMap();
        var instance = Spawn(wall, 0, 0);

        var grid = new SpatialGrid();
        grid.Insert(instance, instance.bbox);

        instance.x = 1000;
        instance.y = 1000;
        grid.Reinsert(instance, instance.bbox);

        Assert.AreEqual(0, grid.Query(new BBox { left = 0, top = 0, right = 31, bottom = 31 }).Count);
        CollectionAssert.AreEqual(new[] { instance }, grid.Query(instance.bbox));
    }

    [TestMethod]
    public void ALargeBboxSpanningManyCellsIsStillFoundOnce()
    {
        var wall = Define(1, "wall");
        InstanceManager.InitObjectMap();
        var tiny = Spawn(wall, 0, 0);
        var huge = Spawn(wall, 0, 0);
        // Passed straight to Insert rather than assigned to huge.bbox: the bbox property's getter
        // recomputes from the sprite whenever bbox_dirty is set (which the x/y setters always do),
        // so an assignment made after construction wouldn't survive the next read anyway.
        var hugeBbox = new BBox { left = 0, top = 0, right = 500, bottom = 500 };

        var grid = new SpatialGrid();
        grid.Insert(tiny, tiny.bbox);
        grid.Insert(huge, hugeBbox);

        // Query a cell far from the origin but still inside huge's span -- huge must show up
        // exactly once, not once per cell it happens to occupy.
        var result = grid.Query(new BBox { left = 400, top = 400, right = 410, bottom = 410 });
        Assert.AreEqual(1, result.Count(o => o == huge));
    }

    // ---- Through the real DrawManager.UpdateCollisions path ----

    [TestMethod]
    public void ManyScatteredTargetsStillFireExactlyTheOverlappingPairs()
    {
        // Shaped after the actual bug report: hundreds of small driver instances (blood) among a
        // room full of target instances (blocks), most of them nowhere near each other. A
        // broad-phase bug would either drop a real collision (false negative) or fire one that
        // shouldn't exist (false positive) -- both would show up as a mismatch here.
        var block = Define(1, "objBlock", solid: true);
        var blood = Define(2, "objBlood");
        blood.CollisionScript[1] = MarkerCode("blood-hit-block");
        InstanceManager.InitObjectMap();

        var blocks = new List<GamemakerObject>();
        for (var gx = 0; gx < 20; gx++)
        {
            for (var gy = 0; gy < 20; gy++)
            {
                blocks.Add(Spawn(block, gx * 200, gy * 200));
            }
        }

        // A handful of blood particles: some sitting exactly on a block, some deliberately far
        // from all of them (in the gaps between the 200px-spaced grid).
        var onBlock1 = Spawn(blood, 0, 0);
        var onBlock2 = Spawn(blood, 3 * 200, 7 * 200);
        var inGap1 = Spawn(blood, 100, 100);
        var inGap2 = Spawn(blood, 2100, 2100); // within the block grid's span, but between cells

        var stepList = blocks.Concat(new[] { onBlock1, onBlock2, inGap1, inGap2 }).ToList();
        DrawManager.UpdateCollisions(stepList);

        Assert.AreEqual(2, _fired.Count(f => f == "blood-hit-block"),
            "exactly the two blood particles actually overlapping a block should have collided");
    }

    [TestMethod]
    public void ASolidRevertRegridsSoALaterCheckSeesTheRevertedPosition()
    {
        // block2 is placed exactly where mover would land after being pushed back by block1's
        // solid collision. If the grid isn't re-bucketed after that revert, mover's stale
        // (pre-revert, already-penetrating-block1) position would still be queried against
        // block2's grid entry and could miss (or wrongly hit) depending on where the stale
        // position happened to fall.
        var block = Define(1, "objBlock", solid: true);
        var mover = Define(2, "objMover", solid: true);
        mover.CollisionScript[1] = MarkerCode("mover-hit-block");
        InstanceManager.InitObjectMap();

        var block1 = Spawn(block, 100, 100);
        var block2 = Spawn(block, 132, 100); // immediately to the right of block1

        // mover starts just left of block1 and is currently overlapping it (simulating this
        // step's movement having walked it into the wall); its previous position is further left
        // still, which reverting should snap it back to -- nowhere near block2.
        var moverInst = Spawn(mover, 110, 100);
        moverInst.xprevious = 50;
        moverInst.yprevious = 100;

        DrawManager.UpdateCollisions(new List<GamemakerObject> { block1, block2, moverInst });

        Assert.AreEqual(50, moverInst.x, "should have been reverted back to its previous position");
        // Exactly once: at least once (it should still register hitting block1), but reverting to
        // (50,100) is nowhere near block2, so a stale (un-regridded) position wrongly matching it
        // would show up here as 2.
        Assert.AreEqual(1, _fired.Count(f => f == "mover-hit-block"));
    }

    private readonly List<string> _fired = new();

    private VMCode MarkerCode(string label)
    {
        var code = GameConverter.ConvertAssembly(null, """
                                                       :[0]
                                                       push.v self.id
                                                       popz.v
                                                       """);
        code.Name = label;
        code.OnCodeExecuted += () => _fired.Add(label);
        return code;
    }
}
