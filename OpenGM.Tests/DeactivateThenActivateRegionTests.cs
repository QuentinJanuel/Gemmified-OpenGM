using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// The standard GameMaker off-screen culling idiom, which does both halves inside ONE event:
///
///     instance_deactivate_object(objBlock);     // or instance_deactivate_all(true)
///     instance_activate_region(view_x - 32, view_y - 32, view_w + 64, view_h + 64, true);
///
/// Real GM moves an instance between the active and inactive lists the moment you ask, so the
/// activate call sees what the deactivate call just did. OpenGM defers the move to a between-frames
/// flush of <see cref="GamemakerObject.NextActive"/> (so nothing vanishes mid-event) -- and
/// instance_activate_region used to scan only <c>inactiveInstances</c>, which the just-deactivated
/// instances had not been moved into yet. It therefore reactivated NOTHING, and after the flush the
/// whole object was deactivated for good.
///
/// I wanna CoinStack 1000 runs this out of objTransitionCamera's User Event 0 on entering any level
/// room, so every solid objBlock in the level went inactive and the player fell through the floor
/// (and, still falling, dragged the camera below the room, leaving a screen with no tiles on it).
/// The hub was fine because its camera is created with followPlayer = 1, whose branch never calls
/// the user event -- which is exactly what made the bug look room-specific rather than structural.
/// </summary>
[TestClass]
public class DeactivateThenActivateRegionTests
{
    private const int SpriteId = 9400;
    private const int BlockId = 18;

    private ObjectDefinition _block = null!;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;

        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprBlockMask",
            Width = 32,
            Height = 32,
            MarginLeft = 0,
            MarginRight = 31,
            MarginTop = 0,
            MarginBottom = 31,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
            Textures = [new SpritePageItem()]
        };

        _block = new ObjectDefinition { AssetId = BlockId, Name = "objBlock", solid = true, sprite = SpriteId };
        InstanceManager.ObjectDefinitions[BlockId] = _block;
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
    }

    private GamemakerObject Spawn(double x, double y)
        => new(_block, x, y, _block.depth, InstanceManager.NextInstanceID++, SpriteId, true, false, -1);

    /// <summary>
    /// The engine's own between-frames flush, not a stand-in for it -- the whole bug lives in the
    /// gap between "NextActive was set" and "the instance actually moved lists".
    /// </summary>
    private static void FlushPendingActivation()
        => new RoomContainer(new Room { AssetId = 9401, Name = "rTest" }).HandleObjectActivation();

    [TestMethod]
    public void DeactivateThenActivateRegionKeepsTheOnScreenBlocks()
    {
        var onScreen = Spawn(160, 512);   // inside the view
        var offScreen = Spawn(160, 2000); // far below it

        // objTransitionCamera's User Event 0, verbatim in shape: view is 0,0 800x608.
        GameFunctions.instance_deactivate_object([BlockId]);
        MiscGapFunctions.instance_activate_region([-32.0, -32.0, 864.0, 672.0, true]);

        FlushPendingActivation();

        Assert.IsTrue(onScreen.Active,
            "the on-screen block must come back -- otherwise the floor stops existing");
        Assert.IsFalse(offScreen.Active, "the off-screen block stays culled, which is the point");
    }

    /// <summary>
    /// The same thing the other way round: activate_all then deactivate_region(outside). The
    /// pending-activation instances live in inactiveInstances at that moment, so a deactivate that
    /// only scanned the active list would miss them.
    /// </summary>
    [TestMethod]
    public void ActivateAllThenDeactivateOutsideRegionAlsoCommutes()
    {
        var onScreen = Spawn(160, 512);
        var offScreen = Spawn(160, 2000);

        // Start from everything culled, as a previous frame would have left it.
        GameFunctions.instance_deactivate_object([BlockId]);
        FlushPendingActivation();
        Assert.IsFalse(onScreen.Active, "precondition: both culled");

        GameFunctions.instance_activate_all([]);
        MiscGapFunctions.instance_deactivate_region([-32.0, -32.0, 864.0, 672.0, false]);

        FlushPendingActivation();

        Assert.IsTrue(onScreen.Active, "inside the region, so it survives the deactivate");
        Assert.IsFalse(offScreen.Active, "outside the region, so it goes back to culled");
    }

    [TestMethod]
    public void ActivateRegionLeavesInstancesOutsideTheRegionAlone()
    {
        var far = Spawn(160, 2000);

        GameFunctions.instance_deactivate_object([BlockId]);
        FlushPendingActivation();

        MiscGapFunctions.instance_activate_region([-32.0, -32.0, 864.0, 672.0, true]);
        FlushPendingActivation();

        Assert.IsFalse(far.Active, "nothing should resurrect a block the region doesn't cover");
    }
}
