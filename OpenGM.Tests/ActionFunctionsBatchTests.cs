using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The GM8/GMS1.4 Drag &amp; Drop action_* table used to be mostly unregistered -- a missing
/// function aborts the whole calling event (VMExecutor.cs), so any D&amp;D-built game silently lost
/// everything after the first action_* it used. These cover the ones with real logic worth
/// pinning down (score/lives/health, conditionals, instance creation/destruction); the pure
/// 1:1 draw_* wrappers aren't retested here since the draw_* functions they delegate to already
/// have their own coverage.
/// </summary>
[TestClass]
public class ActionFunctionsBatchTests
{
    private const int SpriteId = 9100;
    private const int ObjAId = 5001;
    private const int ObjBId = 5002;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        // Real instance ids start at GMConstants.FIRST_INSTANCE_ID (100000) specifically so they
        // never collide with the small, compile-time object asset indices -- functions like
        // instance_destroy rely on that gap to tell "an instance id" from "an object index" apart.
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;
        DrawManager.ResetCollisionCaches();

        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprActionTestBox",
            Width = 32,
            Height = 32,
            MarginLeft = 0,
            MarginRight = 31,
            MarginTop = 0,
            MarginBottom = 31,
            SepMasks = UndertaleModLib.Models.UndertaleSprite.SepMaskType.AxisAlignedRect,
            Textures = [new SpritePageItem()]
        };

        InstanceManager.ObjectDefinitions[ObjAId] = new ObjectDefinition { AssetId = ObjAId, Name = "objA", sprite = SpriteId };
        InstanceManager.ObjectDefinitions[ObjBId] = new ObjectDefinition { AssetId = ObjBId, Name = "objB", sprite = SpriteId };
        InstanceManager.InitObjectMap();

        // score/lives/health are static across the whole VariableResolver, not per-test -- reset
        // them explicitly so tests don't see whatever the previous test left behind.
        VariableResolver.set_score(0, 0);
        VariableResolver.set_lives(-1, 0);
        VariableResolver.set_health(-1, 0);
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

    private static GamemakerObject Spawn(int objId, double x, double y)
    {
        var def = InstanceManager.ObjectDefinitions[objId];
        return new GamemakerObject(def, x, y, def.depth, InstanceManager.NextInstanceID++, SpriteId, true, false, -1);
    }

    [TestMethod]
    public void ScoreSetAndConditional()
    {
        ActionFunctions.action_set_score([10]);
        Assert.AreEqual(10, VariableResolver.get_score(0).Conv<int>());

        ActionFunctions.Action_Relative = true;
        try
        {
            ActionFunctions.action_set_score([5]);
        }
        finally
        {
            ActionFunctions.Action_Relative = false;
        }
        Assert.AreEqual(15, VariableResolver.get_score(0).Conv<int>());

        Assert.IsTrue(ActionFunctions.action_if_score([15, 0]).Conv<bool>(), "== comparator");
        Assert.IsTrue(ActionFunctions.action_if_score([10, 2]).Conv<bool>(), "> comparator");
        Assert.IsTrue(ActionFunctions.action_if_score([20, 1]).Conv<bool>(), "< comparator");
    }

    [TestMethod]
    public void LifeAndHealthSetAndConditional()
    {
        ActionFunctions.action_set_life([3]);
        Assert.AreEqual(3, VariableResolver.get_lives(0).Conv<int>());
        Assert.IsTrue(ActionFunctions.action_if_life([3, 0]).Conv<bool>());

        ActionFunctions.action_set_health([50]);
        Assert.AreEqual(50, VariableResolver.get_health(0).Conv<double>());
        Assert.IsTrue(ActionFunctions.action_if_health([40, 2]).Conv<bool>(), "> comparator");
        Assert.IsTrue(ActionFunctions.action_if_health([60, 1]).Conv<bool>(), "< comparator");
    }

    [TestMethod]
    public void IfNumberComparesLiveInstanceCounts()
    {
        Spawn(ObjAId, 0, 0);
        Spawn(ObjAId, 10, 10);

        Assert.IsTrue(ActionFunctions.action_if_number([ObjAId, 2, 0]).Conv<bool>(), "== 2");
        Assert.IsFalse(ActionFunctions.action_if_number([ObjAId, 3, 0]).Conv<bool>(), "== 3, only 2 exist");
        Assert.IsTrue(ActionFunctions.action_if_number([ObjAId, 1, 2]).Conv<bool>(), "> 1");
        Assert.IsTrue(ActionFunctions.action_if_number([ObjAId, 5, 1]).Conv<bool>(), "< 5");
    }

    [TestMethod]
    public void IfVariableComparesNumbersAndStrings()
    {
        Assert.IsTrue(ActionFunctions.action_if_variable([5.0, 5.0, 0]).Conv<bool>(), "5 == 5");
        Assert.IsTrue(ActionFunctions.action_if_variable([3.0, 5.0, 1]).Conv<bool>(), "3 < 5");
        Assert.IsTrue(ActionFunctions.action_if_variable([8.0, 5.0, 2]).Conv<bool>(), "8 > 5");
        Assert.IsTrue(ActionFunctions.action_if_variable(["abc", "abc", 0]).Conv<bool>(), "\"abc\" == \"abc\"");
        Assert.IsTrue(ActionFunctions.action_if_variable(["abc", "abd", 1]).Conv<bool>(), "\"abc\" < \"abd\"");
    }

    [TestMethod]
    public void KillPositionDestroysOnlyOverlappingInstances()
    {
        var hit = Spawn(ObjAId, 100, 100);
        var missed = Spawn(ObjAId, 500, 500);

        ActionFunctions.action_kill_position([110.0, 110.0]);

        // instance_destroy only marks for deferred destruction (removed from InstanceManager.instances
        // at end-of-step, not immediately) -- Marked is the observable effect right after the call.
        Assert.IsTrue(hit.Marked, "instance under the point should be marked for destruction");
        Assert.IsFalse(missed.Marked, "instance elsewhere should be untouched");
    }

    [TestMethod]
    public void CreateObjectRandomOnlyPicksValidObjectIds()
    {
        // One bogus id mixed in with two real ones -- every draw must land on a real object, and
        // must actually create an instance of it, not just avoid crashing.
        const int bogusId = 999999;

        for (var i = 0; i < 20; i++)
        {
            var beforeCount = InstanceManager.instances.Count;
            var result = ActionFunctions.action_create_object_random([ObjAId, ObjBId, bogusId, bogusId, 0.0, 0.0]);

            Assert.IsNotNull(result);
            var instanceId = result.Conv<int>();
            Assert.IsTrue(InstanceManager.instances.TryGetValue(instanceId, out var instance));
            Assert.IsTrue(instance!.Definition.AssetId is ObjAId or ObjBId);
        }
    }
}
