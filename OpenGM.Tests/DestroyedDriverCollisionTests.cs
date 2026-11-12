using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// An instance destroyed by this frame's own Collision code must not go on colliding with the rest
/// of the frame's candidates. GML's <c>instance_destroy()</c> only MARKS an instance --
/// <c>Destroyed</c> isn't set until RemoveMarked() actually deletes it, long after UpdateCollisions
/// has returned -- so a driver-liveness check written against <c>Destroyed</c> alone never trips
/// for the case that matters.
///
/// Shaped after the real case: I Wanna Spook Jam's tutorial room (rPatIntro) has two
/// objIntroTinyMushroom patrolling the same strip of floor, and they walk through each other.
/// objBullet is object 12 and the mushroom 112, so the bullet drives the pair and finds both
/// mushrooms in one frame. Each mushroom's Collision(objBullet) destroys itself AND the bullet
/// (<c>with (other) instance_destroy()</c>) -- so real GameMaker stops after the first one, while
/// here the dead bullet carried on and killed the second mushroom too.
/// </summary>
[TestClass]
public class DestroyedDriverCollisionTests
{
    private const int BulletSprite = 9501;   // sprGhostBullet: 4x4, origin (1, 1)
    private const int MushroomSprite = 9502; // sprIntroTinyMushroom: 32x32, origin (16, 16)

    private ObjectDefinition _bullet = null!;
    private ObjectDefinition _mushroom = null!;

    private int _mushroomCollisions;

    [TestInitialize]
    public void SetUp()
    {
        CompatFlags.LegacyCollision = true;

        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        // Real instance ids, so instance_destroy(id) below takes its instance branch rather than
        // reading the argument as an asset index (GMConstants.FIRST_INSTANCE_ID).
        InstanceManager.NextInstanceID = 100336;
        DrawManager.ResetCollisionCaches();
        VMExecutor.EnvStack.Clear();
        _mushroomCollisions = 0;

        Sprite(BulletSprite, "sprGhostBullet", 4, 4, 0, 3, 0, 3, 1, 1);
        Sprite(MushroomSprite, "sprIntroTinyMushroom", 32, 32, 0, 31, 3, 31, 16, 16);

        // The real asset indices: the bullet is the lower one, so it drives the merged pair.
        _bullet = Define(12, "objBullet", BulletSprite);
        _mushroom = Define(112, "objIntroTinyMushroom", MushroomSprite);
        _mushroom.CollisionScript[12] = Script("mushroom_bullet", MushroomHitByBullet);

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
        SpriteManager._spriteDict.Remove(BulletSprite);
        SpriteManager._spriteDict.Remove(MushroomSprite);
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

    private static ObjectDefinition Define(int assetId, string name, int sprite)
    {
        var def = new ObjectDefinition { AssetId = assetId, Name = name, sprite = sprite };
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
        var instance = new GamemakerObject(def, x, y, def.depth, InstanceManager.NextInstanceID++, def.sprite, true, false, -1);
        instance.xprevious = x;
        instance.yprevious = y;
        return instance;
    }

    /// gml_Object_objIntroTinyMushroom_Collision_12: take out the mushroom and the bullet both.
    private void MushroomHitByBullet()
    {
        _mushroomCollisions++;
        GameFunctions.instance_destroy([]);
        GameFunctions.instance_destroy([VMExecutor.Other.GMSelf.instanceId]);
    }

    private static bool Dead(GamemakerObject instance) => instance.Marked || instance.Destroyed;

    [TestMethod]
    public void ABulletTakesOutTheOneMushroomItHits()
    {
        var mushroom = Spawn(_mushroom, 1104, 432);
        var bullet = Spawn(_bullet, 1104, 432);
        var list = new List<DrawWithDepth> { mushroom, bullet };

        DrawManager.UpdateCollisions(list);

        Assert.IsTrue(Dead(mushroom), "the mushroom the bullet hit should die");
        Assert.IsTrue(Dead(bullet), "the mushroom's own collision code destroys the bullet too");
    }

    [TestMethod]
    public void ABulletTakesOutOnlyOneOfTwoOverlappingMushrooms()
    {
        // The two rPatIntro mushrooms walking through each other: 32px wide masks, 6px apart, with
        // the bullet inside both.
        var first = Spawn(_mushroom, 1104, 432);
        var second = Spawn(_mushroom, 1110, 432);
        var bullet = Spawn(_bullet, 1107, 432);
        var list = new List<DrawWithDepth> { first, second, bullet };

        Assert.IsTrue(CollisionManager.Collision_Instance(bullet, first, true), "sanity check: bullet is inside the first");
        Assert.IsTrue(CollisionManager.Collision_Instance(bullet, second, true), "sanity check: bullet is inside the second");

        DrawManager.UpdateCollisions(list);

        Assert.AreEqual(1, _mushroomCollisions, "the bullet is gone after the first hit -- it can't hit anything else");
        Assert.IsTrue(Dead(bullet));
        Assert.AreEqual(1, new[] { first, second }.Count(Dead), "one bullet, one mushroom");
    }

    [TestMethod]
    public void ADriverThatDestroysItselfStillLetsTheOtherSideRun()
    {
        // The other common shape of the same pattern -- "the bullet deletes itself, the enemy
        // subtracts hp" -- with the self-destruct on the DRIVER side (objBullet is the lower index,
        // so it drives), running before the target's own handler for that same pair. Real GameMaker
        // issues both of a pair's events regardless of what the first did, so the hit still lands:
        // it's the REMAINING candidates a dead driver has to stop colliding with, not the pair it's
        // already in the middle of.
        var hits = 0;
        _bullet.CollisionScript[112] = Script("bullet_mushroom", () => GameFunctions.instance_destroy([]));
        _mushroom.CollisionScript[12] = Script("mushroom_hit", () => hits++);
        DrawManager.ResetCollisionCaches();

        var mushroom = Spawn(_mushroom, 1104, 432);
        var bullet = Spawn(_bullet, 1104, 432);
        var list = new List<DrawWithDepth> { mushroom, bullet };

        DrawManager.UpdateCollisions(list);

        Assert.AreEqual(1, hits, "the mushroom's own handler still runs for the pair the bullet died in");
        Assert.IsTrue(Dead(bullet));
        Assert.IsFalse(Dead(mushroom), "nothing in this test destroys the mushroom");
    }

    [TestMethod]
    public void EachOfTwoBulletsTakesOutItsOwnMushroom()
    {
        // Same overlap, but with a bullet for each: the second bullet is still live when its turn
        // comes, so the second mushroom does die. Guards against fixing the above by suppressing
        // the second pair outright.
        var first = Spawn(_mushroom, 1104, 432);
        var second = Spawn(_mushroom, 1110, 432);
        var bulletA = Spawn(_bullet, 1107, 432);
        var bulletB = Spawn(_bullet, 1107, 432);
        var list = new List<DrawWithDepth> { first, second, bulletA, bulletB };

        DrawManager.UpdateCollisions(list);

        Assert.AreEqual(2, _mushroomCollisions);
        Assert.IsTrue(Dead(first) && Dead(second), "two bullets, two mushrooms");
        Assert.IsTrue(Dead(bulletA) && Dead(bulletB));
    }
}
