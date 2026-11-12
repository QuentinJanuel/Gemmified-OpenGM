using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// A rotated instance's bounding box has to land on whole pixels, like an unrotated one. Legacy
/// collision treats right/bottom as inclusive and compares them with a +1, so a box that comes back
/// from sin/cos a fraction short of an integer behaves as if it were a whole pixel bigger.
///
/// Shaped after the real case: I Wanna Be The Continuity's df_rCoop1 stands its 16x32
/// coopBreakable blocks on their side (image_angle 270) so they read as 32x16, sitting flush with
/// the top of the objBlock floor beside them. Walking that floor jammed the player one pixel before
/// the breakable, with no way past but a jump.
/// </summary>
[TestClass]
public class RotatedBBoxTests
{
    private const int PlayerMaskSprite = 9201; // sprPlayerMask
    private const int BlockSprite = 9202;      // sprBlockMask
    private const int BreakableSprite = 9203;  // sprCoopBreakable

    private ObjectDefinition _block = null!;
    private ObjectDefinition _breakable = null!;
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
        Sprite(BreakableSprite, "sprCoopBreakable", 16, 32, 0, 15, 0, 31, 0, 0);

        _block = Define(20, "objBlock", BlockSprite, solid: true);
        _breakable = Define(116, "coopBreakable", BreakableSprite, solid: true, parent: _block);
        _player = Define(12, "objPlayer", PlayerMaskSprite);

        // objPlayer's Collision(objBlock): resolve against solids, then stop.
        _player.CollisionScript[20] = Script("block", BlockCollision);

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
        SpriteManager._spriteDict.Remove(BreakableSprite);
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

    private static ObjectDefinition Define(int assetId, string name, int sprite, bool solid = false, ObjectDefinition? parent = null)
    {
        var def = new ObjectDefinition
        {
            AssetId = assetId,
            Name = name,
            solid = solid,
            parent = parent,
            sprite = sprite
        };

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

    private static GamemakerObject Spawn(
        ObjectDefinition def, double x, double y, double angle = 0, double xscale = 1, double yscale = 1, int mask = -1)
    {
        var instance = new GamemakerObject(def, x, y, def.depth, InstanceManager.NextInstanceID++, def.sprite, true, false, mask);
        instance.image_xscale = xscale;
        instance.image_yscale = yscale;
        instance.image_angle = angle;

        // Solid pairs get snapped back to xprevious/yprevious before their event, so a
        // standing-still instance has to start with those matching where it actually is.
        instance.xprevious = x;
        instance.yprevious = y;

        return instance;
    }

    /// The relevant half of gml_Object_objPlayer_Collision_20, for upright gravity.
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

    [TestMethod]
    public void AQuarterTurnedBoxLandsOnWholePixels()
    {
        // 16x32 mask, origin (0,0), turned 270 degrees about its own origin: 32 wide, 16 tall,
        // reaching left from x and down from y.
        var breakable = Spawn(_breakable, 320, 416, angle: 270);

        Assert.AreEqual(289, breakable.bbox_left);
        Assert.AreEqual(416, breakable.bbox_top);
        Assert.AreEqual(320, breakable.bbox_right);
        Assert.AreEqual(431, breakable.bbox_bottom);
    }

    [TestMethod]
    public void AQuarterTurnedBoxIsLevelWithAnUnrotatedOneAtTheSameHeight()
    {
        var floor = Spawn(_block, 0, 416, xscale: 9, yscale: 5);
        var breakable = Spawn(_breakable, 320, 416, angle: 270);

        // What the room author laid out, and what the player walks along.
        Assert.AreEqual(floor.bbox_top, breakable.bbox_top);
    }

    [TestMethod]
    public void ThePlayerWalksFromTheFloorAcrossLevelQuarterTurnedBlocks()
    {
        var list = new List<DrawWithDepth>();

        // df_rCoop1: objBlock floor with bbox (0,416)-(287,575), then three coopBreakable at
        // x 320/352/384, each turned 270 degrees, all with their tops at y=416.
        list.Add(Spawn(_block, 0, 416, xscale: 9, yscale: 5));
        list.Add(Spawn(_breakable, 320, 416, angle: 270));
        list.Add(Spawn(_breakable, 352, 416, angle: 270));
        list.Add(Spawn(_breakable, 384, 416, angle: 270));

        // Standing on the floor: mask bottom is y+8, so feet at 415 == floor top - 1.
        var player = Spawn(_player, 250, 407, mask: PlayerMaskSprite);
        player.gravity = 0.4;
        list.Add(player);

        Assert.AreEqual(415, player.bbox_bottom, "player should be resting exactly on the floor");

        for (var frame = 0; frame < 45; frame++)
        {
            InstanceManager.RememberOldPositions();
            player.hspeed = 3; // holding right at maxSpeed
            InstanceManager.UpdatePositions();
            DrawManager.UpdateCollisions(list);
        }

        // Free of the last breakable (right edge 384) rather than stopped at the first one, and
        // still walking along the top rather than having fallen or been pushed up.
        Assert.IsTrue(player.x > 384, $"player got stuck at x={player.x}");
        Assert.AreEqual(415, player.bbox_bottom);
    }
}
