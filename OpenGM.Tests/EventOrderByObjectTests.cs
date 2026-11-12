using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;

namespace OpenGM.Tests;

/// <summary>
/// A step's per-instance events are dispatched by object index, not by instance id -- and off the
/// live instance lists, not off a list snapshotted at the start of the frame.
///
/// The ordering half is shaped after the real case: I Wanna Be The Continuity's objCeilingExtraJump
/// (object 158) sets objPlayer.vspeed = -30 on touch, and objPlayer (object 12) clamps its own
/// vspeed to maxVspeed in its own Step. objPlayer is created at runtime by objPlayerStart, so its
/// instance id is higher than every room-placed instance's -- ordering the step by instance id ran
/// it last, clamping the -30 away before it had moved the player and shortening the jump by most of
/// a block.
///
/// The liveness half is I Wanna Spook Jam's headless-horseman phase: see
/// <see cref="DrawManager.EventDispatchOrder"/>.
/// </summary>
[TestClass]
public class EventOrderByObjectTests
{
    private const int PlayerObject = 12;
    private const int JumpPadObject = 158;

    private readonly List<GamemakerObject> _spawned = new();

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        _spawned.Clear();

        Define(PlayerObject, "objPlayer");
        Define(JumpPadObject, "objCeilingExtraJump");

        InstanceManager.InitObjectMap();
    }

    [TestCleanup]
    public void TearDown()
    {
        foreach (var instance in _spawned)
        {
            DrawManager.Unregister(instance);
        }

        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
    }

    private static void Define(int assetId, string name)
        => InstanceManager.ObjectDefinitions.Add(assetId, new ObjectDefinition { AssetId = assetId, Name = name });

    /// <summary>
    /// The constructor files the instance under its object in InstanceManager.ObjectMap, which is
    /// what <see cref="DrawManager.EventDispatchOrder"/> reads.
    /// </summary>
    private GamemakerObject Spawn(int assetId, int instanceId)
    {
        var def = InstanceManager.ObjectDefinitions[assetId];
        var instance = new GamemakerObject(def, 0, 0, def.depth, instanceId, -1, true, false, -1);
        _spawned.Add(instance);
        return instance;
    }

    [TestMethod]
    public void ALowerIndexedObjectGoesFirstEvenWithAMuchHigherInstanceId()
    {
        // Room-placed jump pads keep low ids; the player is created at runtime, past LastObjectID.
        var pad = Spawn(JumpPadObject, 103_500);
        var player = Spawn(PlayerObject, 106_363);

        CollectionAssert.AreEqual(new[] { player, pad }, DrawManager.EventDispatchOrder().ToList());
    }

    [TestMethod]
    public void InstancesOfOneObjectStayInCreationOrder()
    {
        var pad1 = Spawn(JumpPadObject, 103_500);
        var pad2 = Spawn(JumpPadObject, 103_501);
        var pad3 = Spawn(JumpPadObject, 103_502);

        CollectionAssert.AreEqual(new[] { pad1, pad2, pad3 }, DrawManager.EventDispatchOrder().ToList());
    }

    [TestMethod]
    public void EveryInstanceOfAnObjectRunsBeforeTheNextObjects()
    {
        var pad1 = Spawn(JumpPadObject, 103_500);
        var pad2 = Spawn(JumpPadObject, 103_501);
        var playerA = Spawn(PlayerObject, 106_363);
        var playerB = Spawn(PlayerObject, 106_400);

        CollectionAssert.AreEqual(
            new[] { playerA, playerB, pad1, pad2 },
            DrawManager.EventDispatchOrder().ToList());
    }

    /// <summary>
    /// An instance created part-way through a pass does not take part in that pass, even when its
    /// object index is higher than the creator's and an ascending walk would still have reached it.
    ///
    /// Measured, not assumed: a probe project has objSpwn (object 11) create one instance of object
    /// 10 and one of object 12 from its Step, and object 12 logs no Step that frame. That rules out
    /// the live per-object walk OpenGMK does, which would have given it one.
    /// </summary>
    [TestMethod]
    public void AnInstanceCreatedDuringAPassDoesNotTakePartInIt()
    {
        var player = Spawn(PlayerObject, 106_363);

        var seen = new List<GamemakerObject>();

        foreach (var instance in DrawManager.EventDispatchOrder())
        {
            seen.Add(instance);

            // objPlayer (12) is dispatched before objCeilingExtraJump (158), so an ascending walk
            // reading the live lists would pick this pad up later in this same pass.
            if (instance == player)
            {
                Spawn(JumpPadObject, 103_500);
            }
        }

        CollectionAssert.AreEqual(new[] { player }, seen);
    }

    /// <summary>
    /// ...and it takes part in the next pass, which is the half that matters: End Step is its own
    /// pass, so it snapshots after the Step pass has finished creating things. That's why the
    /// spotlights' End Step -- the only code they have, and what zeroes the alpha their Create left
    /// at 1 -- now runs on the frame they're created.
    /// </summary>
    [TestMethod]
    public void AnInstanceCreatedDuringAPassTakesPartInTheNextOne()
    {
        var player = Spawn(PlayerObject, 106_364);

        GamemakerObject? spawnedMidPass = null;

        foreach (var instance in DrawManager.EventDispatchOrder())
        {
            if (instance == player)
            {
                spawnedMidPass = Spawn(JumpPadObject, 103_501);
            }
        }

        CollectionAssert.AreEqual(
            new[] { player, spawnedMidPass! },
            DrawManager.EventDispatchOrder().ToList());
    }

    /// <summary>
    /// An instance destroyed by something earlier in the same pass doesn't get its turn, even though
    /// it was in the list when the pass snapshotted it.
    /// </summary>
    [TestMethod]
    public void AnInstanceDestroyedEarlierInThePassIsSkipped()
    {
        var player = Spawn(PlayerObject, 106_365);
        var pad = Spawn(JumpPadObject, 103_502);

        var dispatched = new List<GamemakerObject>();

        foreach (var instance in DrawManager.EventDispatchOrder())
        {
            if (instance == player)
            {
                pad.Marked = true;
            }

            // What RunStepScript/HandleAlarm do before touching the instance.
            if (instance is { Active: true, Marked: false, Destroyed: false })
            {
                dispatched.Add(instance);
            }
        }

        CollectionAssert.AreEqual(new[] { player }, dispatched);
    }

    /// <summary>
    /// The height the ordering is worth, run as the engine sequences it: events, then gravity, then
    /// the move. The jump pad sets -30 once; the player clamps whatever it sees to +-9.
    /// </summary>
    [TestMethod]
    public void ThePadsVelocitySurvivesAFullFrameOfMovement()
    {
        const double gravity = 0.4;
        const double maxVspeed = 9;

        double Jump(bool playerFirst)
        {
            var vspeed = 0d;
            var y = 0d;
            var peak = 0d;
            var fired = false;

            for (var frame = 0; frame < 200; frame++)
            {
                void PlayerStep()
                {
                    if (Math.Abs(vspeed) > maxVspeed)
                    {
                        vspeed = Math.Sign(vspeed) * maxVspeed;
                    }
                }

                void PadStep()
                {
                    if (fired)
                    {
                        return;
                    }

                    vspeed = -30;
                    fired = true;
                }

                if (playerFirst)
                {
                    PlayerStep();
                    PadStep();
                }
                else
                {
                    PadStep();
                    PlayerStep();
                }

                vspeed += gravity; // AdaptSpeed
                y += vspeed;       // UpdatePositions
                peak = Math.Min(peak, y);

                if (vspeed > 0 && y >= 0)
                {
                    break;
                }
            }

            return -peak;
        }

        var withFix = Jump(playerFirst: true);
        var withoutFix = Jump(playerFirst: false);

        Assert.AreEqual(29.6, withFix - withoutFix, 0.001, "one frame of the unclamped -30");
        Assert.IsTrue(withFix > 4 * 32 - 32 && withFix < 4 * 32, $"expected a not-quite-4-block jump, got {withFix}");
        Assert.IsTrue(withoutFix < 3.1 * 32, $"expected barely 3 blocks without the fix, got {withoutFix}");
    }
}
