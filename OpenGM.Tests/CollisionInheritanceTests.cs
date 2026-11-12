using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// A Collision event authored against an object applies to every instance that IS-A that object.
/// The target side of the check always honoured that; the driving side didn't, so a pair whose
/// driver had no exact-type instances in the room was never checked at all.
///
/// Shaped after the real case: I Wanna Be The Continuity's playerTeleport authors one Collision
/// event, against objBlock. Its NANG rooms are built entirely out of Block9 (a child of objBlock)
/// with no objBlock instance anywhere, and the teleport clone flew straight through every wall;
/// the hub, which does place real objBlock instances, worked.
/// </summary>
[TestClass]
public class CollisionInheritanceTests
{
    private const int WallId = 20;      // objBlock
    private const int WallChildId = 71; // Block9, child of objBlock
    private const int BulletId = 79;    // playerTeleport, authors Collision(objBlock)
    private const int SpriteId = 9000;

    private readonly List<string> _fired = new();

    private ObjectDefinition _wall = null!;
    private ObjectDefinition _wallChild = null!;
    private ObjectDefinition _bullet = null!;

    [TestInitialize]
    public void SetUp()
    {
        _fired.Clear();
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        DrawManager.ResetCollisionCaches();

        // A plain 32x32 rectangle-masked sprite, so collisions come down to overlapping boxes.
        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprTestBox",
            Width = 32,
            Height = 32,
            MarginLeft = 0,
            MarginRight = 31,
            MarginTop = 0,
            MarginBottom = 31,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
            Textures = [new SpritePageItem()]
        };

        _wall = Define(WallId, "objBlock", solid: true);
        _wallChild = Define(WallChildId, "Block9", solid: true, parent: _wall);
        _bullet = Define(BulletId, "playerTeleport");
        _bullet.CollisionScript[WallId] = MarkerCode("bullet-hit-wall");

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
    }

    private static ObjectDefinition Define(int assetId, string name, bool solid = false, ObjectDefinition? parent = null)
    {
        var def = new ObjectDefinition
        {
            AssetId = assetId,
            Name = name,
            solid = solid,
            parent = parent,
            sprite = SpriteId
        };

        InstanceManager.ObjectDefinitions.Add(assetId, def);
        return def;
    }

    /// <summary>
    /// A do-nothing script that records the fact it ran. Needs at least one real instruction:
    /// VMExecutor.ExecuteCode returns early on an empty body, before it reports having run.
    /// </summary>
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

        // Where a standing-still instance is by the time collisions run: solid pairs get snapped
        // back to xprevious/yprevious before their event, and leaving those at the default 0,0
        // would teleport everything to the origin on the first collision of the step.
        instance.xprevious = x;
        instance.yprevious = y;

        return instance;
    }

    [TestMethod]
    public void CollisionFiresAgainstADescendantOfTheTargetObject()
    {
        // The room a player actually meets: walls are the subclass, and there is no instance of
        // the object the Collision event was authored against anywhere in it.
        var wall = Spawn(_wallChild, 100, 100);
        var bullet = Spawn(_bullet, 110, 100);

        DrawManager.UpdateCollisions([wall, bullet]);

        CollectionAssert.AreEqual(new[] { "bullet-hit-wall" }, _fired);
    }

    [TestMethod]
    public void CollisionStillFiresAgainstTheTargetObjectItself()
    {
        var wall = Spawn(_wall, 100, 100);
        var bullet = Spawn(_bullet, 110, 100);

        DrawManager.UpdateCollisions([wall, bullet]);

        CollectionAssert.AreEqual(new[] { "bullet-hit-wall" }, _fired);
    }

    [TestMethod]
    public void EachCollidingPairFiresExactlyOnce()
    {
        // Two walls of different types overlapping the same bullet: one event per pair, not per
        // definition the driving instance happens to be reachable under.
        var wall = Spawn(_wall, 100, 100);
        var childWall = Spawn(_wallChild, 108, 100);
        var bullet = Spawn(_bullet, 104, 100);

        DrawManager.UpdateCollisions([wall, childWall, bullet]);

        CollectionAssert.AreEqual(new[] { "bullet-hit-wall", "bullet-hit-wall" }, _fired);
    }

    [TestMethod]
    public void NonOverlappingInstancesDoNotCollide()
    {
        var wall = Spawn(_wallChild, 100, 100);
        var bullet = Spawn(_bullet, 400, 400);

        DrawManager.UpdateCollisions([wall, bullet]);

        CollectionAssert.AreEqual(Array.Empty<string>(), _fired);
    }

    [TestMethod]
    public void AHandlerAuthoredAgainstTheSubclassWinsOverTheParentsOne()
    {
        // With handlers for both objBlock and Block9, a Block9 instance has to run the Block9 one
        // -- and only that one -- even though the pair is driven from the objBlock side.
        _bullet.CollisionScript[WallChildId] = MarkerCode("bullet-hit-wallchild");
        DrawManager.ResetCollisionCaches();

        var wall = Spawn(_wallChild, 100, 100);
        var bullet = Spawn(_bullet, 110, 100);

        DrawManager.UpdateCollisions([wall, bullet]);

        CollectionAssert.AreEqual(new[] { "bullet-hit-wallchild" }, _fired);
    }
}
