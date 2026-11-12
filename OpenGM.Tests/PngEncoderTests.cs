using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;
using StbImageSharp;

namespace OpenGM.Tests;

/// <summary>
/// The PNG writer behind surface_save. It exists because nothing else in the tree can encode one
/// under wasm, and its output has to be readable by StbImageSharp -- which is what sprite_add uses
/// to load the file straight back, so a round trip through both is the test that matters.
/// </summary>
[TestClass]
public class PngEncoderTests
{
    private static byte[] Gradient(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = ((y * w) + x) * 4;
                pixels[i] = (byte)(x * 7);
                pixels[i + 1] = (byte)(y * 11);
                pixels[i + 2] = (byte)((x + y) * 3);
                pixels[i + 3] = (byte)(255 - (x % 256));
            }
        }
        return pixels;
    }

    [TestMethod]
    public void APngRoundTripsThroughTheDecoderSpriteAddUses()
    {
        const int w = 37, h = 23; // deliberately not multiples of anything
        var original = Gradient(w, h);

        var decoded = ImageResult.FromMemory(PngEncoder.Encode(original, w, h), ColorComponents.RedGreenBlueAlpha);

        Assert.AreEqual(w, decoded.Width);
        Assert.AreEqual(h, decoded.Height);
        CollectionAssert.AreEqual(original, decoded.Data, "pixels survived the round trip");
    }

    [TestMethod]
    public void AlphaIsPreserved()
    {
        // Colour type 6, not 2: a save preview drawn from a surface carries real alpha, and losing
        // it would show as a black box where the transparent parts were.
        byte[] pixels = [255, 0, 0, 0, 0, 255, 0, 128, 0, 0, 255, 255, 9, 9, 9, 1];

        var decoded = ImageResult.FromMemory(PngEncoder.Encode(pixels, 2, 2), ColorComponents.RedGreenBlueAlpha);

        CollectionAssert.AreEqual(pixels, decoded.Data);
    }

    [TestMethod]
    public void TheHeaderIsARealPngSignature()
    {
        var png = PngEncoder.Encode(new byte[4], 1, 1);

        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        // ...and it ends with IEND plus its CRC.
        Assert.AreEqual("IEND", System.Text.Encoding.ASCII.GetString(png[^8..^4]));
    }

    [TestMethod]
    public void TooLittleDataIsRejectedRatherThanTruncated()
    {
        Assert.ThrowsException<ArgumentException>(() => PngEncoder.Encode(new byte[10], 4, 4));
    }
}
