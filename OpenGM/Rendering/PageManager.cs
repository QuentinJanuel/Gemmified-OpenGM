using OpenGM.Loading;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using UndertaleModLib.Util;

namespace OpenGM.Rendering;

public static class PageManager
{
    // we probably shouldnt be storing image result here, we literally only use it for width/height and you can get that from opengl
    public static Dictionary<string, (ImageResult image, int id)> TexturePages = new();

    /// <summary>
    /// The still-encoded bytes of every game texture page that hasn't reached the GPU yet, from
    /// <see cref="AddPage"/> (during the load) until <see cref="BindTextures"/> uploads it.
    ///
    /// The pages are deliberately *not* decoded as they are read. A page decodes to width * height
    /// * 4 bytes flat -- a 4096x4096 one is 64MB -- and the load used to decode every page up
    /// front and hold them all until BindTextures ran at the very end of Entry.LoadGame, so a game
    /// with a few dozen large pages needed gigabytes of them resident simultaneously. The browser
    /// build's wasm heap tops out at 2GB, and that is what it ran into: an
    /// OutOfMemoryException thrown inside stb's PNG decoder, part-way through "Loading Texture
    /// Pages", on a game that had loaded fine moments earlier with a less fragmented heap.
    ///
    /// Encoded, those same pages are a fraction of the size (PNG and QOI both), and nothing
    /// between the load and BindTextures needs a page's pixels -- every other consumer of
    /// TexturePages reads only Width/Height and the GL id, so AddPage takes the dimensions
    /// straight out of the blob's header and BindTextures decodes one page at a time, uploads it,
    /// and drops it again. Peak becomes one decoded page instead of all of them.
    /// </summary>
    private static readonly Dictionary<string, (byte Format, byte[] Blob)> _pendingPages = new();

    // Backs texture_set_interpolation/texture_get_interpolation. Real GM's toggle affects every
    // texture sampled from then on, not just newly-loaded ones -- rather than reapplying this at
    // every draw call (the GL texture object's own min/mag filter state persists once set, no need
    // to re-set it per bind), SetInterpolation below immediately re-applies it to every
    // already-uploaded page, and UploadTexture uses the current value for anything loaded after.
    public static bool TextureInterpolation = false;

    /// <summary>
    /// Whether textures sample with wrapping or clamped edges (gpu_set_tex_repeat). Applies to every
    /// live texture page, the same way SetInterpolation does -- there is one global sampler state in
    /// this renderer rather than one per texture.
    /// </summary>
    public static bool TextureRepeat { get; private set; }

    public static void SetRepeat(bool enabled)
    {
        TextureRepeat = enabled;
        var mode = (int)(enabled ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge);

        foreach (var (_, id) in TexturePages.Values)
        {
            GL.TextureParameter(id, TextureParameterName.TextureWrapS, mode);
            GL.TextureParameter(id, TextureParameterName.TextureWrapT, mode);
        }
    }

    public static void SetInterpolation(bool enabled)
    {
        TextureInterpolation = enabled;
        var minFilter = (int)(enabled ? TextureMinFilter.Linear : TextureMinFilter.Nearest);
        var magFilter = (int)(enabled ? TextureMagFilter.Linear : TextureMagFilter.Nearest);

        foreach (var (_, id) in TexturePages.Values)
        {
            GL.TextureParameter(id, TextureParameterName.TextureMinFilter, minFilter);
            GL.TextureParameter(id, TextureParameterName.TextureMagFilter, magFilter);
        }

        // WebGL-level texel filtering alone isn't enough to make this visible in practice: the
        // dominant upscale most players actually see is the browser's own CSS scale of the <canvas>
        // element up to its displayed size (a low-res game room shown in a much bigger window),
        // which player.html hardcodes to `image-rendering: pixelated` with no in-game way to change
        // it. Forward the toggle out to the host page so it can flip that too -- see
        // BrowserWindow.SetSmoothing/browserwindow.js's own comment for the full chain.
#if BROWSER
        BrowserWindow.SetSmoothing(enabled);
#endif
    }

    /// <summary>
    /// Registers one of the game's own texture pages, still encoded. See <see cref="_pendingPages"/>
    /// for why it isn't decoded here; <see cref="BindTextures"/> does that.
    /// </summary>
    public static void AddPage(string name, byte format, byte[] blob)
    {
        var (width, height) = ReadPageSize(name, format, blob);
        _pendingPages[name] = (format, blob);
        // Indexer, not Add: a load that died part-way through the previous game's pages (which is
        // exactly what the OOM above did) leaves entries behind that UnbindTextures can't clear
        // when there was never a GL context to delete them from, and re-adding one of those should
        // replace it rather than throw a duplicate-key exception on top of the original failure.
        TexturePages[name] = (new ImageResult
        {
            Width = width,
            Height = height,
            SourceComp = ColorComponents.RedGreenBlueAlpha,
            Comp = ColorComponents.RedGreenBlueAlpha
        }, -1);
    }

    /// <summary>
    /// A page's dimensions, read straight out of its header rather than by decoding it -- both
    /// formats state them in their first few bytes.
    /// </summary>
    private static (int Width, int Height) ReadPageSize(string name, byte format, byte[] blob)
    {
        switch (format)
        {
            // PNG: 8-byte signature, then the IHDR chunk, whose first two fields are the
            // dimensions as big-endian uint32s (bytes 16..24).
            case GameConverter.PageFormatPng when blob.Length >= 24:
                return (
                    (blob[16] << 24) | (blob[17] << 16) | (blob[18] << 8) | blob[19],
                    (blob[20] << 24) | (blob[21] << 16) | (blob[22] << 8) | blob[23]);
            // GameMaker's QOI: "fioq", then width and height as little-endian uint16s. Same layout
            // QoiConverter.GetImageFromSpan reads.
            case GameConverter.PageFormatQoi when blob.Length >= QoiConverter.HeaderSize:
                return (blob[4] | (blob[5] << 8), blob[6] | (blob[7] << 8));
            default:
                throw new NotImplementedException($"Unknown texture page format {format} for {name}");
        }
    }

    /// <summary>
    /// Decodes one page into the RGBA <see cref="ImageResult"/> the rest of the engine expects.
    /// </summary>
    private static ImageResult DecodePage(string name, byte format, byte[] blob) => format switch
    {
        GameConverter.PageFormatPng => ImageResult.FromMemory(blob, ColorComponents.RedGreenBlueAlpha),
        GameConverter.PageFormatQoi => DecodeQoiPage(blob),
        _ => throw new NotImplementedException($"Unknown texture page format {format} for {name}")
    };

    /// <summary>
    /// Decodes one GameMaker-flavoured QOI texture page (see GameConverter.PageFormatQoi).
    /// </summary>
    private static ImageResult DecodeQoiPage(byte[] blob)
    {
        // QoiConverter hands back BGRA -- B, G, R, A in memory, the order D3D and the data.win use.
        // UploadTexture uploads with PixelFormat.Rgba, so red and blue swap on the way into the
        // array (done as part of the copy out, rather than a second pass over it: a page is
        // megabytes of pixels).
        var image = QoiConverter.GetImageFromSpan(blob);
        var bgra = image.GetRawImageData();
        var rgba = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return new ImageResult
        {
            Width = image.Width,
            Height = image.Height,
            SourceComp = ColorComponents.RedGreenBlueAlpha,
            Comp = ColorComponents.RedGreenBlueAlpha,
            Data = rgba
        };
    }

    public static void UnbindTextures()
    {
        _pendingPages.Clear();

        if (TexturePages.Count == 0)
        {
            return;
        }

        // No window means no GL context, so nothing here was ever uploaded and there is nothing to
        // delete -- but the entries are still the previous (failed) load's and have to go, or
        // AddPage would be building on top of them.
        if (CustomWindow.Instance == null)
        {
            TexturePages.Clear();
            return;
        }

        Console.Write("Unbinding textures...");

        GraphicsManager.BindTexture0(0);
        // doesnt unbind other texture units. is that gonna be a problem?

        foreach (var name in TexturePages.Keys)
        {
            DeleteTexture(name);
        }

        Console.WriteLine(" Done!");
    }

    public static void BindTextures()
    {
        Console.Write("Binding textures...");

        // One page at a time, and each one's encoded bytes are dropped before it is decoded, so the
        // high-water mark is a single page's pixels rather than every page's at once. See
        // _pendingPages.
        foreach (var name in _pendingPages.Keys.ToArray())
        {
            var (format, blob) = _pendingPages[name];
            _pendingPages.Remove(name);
            UploadTexture(name, DecodePage(name, format, blob));
        }

        GC.Collect(); // gc to remove cpu texture data

        Console.WriteLine(" Done!");
    }

    public static void UploadTexture(string name, ImageResult image)
    {
        GraphicsManager.PushMessage($"UploadTexture Name:{name}");

        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GraphicsManager.LabelObject(ObjectLabelIdentifier.Texture, texture, name);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, image.Width, image.Height);
        GL.TextureSubImage2D(texture, 0, 0, 0, image.Width, image.Height, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
        GL.TextureParameter(texture, TextureParameterName.TextureMinFilter, (int)(TextureInterpolation ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
        GL.TextureParameter(texture, TextureParameterName.TextureMagFilter, (int)(TextureInterpolation ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
        // Wrap too, for the same reason the filters are re-applied here: SetRepeat/SetInterpolation
        // only walk the pages that exist when they are called, so a texture created later keeps
        // OpenGL's own defaults. For the filters that default is close enough to be invisible; for
        // wrapping it is GL_REPEAT, while every page loaded with the game is CLAMP_TO_EDGE. A
        // sprite added at runtime then samples its top row from its bottom one wherever it lands on
        // a half-texel -- which is a one-pixel line across the top of it, and how this was found.
        var wrap = (int)(TextureRepeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge);
        GL.TextureParameter(texture, TextureParameterName.TextureWrapS, wrap);
        GL.TextureParameter(texture, TextureParameterName.TextureWrapT, wrap);

        image.Data = null; // let this get gc'd since it was uploaded to the gpu

        TexturePages[name] = (image, texture);
        GraphicsManager.PopMessage();
    }

    public static void DeleteTexture(string name)
    {
        GraphicsManager.PushMessage($"DeleteTexture {name}");
        _pendingPages.Remove(name);
        if (TexturePages.Remove(name, out var value))
        {
            GL.DeleteTexture(value.id);
        }
        GraphicsManager.PopMessage();
    }
}
