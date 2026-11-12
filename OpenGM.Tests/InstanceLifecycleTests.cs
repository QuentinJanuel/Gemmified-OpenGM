using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The two halves of "which instances does a statement actually reach", both found through I Wanna
/// Spook Jam's Polaroid boss:
///
/// - assigning through an object index reaches every instance of it, subscript or no subscript;
/// - an instance is still one of those instances for the whole of its own Destroy event.
/// </summary>
[TestClass]
public class InstanceLifecycleTests
{
    private const int PumpkinObject = 782; // objPolarBossHHPumpkin
    private const int LichObject = 787;    // objPolarBossFinalLichPurple

    private ObjectDefinition _pumpkin = null!;
    private ObjectDefinition _lich = null!;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;

        _pumpkin = new ObjectDefinition { AssetId = PumpkinObject, Name = "objPolarBossHHPumpkin" };
        _lich = new ObjectDefinition { AssetId = LichObject, Name = "objPolarBossFinalLichPurple" };
        InstanceManager.ObjectDefinitions[PumpkinObject] = _pumpkin;
        InstanceManager.ObjectDefinitions[LichObject] = _lich;
        InstanceManager.InitObjectMap();

        ScriptResolver.InitGMLFunctions();
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
    }

    private GamemakerObject Spawn(ObjectDefinition definition)
    {
        var instance = new GamemakerObject(definition, 0, 0, definition.depth,
            InstanceManager.NextInstanceID++, -1, true, false, -1);
        instance._createRan = true; // the constructor already files it in InstanceManager/DrawManager
        return instance;
    }

    /// <summary>
    /// <c>objPolarBossHHPumpkin.alarm[0] = 1</c>, as the game compiles it: the value, then the owner
    /// (an object index, not an instance id), then the array subscript.
    /// </summary>
    [TestMethod]
    public void AnArraySubscriptedAssignmentThroughAnObjectIndexReachesEveryInstance()
    {
        var pumpkins = new[] { Spawn(_pumpkin), Spawn(_pumpkin), Spawn(_pumpkin) };

        // Byte for byte what the game's own gml_Object_objPolarBossHHController_Step_0 holds.
        TestUtils.ExecuteScript("objPolarBossHHPumpkin.alarm[0] = 1", $"""
            :[0]
            pushi.e 1
            pushi.e {PumpkinObject}
            pushi.e 0
            pop.v.i [array]self.alarm
            """);

        foreach (var pumpkin in pumpkins)
        {
            Assert.AreEqual(1, pumpkin.alarm[0],
                $"instance {pumpkin.instanceId} never got armed -- only one pumpkin would fire, not all eight");
        }
    }

    /// <summary>The unsubscripted form has always done this; it's here so the pair can't drift apart.</summary>
    [TestMethod]
    public void APlainAssignmentThroughAnObjectIndexReachesEveryInstance()
    {
        var pumpkins = new[] { Spawn(_pumpkin), Spawn(_pumpkin), Spawn(_pumpkin) };

        TestUtils.ExecuteScript("objPolarBossHHPumpkin.image_alpha = 1", $"""
            :[0]
            push.d 1.0
            conv.d.v
            pushi.e {PumpkinObject}
            pop.v.v [stacktop]self.image_alpha
            """);

        foreach (var pumpkin in pumpkins)
        {
            Assert.AreEqual(1.0, pumpkin.image_alpha, 0.0001);
        }
    }

    /// <summary>An instance id owner still means that one instance, subscript or not.</summary>
    [TestMethod]
    public void AnArraySubscriptedAssignmentThroughAnInstanceIdReachesOnlyThatInstance()
    {
        var first = Spawn(_pumpkin);
        var second = Spawn(_pumpkin);

        TestUtils.ExecuteScript("inst.alarm[0] = 1", $"""
            :[0]
            pushi.e 1
            pushi.i {second.instanceId}
            pushi.e 0
            pop.v.i [array]self.alarm
            """);

        Assert.AreEqual(-1, first.alarm[0]);
        Assert.AreEqual(1, second.alarm[0]);
    }

    /// <summary>
    /// The softlock. Both purple liches start at count = 2; each one's Destroy event decrements
    /// count on every instance of its own object and then checks its own copy for 0. That only ever
    /// reaches 0 if the dying instance is included in its own <c>with</c>, i.e. if it is still live
    /// while its Destroy event runs.
    /// </summary>
    [TestMethod]
    public void ADyingInstanceIsStillReachedByAWithOverItsOwnObjectDuringItsDestroyEvent()
    {
        // The game's own Destroy event, minus the instance_exists(objPlayer) guard around it and
        // the instance_create of the next phase (which is what the count == 0 check gates).
        _lich.DestroyScript = Code("gml_Object_objPolarBossFinalLichPurple_Destroy_0", $"""
            :[0]
            pushi.e {LichObject}
            pushenv [2]
            :[1]
            push.v self.count
            pushi.e 1
            sub.i.v
            pop.v.v self.count
            :[2]
            popenv [1]
            """);

        var first = Spawn(_lich);
        var second = Spawn(_lich);
        first.SelfVariables["count"] = 2;
        second.SelfVariables["count"] = 2;

        InstanceManager.MarkForDestruction(first, executeEvent: true);

        // The first death has to reach both -- the survivor AND the one dying.
        Assert.AreEqual(1, Convert.ToInt32(second.SelfVariables["count"]), "the survivor was decremented");
        Assert.AreEqual(1, Convert.ToInt32(first.SelfVariables["count"]),
            "the dying instance excluded itself from its own with");

        // Clear the first out the way the end of a frame does, then kill the second.
        first.Destroy();

        InstanceManager.MarkForDestruction(second, executeEvent: true);

        Assert.AreEqual(0, Convert.ToInt32(second.SelfVariables["count"]),
            "the last one to die never reached 0, so the boss never spawned its next phase");
    }

    [TestMethod]
    public void ADyingInstanceIsMarkedOnceItsDestroyEventHasReturned()
    {
        var seenDuringDestroy = -1;
        var markedDuringDestroy = true;

        var destroy = Code("destroy_observer", ObserverBody);
        destroy.OnCodeExecuted += () =>
        {
            seenDuringDestroy = InstanceManager.FindByAssetId(LichObject).Count;
            markedDuringDestroy = VMExecutor.Self.GMSelf.Marked;
        };
        _lich.DestroyScript = destroy;

        var lich = Spawn(_lich);

        InstanceManager.MarkForDestruction(lich, executeEvent: true);

        Assert.AreEqual(1, seenDuringDestroy, "the instance vanished from by-object lookups too early");
        Assert.IsFalse(markedDuringDestroy, "the instance was marked before its own Destroy event ran");
        Assert.IsTrue(lich.Marked, "the instance is marked once the Destroy event has returned");
        Assert.AreEqual(0, InstanceManager.FindByAssetId(LichObject).Count);
    }

    /// <summary>
    /// Marking used to double as the "destruction already started" flag; a separate flag has to keep
    /// doing that job, or an instance that destroys itself from its own Destroy event recurses.
    /// </summary>
    [TestMethod]
    public void AnInstanceDestroyingItselfFromItsOwnDestroyEventDoesNotRecurse()
    {
        var runs = 0;

        var destroy = Code("self_destroying_destroy", ObserverBody);
        destroy.OnCodeExecuted += () =>
        {
            runs++;
            InstanceManager.MarkForDestruction(VMExecutor.Self.GMSelf, executeEvent: true);
        };
        _lich.DestroyScript = destroy;

        var lich = Spawn(_lich);

        InstanceManager.MarkForDestruction(lich, executeEvent: true);

        Assert.AreEqual(1, runs);
        Assert.IsTrue(lich.Marked);
    }

    /// <summary>
    /// A script that destroys the player and creates a new one, then asks for the player, gets the
    /// new one -- as in GameMaker. YoYoYo-style engines respawn exactly this way, and a mod reading
    /// the player right after got the old one, still standing where the restart was pressed.
    /// </summary>
    [TestMethod]
    public void InstanceFindSkipsAnInstanceDestroyedThisStep()
    {
        var old = Spawn(_lich);
        InstanceManager.MarkForDestruction(old, executeEvent: false);
        var respawned = Spawn(_lich);

        Assert.AreEqual(respawned.instanceId, GameFunctions.instance_find([LichObject, 0]));
        Assert.AreEqual(GMConstants.noone, GameFunctions.instance_find([LichObject, 1]));
        Assert.AreEqual(respawned.instanceId, GameFunctions.instance_find([GMConstants.all, 0]));
    }

    /// <summary>
    /// A one-instruction no-op. An empty body never reaches OnCodeExecuted, so an observer hung off
    /// it would silently never fire and the test would pass for the wrong reason.
    /// </summary>
    private const string ObserverBody = """
        :[0]
        push.v self.id
        popz.v
        """;

    private static int _nextCodeId = 70_000;

    private static VMCode Code(string name, string asm)
    {
        var code = GameConverter.ConvertAssembly(null, asm);
        code.Name = name;
        GameLoader.Codes[_nextCodeId++] = code;
        return code;
    }
}
