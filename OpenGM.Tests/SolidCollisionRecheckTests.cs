using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// A solid collision pair has to still be colliding at the moment it's handled, not just at the
/// moment the frame's candidate list was gathered. Resolving one pair moves the instance -- which
/// is usually exactly what stops it colliding with the rest of the pairs found alongside it.
///
/// Shaped after the real case: I Wanna Spook Jam's floors are rows of separate 32x32 objBlock
/// instances, and objPlayer's mask is only 11px wide, so a landing straddles two of them about a
/// third of the time. Both were gathered as this frame's collision matches; the first landed the
/// player (move_contact_solid + vspeed = 0), and the second then snapped it back up to yprevious
/// for a collision that no longer existed -- leaving it hanging in the air with no speed. Gravity
/// then had to build back up from 0, so the last few pixels of every such fall took ~6 frames
/// instead of landing on the spot.
/// </summary>
[TestClass]
public class SolidCollisionRecheckTests
{
    private const int PlayerMaskSprite = 9401; // sprPlayerMask: 11 wide, 21 tall, origin (17, 23)
    private const int BlockSprite = 9402;      // sprBlockMask: a full 32x32, origin (0, 0)

    private const int FloorTop = 416;
    private const int RestingY = FloorTop - 1 - 8; // mask bottom is y + (31 - 23)

    private ObjectDefinition _block = null!;
    private ObjectDefinition _player = null!;

    [TestInitialize]
    public void SetUp()
    {
        CompatFlags.LegacyCollision = true;

        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        DrawManager.ResetCollisionCaches();

        Sprite(PlayerMaskSprite, "sprPlayerMask", 32, 32, 12, 22, 11, 31, 17, 23);
        Sprite(BlockSprite, "sprBlockMask", 32, 32, 0, 31, 0, 31, 0, 0);

        _block = Define(16, "objBlock", BlockSprite, solid: true);
        _player = Define(1, "objPlayer", PlayerMaskSprite);
        _player.CollisionScript[16] = Script("block", BlockCollision);

        InstanceManager.InitObjectMap();
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
        SpriteManager._spriteDict.Remove(PlayerMaskSprite);
        SpriteManager._spriteDict.Remove(BlockSprite);
        DrawManager.ResetCollisionCaches();
        CompatFlags.LegacyCollision = false;
    }

    private static void Sprite(int id, string name, int w, int h, int ml, int mr, int mt, int mb, int ox, int oy)
    {
        SpriteManager._spriteDict[id] = new SpriteData
        {
            AssetIndex = id,
            Name = name,
            Width = w,
            Height = h,
            MarginLeft = ml,
            MarginRight = mr,
            MarginTop = mt,
            MarginBottom = mb,
            OriginX = ox,
            OriginY = oy,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
            Textures = [new SpritePageItem()]
        };
    }

    private static ObjectDefinition Define(int assetId, string name, int sprite, bool solid = false)
    {
        var def = new ObjectDefinition { AssetId = assetId, Name = name, solid = solid, sprite = sprite };
        InstanceManager.ObjectDefinitions.Add(assetId, def);
        return def;
    }

    private static VMCode Script(string label, Action body)
    {
        var code = GameConverter.ConvertAssembly(null, """
                                                       :[0]
                                                       push.v self.id
                                                       popz.v
                                                       """);
        code.Name = label;
        code.OnCodeExecuted += body;
        return code;
    }

    private static GamemakerObject Spawn(ObjectDefinition def, double x, double y, int mask = -1)
    {
        var instance = new GamemakerObject(def, x, y, def.depth, InstanceManager.NextInstanceID++, def.sprite, true, false, mask);
        instance.xprevious = x;
        instance.yprevious = y;
        return instance;
    }

    /// gml_Object_objPlayer_Collision_16, for upright gravity.
    private static void BlockCollision()
    {
        var self = VMExecutor.Self.GMSelf;

        if (!CollisionManager.Command_PlaceFree(self, self.x + self.hspeed, self.y, solidOnly: true))
        {
            GameFunctions.move_contact_solid([self.hspeed > 0 ? 0d : 180d, Math.Abs(self.hspeed)]);
            self.hspeed = 0;
        }

        if (!CollisionManager.Command_PlaceFree(self, self.x, self.y + self.vspeed, solidOnly: true))
        {
            GameFunctions.move_contact_solid([self.vspeed > 0 ? 270d : 90d, Math.Abs(self.vspeed)]);
            self.vspeed = 0;
        }

        if (!CollisionManager.Command_PlaceFree(self, self.x + self.hspeed, self.y + self.vspeed, solidOnly: true))
        {
            self.hspeed = 0;
        }
    }

    /// <summary>
    /// A row of 32x32 floor blocks starting at x = 0, with the player dropped from 64px up at
    /// <paramref name="playerX"/>. Returns how far above the floor the player was left on the frame
    /// its fall was first arrested (0 == landed), and the frame it actually came to rest on the
    /// floor -- the two are the same frame unless something stopped it short of the ground.
    /// </summary>
    private (double GapWhenArrested, int RestingFrame) Drop(double playerX)
    {
        var list = new List<DrawWithDepth>();
        for (var i = 0; i < 4; i++)
        {
            list.Add(Spawn(_block, i * 32, FloorTop));
        }

        var player = Spawn(_player, playerX, RestingY - 64, mask: PlayerMaskSprite);
        player.gravity = 0.4;
        list.Add(player);
        Assert.IsTrue(player.bbox_bottom < FloorTop, "sanity check: the player starts above the floor");

        var gapWhenArrested = double.NaN;

        for (var frame = 1; frame <= 200; frame++)
        {
            InstanceManager.RememberOldPositions();

            // The relevant half of gml_Object_objPlayer_Step_0: clamp to maxVspeed.
            if (Math.Abs(player.vspeed) > 9)
            {
                player.vspeed = Math.Sign(player.vspeed) * 9;
            }

            InstanceManager.UpdatePositions();
            DrawManager.UpdateCollisions(list);

            var gap = FloorTop - 1 - player.bbox_bottom;

            if (double.IsNaN(gapWhenArrested) && player.vspeed == 0)
            {
                gapWhenArrested = gap;
            }

            if (gap == 0)
            {
                return (gapWhenArrested, frame);
            }
        }

        Assert.Fail("the player never reached the floor");
        return default;
    }

    [TestMethod]
    public void LandingInsideOneBlockStopsOnTheFloor()
    {
        // bbox 43..53 -- wholly within the block at x 32..63, so only one pair to resolve.
        var (gap, _) = Drop(48);

        Assert.AreEqual(0, gap, "player should be resting on the floor the frame its fall stops");
    }

    [TestMethod]
    public void LandingAcrossTwoBlocksStopsOnTheFloorToo()
    {
        // bbox 27..37 -- straddling the seam between the blocks at x 0..31 and x 32..63, so this
        // frame gathers two colliding blocks. Resolving the first lands the player and ends the
        // second's collision; the second must not then drag it back up to where it started.
        var (gap, _) = Drop(32);

        Assert.AreEqual(0, gap, "player should be resting on the floor the frame its fall stops");
    }

    [TestMethod]
    public void StraddlingASeamDoesNotMakeTheFallTakeLonger()
    {
        // The perceptible half of the bug: stalled short of the floor, the player had to rebuild
        // speed from 0 at 0.4/frame, so the same drop took several extra frames to finish.
        var (_, inside) = Drop(48);

        TearDown();
        SetUp();

        var (_, straddling) = Drop(32);

        Assert.AreEqual(inside, straddling, "the same fall shouldn't take extra frames over a seam");
    }
}
