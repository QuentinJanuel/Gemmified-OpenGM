using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// Sprite animation, pinned to a capture from **GameMaker Studio 1.4.9999 on Windows** running
/// a probe project. These aren't reasoned expectations -- the numbers below are the ones
/// real GM printed.
///
/// Two things it settled, both of which OpenGM had wrong:
///
/// 1. image_index accumulates in SINGLE precision. At image_speed 0.1 real GM reaches 3.99999833
///    after 40 steps, which is float32's answer for 0.1 added 40 times to all 8 digits it printed;
///    double gives exactly 4.0. That one bit decides everything else, because 3.99999833 is *below*
///    the frame count, so the animation never wraps and the sprite simply stays on its last frame.
///    Accumulated in double it hit 4.000000000000002, wrapped, and snapped back to frame 0.
/// 2. image_index is never readable at image_number. Advance and wrap are one operation, and the
///    animation-end event fires once per lap with image_index *already* wrapped.
/// </summary>
[TestClass]
public class AnimationWrapTests
{
    private const int SpriteId = 8800;
    private const int ObjId = 8801;

    private ObjectDefinition _def = null!;

    [TestInitialize]
    public void SetUp()
    {
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;

        _def = new ObjectDefinition { AssetId = ObjId, Name = "objA", sprite = SpriteId };
        InstanceManager.ObjectDefinitions[ObjId] = _def;
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

    /// <summary>sprTest from the capture: 4 subimages, GMS1.4-shaped playback.</summary>
    private static void DefineSprite(int frames = 4)
    {
        SpriteManager._spriteDict[SpriteId] = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprTest",
            Width = 64,
            Height = 64,
            PlaybackSpeed = 1f,
            PlaybackSpeedType = AnimSpeedType.FramesPerGameFrame,
            Textures = Enumerable.Range(0, frames).Select(_ => new SpritePageItem()).ToList()
        };
    }

    private GamemakerObject Spawn(double imageSpeed)
    {
        var instance = new GamemakerObject(_def, 0, 0, 0, InstanceManager.NextInstanceID++, SpriteId, true, false, -1);
        instance.image_index = 0;
        instance.image_speed = imageSpeed;
        return instance;
    }

    /// <summary>
    /// What each step's events would read. Animate runs at the top of the step, so the value an
    /// event sees is the one that call left behind -- the same relationship the capture shows
    /// between its "step=" lines and the advance that preceded them.
    /// </summary>
    private static List<double> Observe(GamemakerObject instance, int steps)
    {
        var seen = new List<double>();

        for (var i = 0; i < steps; i++)
        {
            instance.Animate();
            seen.Add(instance.image_index);
        }

        return seen;
    }

    // ---- objA: image_speed 1, captured verbatim ----

    [TestMethod]
    public void WholeSpeedMatchesTheWindowsCapture()
    {
        // A step=1..12 image_index=1,2,3,0,1,2,3,0,1,2,3,0
        DefineSprite();
        var a = Spawn(1);

        var seen = Observe(a, 12).Select(x => (int)Math.Round(x)).ToList();

        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0 },
            seen,
            "GM never presents image_index at image_number -- it goes straight from 3 to 0");
    }

    [TestMethod]
    public void TheAnimationEndEventFiresOncePerLapWithTheIndexAlreadyWrapped()
    {
        // A ANIM_END t=3 image_index=0.00000000  (and again at t=7, t=11)
        DefineSprite();

        var indicesAtFire = new List<double>();
        var code = OpenGM.Loading.GameConverter.ConvertAssembly(null, """
            :[0]
            push.v self.id
            popz.v
            """);
        code.Name = "gml_Object_objA_Other_7";
        _def.OtherScript[EventSubtypeOther.AnimationEnd] = code;

        var a = Spawn(1);
        code.OnCodeExecuted += () => indicesAtFire.Add(a.image_index);

        Observe(a, 12);

        Assert.AreEqual(3, indicesAtFire.Count, "once per lap over three laps");

        foreach (var index in indicesAtFire)
        {
            Assert.AreEqual(0, index, 1e-9,
                "the capture shows image_index already back at 0 when the event runs");
        }
    }

    // ---- objB: image_speed 0.1, the case the Polar book depends on ----

    [TestMethod]
    public void FractionalSpeedAccumulatesInSinglePrecision()
    {
        // B STOPPED t=40 image_index=3.99999833  -- float32, not double (which gives exactly 4.0).
        DefineSprite();
        var b = Spawn(0.1);

        var seen = Observe(b, 40);

        Assert.AreEqual("3.99999833", seen[^1].ToString("F8", System.Globalization.CultureInfo.InvariantCulture),
            "matches GM's own 8 printed digits");
        Assert.IsTrue(seen[^1] < 4, "and crucially it stays BELOW the frame count, so nothing wraps");
    }

    [TestMethod]
    public void TheFramePastTheLastOneStillComparesEqual()
    {
        // Why `if (image_index == 4)` works in GML on a 4-frame sprite even though image_index never
        // reaches 4: GM compares reals with a tolerance, and so does OpenGM.
        DefineSprite();
        var b = Spawn(0.1);

        var seen = Observe(b, 40);

        Assert.IsTrue(CustomMath.ApproxEqual(seen[^1], 4),
            "3.99999833 == 4 under GML's fuzzy real comparison");
    }

    [TestMethod]
    public void AStoppedAnimationHoldsExactlyWhereItStopped()
    {
        // B held t=40..90 image_index=3.99999833, unchanged for all 50 steps.
        DefineSprite();
        var b = Spawn(0.1);

        Observe(b, 40);
        var atStop = b.image_index;
        b.image_speed = 0;

        foreach (var value in Observe(b, 50))
        {
            Assert.AreEqual(atStop, value, 0, "nothing moves it once image_speed is 0");
        }
    }

    [TestMethod]
    public void AStoppedAnimationDrawsItsLastFrame()
    {
        // The capture's on-screen check: objB showed the digit "3" for the whole time it sat stopped.
        // Falls straight out of flooring 3.99999833 -- no special case needed.
        DefineSprite();
        var b = Spawn(0.1);

        Observe(b, 40);
        b.image_speed = 0;
        Observe(b, 50);

        Assert.AreEqual(3, DrawnFrame(b), "shows subimage 3, matching the digit on screen on Windows");
    }

    [TestMethod]
    public void AStoppedAnimationDoesntKeepEndingOverAndOver()
    {
        // objB never printed a single ANIM_END: it stops below the frame count, so the wrap that
        // raises the event never happens.
        DefineSprite();

        var fired = 0;
        var code = OpenGM.Loading.GameConverter.ConvertAssembly(null, """
            :[0]
            push.v self.id
            popz.v
            """);
        code.Name = "gml_Object_objA_Other_7";
        code.OnCodeExecuted += () => fired++;
        _def.OtherScript[EventSubtypeOther.AnimationEnd] = code;

        var b = Spawn(0.1);
        Observe(b, 40);
        b.image_speed = 0;
        Observe(b, 50);

        Assert.AreEqual(0, fired, "no lap ever completed, so the animation never ended");
    }

    // ---- general ----

    [TestMethod]
    public void ANegativeImageSpeedWrapsThroughTheOtherEnd()
    {
        DefineSprite();
        var a = Spawn(-1);

        var seen = Observe(a, 6).Select(x => (int)Math.Round(x)).ToList();

        CollectionAssert.AreEqual(new[] { 3, 2, 1, 0, 3, 2 }, seen,
            "-1 from 0 goes to -1, which wraps up to 3 in the same operation");
    }

    /// <summary>
    /// Wrapping backwards fires animation end too. No Windows capture settled this one -- a shipped
    /// game did. I wanna CoinStack 1000's hub doors (objLevel) are driven entirely by this event:
    ///
    ///     Collision with player + UP  ->  image_speed = 0.2          (door swings open)
    ///     Other_7 (animation end), image_speed &gt; 0  ->  image_index = image_number - 1,
    ///                                                   image_speed = -0.2, spawn the transition,
    ///                                                   destroy the player
    ///     Other_7 again, on the REVERSE lap        ->  image_speed = 0, alarm[0] = 25
    ///     Alarm 0                                  ->  room_goto(roomTo)
    ///
    /// That else-branch is the only place the door's room_goto is ever reached. Without the event on
    /// the backwards wrap it is unreachable, and the door opened, the circle transition played, and
    /// the game sat in the hub forever with the sprite cycling backwards -- which is exactly what
    /// was reported. A branch that would have to be dead code in a game that works on the real
    /// runner is the proof: GM fires it in both directions.
    /// </summary>
    [TestMethod]
    public void WrappingBackwardsFiresTheAnimationEndEventToo()
    {
        DefineSprite();

        var fired = 0;
        var indicesAtFire = new List<double>();
        var code = OpenGM.Loading.GameConverter.ConvertAssembly(null, """
            :[0]
            push.v self.id
            popz.v
            """);
        code.Name = "gml_Object_objA_Other_7";
        _def.OtherScript[EventSubtypeOther.AnimationEnd] = code;

        var a = Spawn(-1);
        code.OnCodeExecuted += () =>
        {
            fired++;
            indicesAtFire.Add(a.image_index);
        };

        // Starting at 0, the very FIRST step goes to -1 and wraps (as
        // ANegativeImageSpeedWrapsThroughTheOtherEnd shows: its first observed value is already 3).
        // So 10 steps read 3,2,1,0,3,2,1,0,3,2 and wrap on steps 1, 5 and 9.
        Observe(a, 10);

        Assert.AreEqual(3, fired, "once per backwards lap");
        foreach (var index in indicesAtFire)
        {
            Assert.AreEqual(3, index, 1e-9,
                "already wrapped when the event runs, the same as the forward branch");
        }
    }

    /// <summary>
    /// The door sequence itself, in miniature: open forwards, flip to reverse in the event, and
    /// confirm the reverse lap comes back round. This is the part that was unreachable.
    /// </summary>
    [TestMethod]
    public void ASpriteFlippedToReverseInsideTheEventStillReachesTheNextAnimationEnd()
    {
        DefineSprite();

        var code = OpenGM.Loading.GameConverter.ConvertAssembly(null, """
            :[0]
            push.v self.id
            popz.v
            """);
        code.Name = "gml_Object_objA_Other_7";
        _def.OtherScript[EventSubtypeOther.AnimationEnd] = code;

        var door = Spawn(0.2);
        var laps = 0;
        code.OnCodeExecuted += () =>
        {
            laps++;
            if (door.image_speed > 0)
            {
                // objLevel's forward branch: snap to the last frame and play back out.
                door.image_index = SpriteManager.GetNumberOfFrames(door.sprite_index) - 1;
                door.image_speed = -0.2;
            }
            else
            {
                // objLevel's else branch -- the one that sets the alarm that calls room_goto.
                door.image_speed = 0;
                door.image_index = 0;
            }
        };

        // 20 steps forward to open, then 20 back; 60 is comfortably enough for both laps.
        Observe(door, 60);

        Assert.AreEqual(2, laps, "the reverse lap must end too, or the door never calls room_goto");
        Assert.AreEqual(0, door.image_speed, 1e-9, "and the door comes to rest closed");
    }

    [TestMethod]
    public void EveryValueStaysInsideTheFrameRange()
    {
        DefineSprite();
        var a = Spawn(0.1);

        foreach (var value in Observe(a, 200))
        {
            Assert.IsTrue(value >= 0 && value < 4, $"image_index left its range: {value}");
        }
    }

    /// <summary>The subimage DrawSelf would actually put on screen for a given image_index.</summary>
    private static int DrawnFrame(GamemakerObject instance)
        => (int)typeof(SpriteManager)
            .GetMethod("GetIndexFromImageIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [instance.image_index, SpriteManager.GetNumberOfFrames(instance.sprite_index)])!;
}
