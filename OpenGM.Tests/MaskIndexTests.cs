using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;

namespace OpenGM.Tests;

/// <summary>
/// Precise (per-pixel) collision, sampled at points that fall outside the mask.
///
/// ColMaskSet bounds-checked only the far end of the byte array it indexes, which left two holes.
/// A negative pixel co-ordinate indexed the array negatively and threw:
///
///   Execution of instruction call.i collision_line(argc=7) ... in script
///   gml_Object_obj_128MatterclawHand_Step_2 failed :
///   System.IndexOutOfRangeException: Arg_IndexOutOfRangeException
///
/// and a co-ordinate past the right-hand edge stayed silent but read the wrong pixel, because u is a
/// bit offset within a row: u == width lands on the next row's first byte, which is comfortably
/// inside the array.
///
/// Callers hand it out-of-range points as a matter of course. collision_line samples along a segment
/// that has been clipped to the bounding box and then rounded to whole pixels, and for a rotated
/// instance PreciseCollisionPoint runs each sample back through a rotation first, so landing a pixel
/// or two outside is routine rather than exceptional. obj_128MatterclawHand calls collision_line from
/// a Step event, so it threw every frame and took the rest of the event with it each time.
/// </summary>
[TestClass]
public class MaskIndexTests
{
    private const int SpriteId = 8900;

    /// <summary>A sprite with one fully-set mask per frame, so any in-range sample is a hit.</summary>
    private static SpriteData Sprite(int frames, int width = 8, int height = 8)
    {
        var sprite = new SpriteData
        {
            AssetIndex = SpriteId,
            Name = "sprPreciseTest",
            Width = width,
            Height = height,
            PlaybackSpeed = 1f,
            PlaybackSpeedType = AnimSpeedType.FramesPerGameFrame,
            SepMasks = UndertaleSprite.SepMaskType.Precise,
            Textures = Enumerable.Range(0, frames).Select(_ => new SpritePageItem()).ToList()
        };

        for (var i = 0; i < frames; i++)
        {
            sprite.CollisionMasks.Add(Enumerable.Repeat((byte)0xFF, ((width + 7) >> 3) * height).ToArray());
        }

        return sprite;
    }

    // ---- ColMaskSet bounds ----

    [TestMethod]
    public void ANegativePixelIsAMissRatherThanAThrow()
    {
        var sprite = Sprite(1);
        var mask = sprite.CollisionMasks[0];

        // Every one of these indexed the byte array negatively before the fix.
        Assert.IsFalse(CollisionManager.ColMaskSet(sprite, -1, 0, mask), "u = -1");
        Assert.IsFalse(CollisionManager.ColMaskSet(sprite, 0, -1, mask), "v = -1");
        Assert.IsFalse(CollisionManager.ColMaskSet(sprite, -9, -9, mask), "both, past a byte boundary");
    }

    [TestMethod]
    public void APixelPastTheEdgeIsAMissRatherThanTheNextRow()
    {
        // u is a bit offset within a row, so on an 8-wide sprite u = 8 resolves to byte 1 of row v --
        // which is row v+1's first byte, well inside the array and so silently wrong rather than
        // caught. Height-2 so there genuinely is a next row to be misread.
        var sprite = Sprite(1, width: 8, height: 2);
        var mask = sprite.CollisionMasks[0];

        Assert.IsFalse(CollisionManager.ColMaskSet(sprite, 8, 0, mask), "one past the right-hand edge");
        Assert.IsFalse(CollisionManager.ColMaskSet(sprite, 0, 2, mask), "one past the bottom edge");
        Assert.IsTrue(CollisionManager.ColMaskSet(sprite, 7, 1, mask), "the real bottom-right pixel still hits");
    }

    [TestMethod]
    public void CollisionLineSurvivesSamplingOutsideTheSprite()
    {
        // The reported crash, through the function in the stack trace: a line running well past both
        // ends of the sprite, so the sampled points start and end outside it.
        var sprite = Sprite(4);

        var hit = CollisionManager.PreciseCollisionLine(
            sprite, 0,
            0, 0, 1, 1, 0,
            -40, 4, 40, 4);

        Assert.IsTrue(hit, "it still crosses the sprite, so it still collides");
    }

    [TestMethod]
    public void CollisionLineSurvivesARotatedInstance()
    {
        // obj_128MatterclawHand rotates, and the rotated branch of PreciseCollisionPoint is what
        // pushes samples furthest outside the mask.
        var sprite = Sprite(4);

        var hit = CollisionManager.PreciseCollisionLine(
            sprite, 0,
            0, 0, 1, 1, 35,
            -40, 4, 40, 4);

        Assert.IsTrue(hit);
    }

    // ---- mask index normalisation ----

    [TestMethod]
    public void RestingOnePastTheEndUsesTheLastMask()
    {
        var sprite = Sprite(4);

        // 4 is what image_index reads as while a 4-frame animation rests at its end -- Animate defers
        // the wrap a step. The drawn frame there is the last one, so the mask has to match it.
        Assert.AreEqual(3, CollisionManager.MaskIndexFor(sprite, 4));
    }

    [TestMethod]
    public void OutOfRangeIndicesWrapInsteadOfThrowing()
    {
        var sprite = Sprite(4);

        Assert.AreEqual(0, CollisionManager.MaskIndexFor(sprite, 0));
        Assert.AreEqual(3, CollisionManager.MaskIndexFor(sprite, 3));
        Assert.AreEqual(1, CollisionManager.MaskIndexFor(sprite, 5));
        Assert.AreEqual(3, CollisionManager.MaskIndexFor(sprite, -1), "negative image_speed wraps up");
        Assert.AreEqual(2, CollisionManager.MaskIndexFor(sprite, -2));
    }

    [TestMethod]
    public void ASpriteWithOneSharedMaskAlwaysUsesIt()
    {
        // Masks and frames aren't necessarily 1:1 -- one mask can cover every frame.
        var sprite = Sprite(1);

        foreach (var index in new[] { 0, 1, 4, 17, -1 })
        {
            Assert.AreEqual(0, CollisionManager.MaskIndexFor(sprite, index), $"index {index}");
        }
    }

    [TestMethod]
    public void ASpriteWithNoMasksDoesntThrow()
    {
        var sprite = Sprite(0);

        Assert.AreEqual(0, CollisionManager.MaskIndexFor(sprite, 4));
        Assert.IsFalse(CollisionManager.PreciseCollisionPoint(sprite, 0, 0, 0, 1, 1, 0, 0, 0),
            "no masks at all is a miss, not a lookup into an empty list");
    }
}
