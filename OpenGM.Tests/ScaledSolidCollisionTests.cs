using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// Reproduction of "the player falls through the ground in rLevel1" (I wanna CoinStack 1000), using
/// that room's real numbers.
///
/// The hub builds its floor out of 109 objBlock instances at scale 1,1 and works. Every level room
/// instead stretches a handful of blocks: rLevel1's floor is ONE objBlock at (128, 512) with
/// image_xscale 21 and image_yscale 3 -- a 672x96 slab from a 32x32 sprite. objBlock is invisible
/// (the visible ground is tiles), so a collision that only covers the unscaled 32x32 corner looks
/// exactly like falling through solid-looking ground.
///
/// sprBlockMask is 32x32, origin 0,0, fully opaque (margins 0..31) and SepMasks=Precise, and so is
/// the player's mask -- so this goes down the per-pixel path, not the cheap bbox one.
/// </summary>
[TestClass]
public class ScaledSolidCollisionTests
{
    private const int BlockSprite = 9300;
    private const int PlayerSprite = 9301;

    // rLevel1, verbatim from the room file.
    private const double FloorX = 128;
    private const double FloorY = 512;
    private const double FloorXScale = 21;
    private const double FloorYScale = 3;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;
        DrawManager.ResetCollisionCaches();

        // sprBlockMask: a fully opaque 32x32 square.
        SpriteManager._spriteDict[BlockSprite] = new SpriteData
        {
            AssetIndex = BlockSprite,
            Name = "sprBlockMask",
            Width = 32,
            Height = 32,
            OriginX = 0,
            OriginY = 0,
            MarginLeft = 0,
            MarginRight = 31,
            MarginTop = 0,
            MarginBottom = 31,
            SepMasks = UndertaleSprite.SepMaskType.Precise,
            CollisionMasks = [Opaque(32, 32)],
            Textures = [new SpritePageItem()]
        };

        // sprPlayerMask: origin 17,23, opaque within its margins.
        SpriteManager._spriteDict[PlayerSprite] = new SpriteData
        {
            AssetIndex = PlayerSprite,
            Name = "sprPlayerMask",
            Width = 32,
            Height = 32,
            OriginX = 17,
            OriginY = 23,
            MarginLeft = 12,
            MarginRight = 22,
            MarginTop = 11,
            MarginBottom = 31,
            SepMasks = UndertaleSprite.SepMaskType.Precise,
            CollisionMasks = [Opaque(32, 32)],
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
        SpriteManager._spriteDict.Remove(BlockSprite);
        SpriteManager._spriteDict.Remove(PlayerSprite);
        DrawManager.ResetCollisionCaches();
    }

    /// <summary>An all-bits-set collision mask, the shape a solid square block has.</summary>
    private static byte[] Opaque(int width, int height)
    {
        var stride = (width + 7) / 8;
        var mask = new byte[stride * height];
        Array.Fill(mask, (byte)0xFF);
        return mask;
    }

    private static GamemakerObject SpawnFloor()
    {
        var def = new ObjectDefinition
        {
            AssetId = 18, Name = "objBlock", solid = true, sprite = BlockSprite, visible = false
        };
        InstanceManager.ObjectDefinitions[18] = def;
        InstanceManager.InitObjectMap();

        var floor = new GamemakerObject(def, FloorX, FloorY, def.depth,
            InstanceManager.NextInstanceID++, BlockSprite, false, false, -1);
        floor.image_xscale = FloorXScale;
        floor.image_yscale = FloorYScale;
        return floor;
    }

    private static GamemakerObject SpawnPlayer(double x, double y)
    {
        var def = new ObjectDefinition { AssetId = 10, Name = "objPlayer", sprite = PlayerSprite };
        InstanceManager.ObjectDefinitions[10] = def;
        InstanceManager.InitObjectMap();

        return new GamemakerObject(def, x, y, def.depth,
            InstanceManager.NextInstanceID++, PlayerSprite, true, false, -1);
    }

    [TestMethod]
    public void AScaledBlockCoversItsWholeStretchedBoundingBox()
    {
        var floor = SpawnFloor();

        Assert.AreEqual(128, floor.bbox_left, 1e-9, "left edge");
        Assert.AreEqual(512, floor.bbox_top, 1e-9, "top edge");
        Assert.AreEqual(128 + (32 * FloorXScale), floor.bbox_right, 1e-9, "21 tiles wide, not 1");
        Assert.AreEqual(512 + (32 * FloorYScale), floor.bbox_bottom, 1e-9, "3 tiles tall, not 1");
    }

    /// <summary>
    /// objPlayerStart sits at (160, 480) and spawns the player at (x+17, y+23) -- dead centre of the
    /// floor slab's left end, standing on top of it. One pixel down must be blocked.
    /// </summary>
    [TestMethod]
    public void ThePlayerStandsOnTheStretchedFloorAtSpawn()
    {
        SpawnFloor();
        var player = SpawnPlayer(177, 503);

        Assert.IsFalse(CollisionManager.Command_PlaceFree(player, player.x, player.y + 1, solidOnly: true),
            "one pixel into the floor is not free -- this is the fall-through");
    }

    /// <summary>
    /// The whole span, not just the first 32 pixels. A collision that ignored image_xscale would
    /// pass at the left end and fail everywhere else, which is what falling through looks like.
    /// </summary>
    [TestMethod]
    public void TheFloorIsSolidAlongItsEntireLength()
    {
        SpawnFloor();
        var player = SpawnPlayer(177, 503);

        for (var offset = 0; offset < 32 * FloorXScale; offset += 16)
        {
            var px = 145 + offset; // 145 puts the player's mask just inside the slab's left edge
            player.x = px;
            player.bbox_dirty = true;

            Assert.IsFalse(CollisionManager.Command_PlaceFree(player, px, player.y + 1, solidOnly: true),
                $"floor should be solid at x={px}, {offset}px along a {32 * FloorXScale}px slab");
        }
    }

    [TestMethod]
    public void AboveAndBesideTheFloorIsStillFree()
    {
        SpawnFloor();
        var player = SpawnPlayer(177, 400);

        Assert.IsTrue(CollisionManager.Command_PlaceFree(player, player.x, player.y, solidOnly: true),
            "well above the floor");

        Assert.IsTrue(CollisionManager.Command_PlaceFree(player, 60, 503, solidOnly: true),
            "left of where the slab starts");
    }
}
