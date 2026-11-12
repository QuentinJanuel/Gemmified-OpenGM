using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// Which instances take part in a frame's draw, pinned to GameMaker Studio 1.4.9999
/// (a probe project). The capture spawns objD mid-Step and logs from its Draw event:
///
///     --- spawning objD ---
///     D CREATED image_index=0.00000000
///     D draw t=0 image_index=0.00000000 frame=0     &lt;- drawn on its creation frame, Step not yet run
///     ...
///     D draw t=7 image_index=3.50000000 frame=3
///     D ANIM_END t=7 image_index=0.00000000         &lt;- destroys itself; no draw follows
///
/// Two rules, and OpenGM had both wrong in opposite directions because it drew a snapshot of the
/// list taken *before* the step: a new instance was held back a frame, and a destroyed one got an
/// extra one.
///
/// An instance that destroyed itself must not get one more frame on screen.
///
/// DrawManager.FixedUpdate snapshots the draw list *before* DoAStep, so anything destroyed during
/// the step is still in that snapshot -- RemoveMarked only unlinks it for the next frame. The draw
/// filter checked _createRan/visible/Active but not Marked/Destroyed, so the instance drew once more
/// with whatever image_index it had ended on.
///
/// Invisible until sprite animation started wrapping immediately: the stray draw used to land on a
/// finished animation resting at image_number, which a (since-removed) special case rendered as the
/// last frame, so it looked continuous. With the wrap applied straight away it became frame 0 --
/// I Wanna Spook Jam's jump smoke is an objFx that destroys itself in its animation-end event, and
/// it flashed the big fresh puff for one frame just as the smoke finished dissipating.
/// </summary>
[TestClass]
public class DestroyedNotDrawnTests
{
    private const int SpriteId = 8950;
    private const int ObjId = 8951;

    private ObjectDefinition _def = null!;
    private readonly List<string> _drew = new();

    [TestInitialize]
    public void SetUp()
    {
        ScriptResolver.InitGMLFunctions();
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;
        DrawManager._drawObjects.Clear();
        _drew.Clear();

        _roomLoadedBefore = RoomManager.RoomLoaded;
        RoomManager.RoomLoaded = true;

        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprGhostJumpAnim1",
            Width = 16,
            Height = 16,
            PlaybackSpeed = 1f,
            PlaybackSpeedType = AnimSpeedType.FramesPerGameFrame,
            Textures = [new SpritePageItem(), new SpritePageItem(), new SpritePageItem(), new SpritePageItem()]
        };

        _def = new ObjectDefinition { AssetId = ObjId, Name = "objFx", sprite = SpriteId };

        var drawCode = OpenGM.Loading.GameConverter.ConvertAssembly(null, """
            :[0]
            push.v self.id
            popz.v
            """);
        drawCode.Name = "gml_Object_objFx_Draw_0";
        drawCode.OnCodeExecuted += () => _drew.Add("draw");
        _def.DrawScript[EventSubtypeDraw.Draw] = drawCode;

        InstanceManager.ObjectDefinitions[ObjId] = _def;
        InstanceManager.InitObjectMap();
    }

    private bool _roomLoadedBefore;

    [TestCleanup]
    public void TearDown()
    {
        foreach (var instance in InstanceManager.instances.Values.ToList())
        {
            DrawManager.Unregister(instance);
        }

        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        DrawManager._drawObjects.Clear();
        SpriteManager._spriteDict.Remove(SpriteId);
        RoomManager.RoomLoaded = _roomLoadedBefore;
    }

    private GamemakerObject Spawn()
    {
        var instance = new GamemakerObject(_def, 0, 0, 0, InstanceManager.NextInstanceID++, SpriteId, true, false, -1);
        instance._createRan = true;
        return instance;
    }

    /// <summary>The predicate RunDrawScript filters this frame's draw list with.</summary>
    private static bool ShouldDraw(GamemakerObject instance) => DrawManager.ShouldDraw(instance);

    [TestMethod]
    public void AliveInstancesStillDraw()
    {
        var fx = Spawn();

        Assert.IsTrue(ShouldDraw(fx), "the control: nothing here stops an ordinary draw");
    }

    [TestMethod]
    public void AnInstanceCreatedThisStepIsDrawnImmediately()
    {
        // `D draw t=0` in the capture: created during another object's Step, on screen that same
        // frame. Spook Jam's Matterclaw connector circles depend on it -- the boss's End Step
        // recreates all five every frame and each destroys itself in its animation-end event, so a
        // one-frame delay means they are never drawn at all.
        var fx = Spawn();

        Assert.IsTrue(ShouldDraw(fx), "a brand new instance draws on the frame it was created");
    }

    [TestMethod]
    public void AnInstanceDestroyedThisStepIsNotDrawnAgain()
    {
        var fx = Spawn();

        // What instance_destroy does: flags the instance, leaving RemoveMarked to unlink it after
        // the step -- which is after this frame's draw.
        fx.Marked = true;

        Assert.IsFalse(ShouldDraw(fx), "GameMaker drops an instance the moment instance_destroy runs");
    }

    [TestMethod]
    public void AnAlreadyDestroyedInstanceIsNotDrawn()
    {
        var fx = Spawn();
        fx.Destroyed = true;

        Assert.IsFalse(ShouldDraw(fx));
    }

    [TestMethod]
    public void TheJumpSmokeDoesntFlashItsFirstFrameOnTheWayOut()
    {
        // objFx end to end: 4 frames, destroys itself in Other[7]. The animation-end event runs from
        // Animate, so by draw time image_index has already wrapped to 0 -- drawing it there is what
        // put the big fresh puff back on screen for a frame.
        var endCode = OpenGM.Loading.GameConverter.ConvertAssembly(null, """
            :[0]
            call.i instance_destroy(argc=0)
            popz.v
            """);
        endCode.Name = "gml_Object_objFx_Other_7";
        _def.OtherScript[EventSubtypeOther.AnimationEnd] = endCode;

        var fx = Spawn();
        fx.image_speed = 1;

        var drawnFrames = new List<int>();

        for (var step = 0; step < 4; step++)
        {
            fx.Animate();

            if (ShouldDraw(fx))
            {
                drawnFrames.Add((int)fx.image_index);
            }
        }

        Assert.IsTrue(fx.Marked || fx.Destroyed, "precondition: the animation-end event really did destroy it");
        Assert.AreEqual(0, fx.image_index, 1e-9, "and image_index really had wrapped back to 0");

        // Steps 1-3 show frames 1, 2, 3. Step 4 wraps to 0, fires the event and destroys, so it
        // contributes nothing -- rather than putting the big fresh puff back on screen.
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, drawnFrames);
    }
}
