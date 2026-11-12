using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// <c>instance_change</c> has to re-bucket the instance under its new object index, not just swap
/// its <see cref="ObjectDefinition"/>. Everything that finds instances by object -- collision
/// candidates, <c>instance_exists</c>, <c>with (obj)</c>, an <c>obj.var</c> read -- goes through
/// <see cref="InstanceManager.ObjectMap"/>, and leaving the instance filed under its OLD object
/// makes it simultaneously invisible as its new type and still findable as its old one.
///
/// Shaped after the real case: I Wanna Spook Jam's Raynius stage ends with objRay_BossDoor turning
/// the player into objRay_ModifiedPlayer (<c>instance_change(objRay_ModifiedPlayer, true)</c>) and
/// then walking it into the door. objPlayer is object 1 and objRay_ModifiedPlayer 888, so the
/// player's Collision(objBlock) pair is driven from objPlayer's side but the modified player's is
/// driven from objBlock's -- the blocks look up object 888's instance list, which stayed empty, so
/// the ground stopped existing and the player fell straight through it mid-cutscene.
/// </summary>
[TestClass]
public class InstanceChangeTests
{
    private const int PlayerMaskSprite = 9601; // sprPlayerMask: 11 wide, 21 tall, origin (17, 23)
    private const int BlockSprite = 9602;      // sprBlockMask: a full 32x32, origin (0, 0)
    private const int GhostSprite = 9603;      // sprGhostKid: what both player objects draw

    private const int FloorTop = 416;
    private const int RestingY = FloorTop - 1 - 8; // mask bottom is y + (31 - 23)

    private ObjectDefinition _block = null!;
    private ObjectDefinition _player = null!;
    private ObjectDefinition _modifiedPlayer = null!;

    [TestInitialize]
    public void SetUp()
    {
        CompatFlags.LegacyCollision = true;

        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 100100;
        DrawManager.ResetCollisionCaches();
        VMExecutor.EnvStack.Clear();

        Sprite(PlayerMaskSprite, "sprPlayerMask", 32, 32, 12, 22, 11, 31, 17, 23);
        Sprite(BlockSprite, "sprBlockMask", 32, 32, 0, 31, 0, 31, 0, 0);
        Sprite(GhostSprite, "sprGhostKid", 32, 32, 3, 26, 11, 31, 17, 23);

        // The real asset indices, which is what decides who drives each collision pair.
        _block = Define(16, "objBlock", GhostSprite, solid: true);
        _block.sprite = BlockSprite;
        _player = Define(1, "objPlayer", GhostSprite, mask: PlayerMaskSprite);
        _modifiedPlayer = Define(888, "objRay_ModifiedPlayer", GhostSprite, mask: PlayerMaskSprite);

        // Both player objects author the same gml_Object_*_Collision_16.
        _player.CollisionScript[16] = Script("player_block", BlockCollision);
        _modifiedPlayer.CollisionScript[16] = Script("modified_block", BlockCollision);

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
        SpriteManager._spriteDict.Remove(GhostSprite);
        DrawManager.ResetCollisionCaches();
        VMExecutor.EnvStack.Clear();
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

    private static ObjectDefinition Define(int assetId, string name, int sprite, bool solid = false, int mask = -1)
    {
        var def = new ObjectDefinition
        {
            AssetId = assetId,
            Name = name,
            solid = solid,
            sprite = sprite,
            textureMaskId = mask
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

    private static GamemakerObject Spawn(ObjectDefinition def, double x, double y)
    {
        var instance = new GamemakerObject(def, x, y, def.depth, InstanceManager.NextInstanceID++, def.sprite, true, false, def.textureMaskId);
        instance.xprevious = x;
        instance.yprevious = y;
        return instance;
    }

    /// gml_Object_objPlayer_Collision_16 / gml_Object_objRay_ModifiedPlayer_Collision_16 -- the two
    /// are the same code, for upright gravity.
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

    /// A floor of 32x32 blocks with the player standing on it, exactly as the cutscene starts.
    private (List<DrawWithDepth> List, GamemakerObject Player) Stage()
    {
        var list = new List<DrawWithDepth>();
        for (var i = 0; i < 8; i++)
        {
            list.Add(Spawn(_block, i * 32, FloorTop));
        }

        var player = Spawn(_player, 48, RestingY);
        player.gravity = 0.4;
        list.Add(player);

        return (list, player);
    }

    private static void Step(List<DrawWithDepth> list)
    {
        InstanceManager.RememberOldPositions();
        InstanceManager.UpdatePositions();
        DrawManager.UpdateCollisions(list);
    }

    /// <summary>The door's <c>with (objPlayer) instance_change(objRay_ModifiedPlayer, true)</c>.</summary>
    private void BecomeModifiedPlayer(GamemakerObject player)
    {
        VMExecutor.EnvStack.Push(new VMEnvFrame { Self = player, ObjectDefinition = player.Definition });
        GameFunctions.instance_change([888, true]);
        VMExecutor.EnvStack.Pop();
    }

    [TestMethod]
    public void ThePlayerStandsOnTheFloorBeforeTheCutscene()
    {
        var (list, player) = Stage();

        for (var frame = 0; frame < 10; frame++)
        {
            Step(list);
        }

        Assert.AreEqual(FloorTop - 1, player.bbox_bottom, "sanity check: the floor holds the player up");
    }

    [TestMethod]
    public void TheFloorStillHoldsUpAPlayerTurnedIntoAnotherObject()
    {
        var (list, player) = Stage();
        Step(list);

        BecomeModifiedPlayer(player);

        for (var frame = 0; frame < 10; frame++)
        {
            Step(list);
        }

        Assert.AreEqual(888, player.object_index);
        Assert.AreEqual(FloorTop - 1, player.bbox_bottom, "the modified player fell through the floor");
    }

    [TestMethod]
    public void ChangingObjectRefilesTheInstanceUnderItsNewObject()
    {
        var (_, player) = Stage();

        BecomeModifiedPlayer(player);

        CollectionAssert.DoesNotContain(InstanceManager.ObjectMap[1].Instances, player,
            "it isn't an objPlayer any more -- instance_exists/with/obj.var must stop finding it as one");
        CollectionAssert.Contains(InstanceManager.ObjectMap[888].Instances, player,
            "everything that looks instances up by object goes through this list");
    }

    [TestMethod]
    public void ChangingObjectTakesOnTheNewObjectsOwnFields()
    {
        var (_, player) = Stage();
        _modifiedPlayer.solid = true;
        _modifiedPlayer.depth = -5;
        _modifiedPlayer.persistent = true;
        _modifiedPlayer.visible = false;

        BecomeModifiedPlayer(player);

        // Real GM re-seeds all of these from the new object before running its Create event.
        Assert.IsTrue(player.solid, "solid comes from the new object");
        Assert.IsTrue(InstanceManager.SolidInstances.Contains(player), "and has to stay tracked");
        Assert.AreEqual(-5, player.depth);
        Assert.IsTrue(player.persistent);
        Assert.IsFalse(player.visible);
        Assert.AreEqual(PlayerMaskSprite, player.mask_index, "mask_index comes from the new object too");
    }
}
