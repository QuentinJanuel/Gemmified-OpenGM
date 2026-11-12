using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Rendering;
using StbImageSharp;
using UndertaleModLib.Util;

namespace OpenGM.Tests;

/// <summary>
/// Texture pages are registered still encoded and only decoded, one at a time, when
/// PageManager.BindTextures uploads them -- decoding every page up front and keeping them all
/// resident until the end of the load is what ran the browser build out of wasm heap (a 4096x4096
/// page is 64MB decoded, against a 2GB ceiling for the whole engine).
///
/// So what these pin down is that PageManager.AddPage learns a page's size without unpacking its
/// pixels, for both formats GameConverter writes, and that the size it reads is the one a real
/// decode produces.
/// </summary>
[TestClass]
public class TexturePageDeferredDecodeTests
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
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>A QOI page exactly as GameConverter.ExportPages writes one.</summary>
    private static byte[] QoiPage(int w, int h)
    {
        var image = new GMImage(w, h);
        Gradient(w, h).CopyTo(image.GetRawImageData());
        return image.ConvertToQoi().ToSpan().ToArray();
    }

    [TestCleanup]
    public void TearDown()
    {
        // No GL context in tests, so UnbindTextures takes its "nothing was ever uploaded" path --
        // which still has to empty both of PageManager's dictionaries.
        PageManager.UnbindTextures();
    }

    [TestMethod]
    public void APngPagesSizeIsReadFromItsHeaderWithoutDecodingIt()
    {
        const int w = 37, h = 23; // deliberately not multiples of anything
        var blob = PngEncoder.Encode(Gradient(w, h), w, h);

        PageManager.AddPage("page_png", GameConverter.PageFormatPng, blob);

        var (image, id) = PageManager.TexturePages["page_png"];
        Assert.AreEqual(w, image.Width);
        Assert.AreEqual(h, image.Height);
        Assert.IsNull(image.Data, "the page must still be encoded until BindTextures uploads it");
        Assert.AreEqual(-1, id, "nothing is on the GPU yet");
    }

    [TestMethod]
    public void AQoiPagesSizeIsReadFromItsHeaderWithoutDecodingIt()
    {
        const int w = 41, h = 19;

        PageManager.AddPage("page_qoi", GameConverter.PageFormatQoi, QoiPage(w, h));

        var (image, id) = PageManager.TexturePages["page_qoi"];
        Assert.AreEqual(w, image.Width);
        Assert.AreEqual(h, image.Height);
        Assert.IsNull(image.Data, "the page must still be encoded until BindTextures uploads it");
        Assert.AreEqual(-1, id, "nothing is on the GPU yet");
    }

    /// <summary>
    /// The header read and a real decode have to agree, or every sprite's UVs (which divide by
    /// these, see draw_sprite's page.image.Width) come out wrong while the drawn texture is fine.
    /// </summary>
    [TestMethod]
    public void TheHeaderSizeMatchesWhatDecodingThePageActuallyProduces()
    {
        const int w = 64, h = 48;

        var png = PngEncoder.Encode(Gradient(w, h), w, h);
        PageManager.AddPage("png", GameConverter.PageFormatPng, png);
        var decodedPng = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual(decodedPng.Width, PageManager.TexturePages["png"].image.Width);
        Assert.AreEqual(decodedPng.Height, PageManager.TexturePages["png"].image.Height);

        var qoi = QoiPage(w, h);
        PageManager.AddPage("qoi", GameConverter.PageFormatQoi, qoi);
        var decodedQoi = QoiConverter.GetImageFromSpan(qoi);
        Assert.AreEqual(decodedQoi.Width, PageManager.TexturePages["qoi"].image.Width);
        Assert.AreEqual(decodedQoi.Height, PageManager.TexturePages["qoi"].image.Height);
    }

    /// <summary>
    /// A load that dies part-way through the pages (which is exactly what the out-of-memory crash
    /// this all came from did) leaves entries behind, and with no GL context UnbindTextures has no
    /// textures to delete -- but the stale entries still have to go, or the retry hits a
    /// duplicate key instead of the real problem.
    /// </summary>
    [TestMethod]
    public void APageFromAnAbandonedLoadIsNotCarriedIntoTheNextOne()
    {
        PageManager.AddPage("page", GameConverter.PageFormatQoi, QoiPage(8, 8));
        PageManager.UnbindTextures();
        Assert.AreEqual(0, PageManager.TexturePages.Count);

        PageManager.AddPage("page", GameConverter.PageFormatQoi, QoiPage(16, 32));
        Assert.AreEqual(16, PageManager.TexturePages["page"].image.Width);
        Assert.AreEqual(32, PageManager.TexturePages["page"].image.Height);
    }
}
