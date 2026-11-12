using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// A room's instances carry ids baked into the room file, and get them again on every (re)load. A
/// PERSISTENT instance placed in a room and then carried back into it therefore collides with its
/// own baked id -- so real GameMaker skips creating a room instance whose id is already live
/// ("Load all instances in new room, unless they already exist due to persistence", OpenGMK's
/// load_room).
///
/// Without that, InstanceManager.AddInstance refused the duplicate registration and logged
/// "Tried to register another object with instanceId:N" -- but the instance had already been
/// constructed and added to DrawManager's list, so it was left running its Step event out of the
/// draw list forever, invisible to instances/ObjectMap and so to RemoveMarked. Spook Jam's
/// oZebMistController (persistent, placed in 8 Zeb rooms, id 125108 in roomZeb1) is exactly this:
/// walk back into roomZeb1 and its mist background keeps scrolling in rooms that shouldn't have it.
/// </summary>
[TestClass]
public class PersistentInstanceRoomReentryTests
{
    private const int RoomA = 0;
    private const int RoomB = 1;
    private const int PersistentObj = 0;
    private const int PlainObj = 1;
    private const int BakedIdA = GMConstants.FIRST_INSTANCE_ID;
    private const int BakedIdB = GMConstants.FIRST_INSTANCE_ID + 1;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID + 100;
        DrawManager._drawObjects.Clear();
        DrawManager.ResetCollisionCaches();
        RoomManager.RoomList.Clear();
        RoomManager.PersistentRooms.Clear();
        RoomManager.CurrentRoom = null!;

        InstanceManager.ObjectDefinitions.Add(PersistentObj, new ObjectDefinition
        {
            AssetId = PersistentObj,
            Name = "oZebMistController",
            persistent = true
        });

        InstanceManager.ObjectDefinitions.Add(PlainObj, new ObjectDefinition
        {
            AssetId = PlainObj,
            Name = "objPlain"
        });

        InstanceManager.InitObjectMap();

        // Room A places the persistent instance; room B places an ordinary one, so changing rooms
        // is a normal room change rather than a special empty case.
        RoomManager.RoomList.Add(RoomA, Room(RoomA, "roomZeb1", Instance(BakedIdA, PersistentObj)));
        RoomManager.RoomList.Add(RoomB, Room(RoomB, "roomZeb2", Instance(BakedIdB, PlainObj)));
    }

    [TestCleanup]
    public void TearDown()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        DrawManager._drawObjects.Clear();
        DrawManager.ResetCollisionCaches();
        RoomManager.RoomList.Clear();
        RoomManager.PersistentRooms.Clear();
        RoomManager.CurrentRoom = null!;
    }

    private static GameObject Instance(int instanceId, int definitionId) => new()
    {
        Type = ElementType.Instance,
        Id = 0,
        InstanceID = instanceId,
        DefinitionID = definitionId,
        PreCreateCodeID = -1,
        CreationCodeID = -1
    };

    private static Room Room(int assetId, string name, params GameObject[] instances) => new()
    {
        AssetId = assetId,
        Name = name,
        GameObjects = instances.ToList(),
        CreationCodeId = -1, // 0 would be looked up as a real code entry
        // Room.Views is an 8-slot array of nulls by default; OnRoomChanged reads every slot.
        Views = Enumerable.Range(0, 8).Select(_ => new View()).ToArray()
    };

    private static void GoTo(int room, bool first = false)
    {
        RoomManager.FirstRoom = first;
        RoomManager.New_Room = room;
        RoomManager.ChangeToWaitingRoom();
    }

    private static int CountOf(int objectId)
        => InstanceManager.instances.Values.Count(x => x.Definition.AssetId == objectId);

    /// <summary>
    /// Instances of this object in DrawManager's list -- which is what actually gets stepped and
    /// drawn each frame, and where a rejected duplicate ends up. AddInstance refuses a colliding id
    /// so `instances` never shows the extra copy; DrawManager.Register has no such check, so the
    /// half-registered instance lands here and, being invisible to `instances`, is never cleaned up
    /// by RemoveMarked or a room change either. This is the count that shows the bug.
    /// </summary>
    private static int DrawnCountOf(int objectId)
        => DrawManager._drawObjects.OfType<GamemakerObject>().Count(x => x.Definition.AssetId == objectId);

    [TestMethod]
    public void APersistentInstanceComingBackToItsOwnRoomIsNotDuplicated()
    {
        GoTo(RoomA, first: true);
        Assert.AreEqual(1, CountOf(PersistentObj), "sanity check: room A created its controller");

        GoTo(RoomB);
        Assert.AreEqual(1, CountOf(PersistentObj), "persistent, so it comes along");

        GoTo(RoomA);

        Assert.AreEqual(1, DrawnCountOf(PersistentObj), "room A must not create a second copy of it");
        Assert.AreEqual(1, CountOf(PersistentObj));
        Assert.IsTrue(InstanceManager.instances.ContainsKey(BakedIdA), "and the surviving one keeps its id");
    }

    [TestMethod]
    public void TheSurvivingInstanceStaysFullyRegistered()
    {
        GoTo(RoomA, first: true);
        var original = InstanceManager.instances[BakedIdA];

        GoTo(RoomB);
        GoTo(RoomA);

        Assert.AreSame(original, InstanceManager.instances[BakedIdA], "same instance throughout");
        CollectionAssert.Contains(InstanceManager.ObjectMap[PersistentObj].Instances, original,
            "still filed under its object");

        // Every stepped/drawn instance of this object must be the one `instances` knows about --
        // a rejected duplicate is neither in `instances` nor removable, so it would step forever.
        foreach (var drawn in DrawManager._drawObjects.OfType<GamemakerObject>()
                     .Where(x => x.Definition.AssetId == PersistentObj))
        {
            Assert.AreSame(original, drawn, "a duplicate was left stepping out of the draw list");
        }
    }

    [TestMethod]
    public void ANonPersistentRoomInstanceIsRecreatedNormally()
    {
        // The control: nothing survives the room change, so the baked id is free and room B's own
        // instance is built again from scratch every time.
        GoTo(RoomA, first: true);
        GoTo(RoomB);
        var first = InstanceManager.instances[BakedIdB];

        GoTo(RoomA);
        Assert.IsFalse(InstanceManager.instances.ContainsKey(BakedIdB), "it isn't persistent");

        GoTo(RoomB);
        Assert.IsTrue(InstanceManager.instances.ContainsKey(BakedIdB));
        Assert.AreNotSame(first, InstanceManager.instances[BakedIdB], "a fresh instance, reusing the baked id");
    }
}
