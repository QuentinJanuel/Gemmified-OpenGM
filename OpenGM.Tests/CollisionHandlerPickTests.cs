using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// Which of an instance's Collision handlers fires for a given pair: the most specific
/// ancestor-or-self of the OTHER instance's type that THIS instance actually authored a handler for.
///
/// UpdateCollisions used to resolve that against the merged `targets` list instead of against the
/// driver's own handlers -- but that list is a merge of BOTH sides' authored pairs, so it contains
/// ids the driver has no handler for at all, and picking one of those meant the driver's real
/// handler never ran.
///
/// Shaped after the real case: I Wanna Spook Jam's Smartkin save blockers. objBullet(12) authors
/// Collision(objSaveBlocker=25) to delete itself; oSmartkin_SaveBlocker(920), a child of 25, authors
/// Collision(objBullet=12) to fade itself in. 920 is in objBullet's merged target list purely
/// because the blocker authored that pairing, so the walk picked 920, objBullet had no handler for
/// it, and the bullet flew straight through -- while the blocker still lit up from its own side,
/// which is exactly what it looked like in game.
/// </summary>
[TestClass]
public class CollisionHandlerPickTests
{
    private const int BulletId = 12;      // objBullet
    private const int BlockerId = 25;     // objSaveBlocker
    private const int SubBlockerId = 920; // oSmartkin_SaveBlocker, child of objSaveBlocker
    private const int SpriteId = 9800;

    private readonly List<string> _fired = new();

    private ObjectDefinition _bullet = null!;
    private ObjectDefinition _blocker = null!;
    private ObjectDefinition _subBlocker = null!;

    [TestInitialize]
    public void SetUp()
    {
        _fired.Clear();
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        DrawManager.ResetCollisionCaches();
        VMExecutor.EnvStack.Clear();

        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprHandlerPickBox",
            Width = 32,
            Height = 32,
            MarginLeft = 0,
            MarginRight = 31,
            MarginTop = 0,
            MarginBottom = 31,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
            Textures = [new SpritePageItem()]
        };

        _bullet = Define(BulletId, "objBullet");
        _blocker = Define(BlockerId, "objSaveBlocker");
        _subBlocker = Define(SubBlockerId, "oSmartkin_SaveBlocker", parent: _blocker);

        // gml_Object_objBullet_Collision_25: instance_destroy().
        _bullet.CollisionScript[BlockerId] = MarkerCode("bullet-dies");
        // gml_Object_oSmartkin_SaveBlocker_Collision_12: activated = 1 (it fades in).
        _subBlocker.CollisionScript[BulletId] = MarkerCode("blocker-lights-up");

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
        SpriteManager._spriteDict.Remove(SpriteId);
        DrawManager.ResetCollisionCaches();
        VMExecutor.EnvStack.Clear();
    }

    private static ObjectDefinition Define(int assetId, string name, ObjectDefinition? parent = null)
    {
        var def = new ObjectDefinition { AssetId = assetId, Name = name, parent = parent, sprite = SpriteId };
        InstanceManager.ObjectDefinitions.Add(assetId, def);
        return def;
    }

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

    private GamemakerObject Spawn(ObjectDefinition def, double x, double y)
    {
        var instance = new GamemakerObject(def, x, y, def.depth, InstanceManager.NextInstanceID++, SpriteId, true, false, -1);
        instance.xprevious = x;
        instance.yprevious = y;
        return instance;
    }

    [TestMethod]
    public void BothSidesFireWhenEachAuthoredAgainstTheOther()
    {
        var blocker = Spawn(_subBlocker, 100, 100);
        var bullet = Spawn(_bullet, 110, 100);

        DrawManager.UpdateCollisions([blocker, bullet]);

        CollectionAssert.AreEquivalent(new[] { "bullet-dies", "blocker-lights-up" }, _fired,
            "the bullet's handler for the parent type has to fire, not just the child's own handler");
    }

    [TestMethod]
    public void TheParentsHandlerFiresAgainstAPlainInstanceOfTheParent()
    {
        // Control: no subclass involved, so nothing can shadow the bullet's handler.
        var blocker = Spawn(_blocker, 100, 100);
        var bullet = Spawn(_bullet, 110, 100);

        DrawManager.UpdateCollisions([blocker, bullet]);

        CollectionAssert.AreEqual(new[] { "bullet-dies" }, _fired);
    }

    [TestMethod]
    public void AMoreSpecificHandlerShadowsTheAncestorsOne()
    {
        // The other half of the rule, and the one that must NOT regress: a driver with handlers for
        // both a type and its parent gets only the more specific one. Firing both is what used to
        // run objBlock's move_contact_solid on a non-solid objPlatform pair in YoYoYo_Engine and
        // wedge instances inside walls.
        _bullet.CollisionScript[SubBlockerId] = MarkerCode("bullet-hits-subblocker");
        DrawManager.ResetCollisionCaches();

        var blocker = Spawn(_subBlocker, 100, 100);
        var bullet = Spawn(_bullet, 110, 100);

        DrawManager.UpdateCollisions([blocker, bullet]);

        CollectionAssert.AreEquivalent(new[] { "bullet-hits-subblocker", "blocker-lights-up" }, _fired,
            "the specific handler wins outright -- the parent's must not fire as well");
    }

    [TestMethod]
    public void APairIsStillOnlyHandledOnce()
    {
        // Both 25 and 920 are targets of the bullet, and both match this one instance. It still gets
        // exactly one turn.
        _bullet.CollisionScript[SubBlockerId] = MarkerCode("bullet-hits-subblocker");
        DrawManager.ResetCollisionCaches();

        var blocker = Spawn(_subBlocker, 100, 100);
        var bullet = Spawn(_bullet, 110, 100);

        DrawManager.UpdateCollisions([blocker, bullet]);

        Assert.AreEqual(1, _fired.Count(f => f.StartsWith("bullet-")), "one event per pair");
    }
}
