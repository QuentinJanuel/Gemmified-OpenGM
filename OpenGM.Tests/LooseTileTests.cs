using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The pre-Studio loose-tile API (tile_add and the tile_get_*/tile_set_*/tile_layer_* family it
/// hands ids to). Found by running the per-game unresolved-call scan across the other GMS games in
/// the corpus rather than waiting for a crash: I Wanna Be The Continuity uses tile_set_position,
/// tile_set_blend, tile_set_alpha, tile_set_scale, tile_set_region, tile_get_x/y and
/// tile_layer_shift, none of which were registered.
///
/// Not to be confused with the tile_get_index/tile_set_flip/... group in the same file, which is
/// GMS2's tilemap API -- same prefix, different addressing (tile DATA values, not tile ids).
/// </summary>
[TestClass]
public class LooseTileTests
{
    private const int BackgroundId = 9701;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        RoomManager.CurrentRoom = new RoomContainer(new Room { Name = "tile_test" });
        ScriptResolver.InitGMLFunctions();
    }

    [TestCleanup]
    public void TearDown()
    {
        foreach (var tile in RoomManager.CurrentRoom.Tiles.ToList())
        {
            tile.Destroy();
        }

        RoomManager.CurrentRoom.Tiles.Clear();
        RoomManager.CurrentRoom = null!;
    }

    /// tile_add(background, left, top, width, height, x, y, depth)
    private static int Add(double x, double y, double depth = 100)
        => LayerFunctions.tile_add([BackgroundId, 0, 0, 32, 32, x, y, depth]).Conv<int>();

    private static GMTile Tile(int id) => RoomManager.CurrentRoom.Tiles.Cast<GMTile>().First(t => t.instanceId == id);

    [TestMethod]
    public void TileAddReturnsDistinctIdsAndFilesThemInTheRoom()
    {
        var a = Add(0, 0);
        var b = Add(32, 0);

        Assert.AreNotEqual(a, b, "each tile needs its own id -- everything else addresses them by it");
        Assert.AreEqual(2, RoomManager.CurrentRoom.Tiles.Count);
        Assert.IsTrue(LayerFunctions.tile_exists([a]).Conv<bool>());
        Assert.IsFalse(LayerFunctions.tile_exists([a + 12345]).Conv<bool>());
    }

    [TestMethod]
    public void TileLayerFindLocatesATileByDepthAndPoint()
    {
        var id = Add(64, 96, depth: 500);
        Add(0, 0, depth: 500);

        Assert.AreEqual(id, LayerFunctions.tile_layer_find([500d, 70d, 100d]).Conv<int>());
        Assert.AreEqual(-1, LayerFunctions.tile_layer_find([499d, 70d, 100d]).Conv<int>(), "wrong depth");
        Assert.AreEqual(-1, LayerFunctions.tile_layer_find([500d, 300d, 300d]).Conv<int>(), "nothing there");
    }

    [TestMethod]
    public void PositionRegionAndScaleRoundTrip()
    {
        var id = Add(10, 20);

        LayerFunctions.tile_set_position([id, 111d, 222d]);
        Assert.AreEqual(111d, LayerFunctions.tile_get_x([id]).Conv<double>());
        Assert.AreEqual(222d, LayerFunctions.tile_get_y([id]).Conv<double>());

        LayerFunctions.tile_set_region([id, 5, 6, 7, 8]);
        Assert.AreEqual(5, LayerFunctions.tile_get_left([id]).Conv<int>());
        Assert.AreEqual(6, LayerFunctions.tile_get_top([id]).Conv<int>());
        Assert.AreEqual(7, LayerFunctions.tile_get_width([id]).Conv<int>());
        Assert.AreEqual(8, LayerFunctions.tile_get_height([id]).Conv<int>());

        LayerFunctions.tile_set_scale([id, 2d, 3d]);
        Assert.AreEqual(2d, LayerFunctions.tile_get_xscale([id]).Conv<double>());
        Assert.AreEqual(3d, LayerFunctions.tile_get_yscale([id]).Conv<double>());
    }

    [TestMethod]
    public void BlendAndAlphaSharePackedColourWithoutClobberingEachOther()
    {
        var id = Add(0, 0);

        // Both live in GMTile.Color (packed ABGR), so the risk is one write wiping the other.
        Assert.AreEqual(1d, LayerFunctions.tile_get_alpha([id]).Conv<double>(), "tiles start opaque");

        LayerFunctions.tile_set_blend([id, 0x0000FF]);
        Assert.AreEqual(0x0000FF, LayerFunctions.tile_get_blend([id]).Conv<int>());
        Assert.AreEqual(1d, LayerFunctions.tile_get_alpha([id]).Conv<double>(), "setting blend must not touch alpha");

        LayerFunctions.tile_set_alpha([id, 0.5]);
        Assert.AreEqual(0x0000FF, LayerFunctions.tile_get_blend([id]).Conv<int>(), "setting alpha must not touch blend");
        Assert.AreEqual(0.5, LayerFunctions.tile_get_alpha([id]).Conv<double>(), 1.0 / 255);
    }

    [TestMethod]
    public void LayerShiftMovesEveryTileAtThatDepthAndNoOthers()
    {
        var onLayer = Add(100, 100, depth: 700);
        var elsewhere = Add(100, 100, depth: 800);

        LayerFunctions.tile_layer_shift([700d, 10d, -5d]);

        Assert.AreEqual(110d, Tile(onLayer).X);
        Assert.AreEqual(95d, Tile(onLayer).Y);
        Assert.AreEqual(100d, Tile(elsewhere).X, "a different depth is a different layer");
        Assert.AreEqual(100d, Tile(elsewhere).Y);
    }

    [TestMethod]
    public void LayerHideShowAndDeleteActOnWholeLayers()
    {
        var onLayer = Add(0, 0, depth: 700);
        var elsewhere = Add(0, 0, depth: 800);

        LayerFunctions.tile_layer_hide([700d]);
        Assert.IsFalse(LayerFunctions.tile_get_visible([onLayer]).Conv<bool>());
        Assert.IsTrue(LayerFunctions.tile_get_visible([elsewhere]).Conv<bool>());

        LayerFunctions.tile_layer_show([700d]);
        Assert.IsTrue(LayerFunctions.tile_get_visible([onLayer]).Conv<bool>());

        LayerFunctions.tile_layer_delete([700d]);
        Assert.IsFalse(LayerFunctions.tile_exists([onLayer]).Conv<bool>());
        Assert.IsTrue(LayerFunctions.tile_exists([elsewhere]).Conv<bool>(), "only that layer");
    }

    [TestMethod]
    public void TileDeleteRemovesOneTile()
    {
        var a = Add(0, 0);
        var b = Add(32, 0);

        LayerFunctions.tile_delete([a]);

        Assert.IsFalse(LayerFunctions.tile_exists([a]).Conv<bool>());
        Assert.IsTrue(LayerFunctions.tile_exists([b]).Conv<bool>());
        Assert.AreEqual(1, RoomManager.CurrentRoom.Tiles.Count);
    }
}
