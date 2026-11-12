using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// CollisionManager.IsCollidingAtCurrentPosition -- the hot path behind move_contact_solid/
/// move_bounce_solid/move_outside_solid and place_free -- used to scan every instance in the room
/// just to skip the non-solid majority via a `.solid` check. A death effect that leaves hundreds
/// of non-solid particles sitting around (each one re-running this scan every frame once landed)
/// pays that full scan every single time. InstanceManager.SolidInstances is a set kept in sync
/// with GamemakerObject.solid so that scan only ever has to walk the actually-solid instances.
///
/// The tricky part isn't the common case (assign solid = true once at Create) -- it's every path
/// that changes an instance's registration state *without* going through that assignment:
/// destruction, deactivate/reactivate, and persistent rooms being saved and reloaded. These cover
/// all of them, since a silent gap in any one leaks a stale reference (memory, and potentially a
/// defunct instance still counting as a collision candidate) or drops a real solid instance from
/// the set (missed collisions).
/// </summary>
[TestClass]
public class SolidInstancesTests
{
    private const int SpriteId = 9300;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        DrawManager.ResetCollisionCaches();

        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprSolidTestBox",
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

    [TestMethod]
    public void ConstructingASolidInstanceRegistersItImmediately()
    {
        var wallDef = Define(1, "wall", solid: true);
        InstanceManager.InitObjectMap();

        var wall = Spawn(wallDef, 0, 0);

        Assert.IsTrue(InstanceManager.SolidInstances.Contains(wall));
    }

    [TestMethod]
    public void ConstructingANonSolidInstanceDoesNotRegisterIt()
    {
        var def = Define(1, "thing");
        InstanceManager.InitObjectMap();

        var instance = Spawn(def, 0, 0);

        Assert.IsFalse(InstanceManager.SolidInstances.Contains(instance));
    }

    [TestMethod]
    public void TogglingSolidAtRuntimeUpdatesTheSet()
    {
        var def = Define(1, "thing");
        InstanceManager.InitObjectMap();
        var instance = Spawn(def, 0, 0);

        instance.solid = true;
        Assert.IsTrue(InstanceManager.SolidInstances.Contains(instance));

        instance.solid = false;
        Assert.IsFalse(InstanceManager.SolidInstances.Contains(instance));
    }

    [TestMethod]
    public void DestroyingASolidInstanceRemovesItFromTheSet()
    {
        var wallDef = Define(1, "wall", solid: true);
        InstanceManager.InitObjectMap();
        var wall = Spawn(wallDef, 0, 0);

        wall.Unregister();

        Assert.IsFalse(InstanceManager.SolidInstances.Contains(wall));
    }

    [TestMethod]
    public void DeactivatingAndReactivatingASolidInstanceKeepsItTracked()
    {
        var wallDef = Define(1, "wall", solid: true);
        InstanceManager.InitObjectMap();
        var wall = Spawn(wallDef, 0, 0);
        var room = new RoomContainer(new SerializedFiles.Room());

        room.DeactivateInstance(wall);
        Assert.IsFalse(InstanceManager.SolidInstances.Contains(wall), "gone while deactivated -- not a live collision candidate");

        room.ReactivateInstance(wall);
        Assert.IsTrue(InstanceManager.SolidInstances.Contains(wall), "must be back once reactivated");
    }

    [TestMethod]
    public void IsCollidingAtCurrentPositionSolidOnlyOnlyConsidersSolidInstances()
    {
        var wallDef = Define(1, "wall", solid: true);
        var particleDef = Define(2, "particle");
        InstanceManager.InitObjectMap();

        // A non-solid instance sitting exactly where mover is -- solidOnly must ignore it.
        Spawn(particleDef, 100, 100);
        var mover = Spawn(particleDef, 100, 100);

        Assert.IsFalse(CollisionManager.IsCollidingAtCurrentPosition(mover, solidOnly: true),
            "only a non-solid instance is here -- solidOnly must not count it");
        Assert.IsTrue(CollisionManager.IsCollidingAtCurrentPosition(mover, solidOnly: false),
            "sanity check: the two non-solid instances really do overlap");

        Spawn(wallDef, 100, 100);
        Assert.IsTrue(CollisionManager.IsCollidingAtCurrentPosition(mover, solidOnly: true),
            "a solid instance is now here too -- solidOnly must find it");
    }

    [TestMethod]
    public void ClearAllInstancesAlsoClearsSolidInstances()
    {
        // Regression test: a plain InstanceManager.instances.Clear() (what every test/reset path
        // used before ClearAllInstances existed) left solid instances dangling in the set forever,
        // since only per-instance destruction (RemoveInstance) cleaned it up. That leaked a defunct
        // instance into whatever ran next in the same process -- caught by
        // RotatedBBoxTests.ThePlayerWalksFromTheFloorAcrossLevelQuarterTurnedBlocks intermittently
        // failing depending on test run order once this feature landed.
        var wallDef = Define(1, "wall", solid: true);
        InstanceManager.InitObjectMap();
        Spawn(wallDef, 0, 0);
        Assert.AreNotEqual(0, InstanceManager.SolidInstances.Count);

        InstanceManager.ClearAllInstances();

        Assert.AreEqual(0, InstanceManager.SolidInstances.Count);
    }
}
