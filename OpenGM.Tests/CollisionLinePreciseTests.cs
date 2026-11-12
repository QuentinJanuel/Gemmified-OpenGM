using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;

namespace OpenGM.Tests;

/// <summary>
/// collision_line's precise mode used to just log "Precise collision_line not implemented!" and
/// return true unconditionally the moment the bbox-level check passed -- so any precise-masked
/// object always reported a hit as soon as a line's bounding box overlapped it, regardless of
/// whether the line actually touched the sprite's real shape. That's exactly what backs
/// obj_128MatterclawHand's laser-sightline attack pattern in I Wanna Spook Jam
/// (`collision_line(x, y, ..., objPlayer, true, true)`), which needs a real per-pixel test to mean
/// anything.
///
/// These exercise CollisionManager.PreciseCollisionLine directly (ported from OpenGMK's
/// check_collision_line) against a small synthetic 8x8 mask with exactly one pixel set, since a
/// non-trivial mask is the whole point -- an all-zero or all-one mask couldn't tell a real pixel
/// walk apart from the old "always true" bug.
/// </summary>
[TestClass]
public class CollisionLinePreciseTests
{
    // 8x8, 1 byte per row (ceil(8/8)). Bit 7 (MSB) is pixel u=0, matching ColMaskSet's
    // `ourbit = 7 - (u & 7)`. Only pixel (7, 7) -- the bottom-right corner -- is set.
    private static SpriteData MakeSprite()
    {
        var mask = new byte[8];
        mask[7] = 0b0000_0001; // u=7 on row v=7

        return new SpriteData
        {
            Width = 8,
            Height = 8,
            OriginX = 0,
            OriginY = 0,
            CollisionMasks = new List<byte[]> { mask },
            Textures = new List<SpritePageItem> { new() }
        };
    }

    [TestMethod]
    public void LineThroughAnUnsetRowMisses()
    {
        var sprite = MakeSprite();

        // Top row (v=0): no pixel set anywhere along it.
        var hit = CollisionManager.PreciseCollisionLine(sprite, 0, 0, 0, 1, 1, 0, 0, 0, 7, 0);

        Assert.IsFalse(hit, "top row has no set pixels -- the old bug would have returned true here");
    }

    [TestMethod]
    public void LineThroughTheSetPixelHits()
    {
        var sprite = MakeSprite();

        // Bottom row (v=7): passes directly over the one set pixel at u=7.
        var hit = CollisionManager.PreciseCollisionLine(sprite, 0, 0, 0, 1, 1, 0, 0, 7, 7, 7);

        Assert.IsTrue(hit);
    }

    [TestMethod]
    public void DiagonalLineReachingTheSetPixelHits()
    {
        var sprite = MakeSprite();

        // dx == dy (7,7): exercises the horizontal/vertical axis-selection tie-break, not just a
        // pure horizontal or vertical walk.
        var hit = CollisionManager.PreciseCollisionLine(sprite, 0, 0, 0, 1, 1, 0, 0, 0, 7, 7);

        Assert.IsTrue(hit);
    }

    [TestMethod]
    public void DiagonalLineMissingTheSetPixelMisses()
    {
        var sprite = MakeSprite();

        // Top-right to bottom-left: never comes near (7, 7).
        var hit = CollisionManager.PreciseCollisionLine(sprite, 0, 0, 0, 1, 1, 0, 7, 0, 0, 6);

        Assert.IsFalse(hit);
    }
}
