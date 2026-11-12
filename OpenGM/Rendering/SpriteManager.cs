using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.SerializedFiles;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;
using System.Diagnostics;
using UndertaleModLib.Models;

namespace OpenGM.Rendering;

public static class SpriteManager
{
    // https://manual.gamemaker.io/monthly/en/GameMaker_Language/GML_Reference/Drawing/Colour_And_Alpha/draw_set_colour.htm
    public static int DrawColor = 16777215;
    // https://manual.gamemaker.io/monthly/en/GameMaker_Language/GML_Reference/Drawing/Colour_And_Alpha/draw_set_alpha.htm
    public static double DrawAlpha = 1;

    public static Dictionary<int, SpriteData> _spriteDict = new();

    /// <summary>
    /// screen_save_part (ScreenFunctions.cs) stores its captured pixels here instead of actually
    /// encoding and writing a real PNG to disk (no PNG encoder is wired up, only StbImageSharp's
    /// decoder) -- the extremely common screen_save_part-immediately-followed-by-sprite_add-of-the-
    /// same-filename idiom (a screenshot used as a one-off sprite, never actually needed as a
    /// persisted file) works correctly this way; sprite_add checks here first, keyed by the same
    /// resolved absolute path both functions compute via Entry.ResolveDataWinPath.
    /// </summary>
    public static Dictionary<string, ImageResult> _screenshotCache = new();

    public static bool SpriteExists(int id) => id != -1 && _spriteDict.ContainsKey(id);

    public static SpriteData? GetSpriteAsset(int name)
    {
        if (name == -1)
        {
            return null;
        }

        if (_spriteDict.ContainsKey(name))
        {
            return _spriteDict[name];
        }

        DebugLog.LogError($"Sprite Dictionary does not contain {name}");
        DebugLog.PrintCallStack(DebugLog.LogType.Error);
        return null;
    }

    public static Vector2i GetSpriteOrigin(int name)
    {
        if (name == -1)
        {
            DebugLog.LogWarning($"Tried to get origin of null sprite");
            return Vector2i.Zero;
        }

        return _spriteDict[name].Origin;
    }

    public static SpritePageItem GetSpritePageItem(string name, double index)
    {
        return GetSpritePageItem(AssetIndexManager.GetIndex(AssetType.sprites, name), index);
    }

    public static SpritePageItem GetSpritePageItem(int id, double index)
    {
        var subimages = _spriteDict[id].Textures;
        index %= subimages.Count;
        var floored = CustomMath.FloorToInt(index);

        if (floored < 0)
        {
            throw new NotImplementedException($"Trying to get SpritePageItem for index {index} floored:{floored}");
        }

        return subimages[floored];
    }

    public static void DrawSprite(int name, double index, double x, double y)
        => DrawSpriteExt(name, index, x, y, 1, 1, 0, 16777215, 1);

    public static void DrawSpriteExt(int name, double index, double x, double y, double xscale, double yscale, double rot, int blend, double alpha)
    {
        if (name < 0)
        {
            DebugLog.LogWarning($"Tried to draw sprite {name} with index {index}.");
            return;
        }

        var asset = GetSpriteAsset(name);
        if (asset is not null)
        {
            index = CustomMath.Mod(index, asset.Textures.Count);
        }

        var sprite = GetSpritePageItem(name, index);
        var origin = GetSpriteOrigin(name);

        var c = blend.ABGRToCol4(alpha);

        CustomWindow.Draw(new GMSpriteJob()
        {
            texture = sprite,
            screenPos = new Vector2d(x, y),
            angle = rot,
            scale = new Vector2d(xscale, yscale),
            Colors = [c, c, c, c],
            origin = origin,
            nineSlice = asset?.NineSlice,
            //fogEnabled = FogEnabled,
            //fogColor = FogColor.ABGRToCol4(1) // https://github.com/YoYoGames/GameMaker-HTML5/blob/develop/scripts/functions/Function_D3D.js#L1095
        });
    }

    public static void DrawSpritePart(int name, int index, double left, double top, double width, double height, double x, double y)
    {
        DrawSpritePartExt(name, index, left, top, width, height, x, y, 1, 1, 16777215, 1);
    }

    public static void DrawSpritePartExt(int name, double index, double left, double top, double width, double height, double x, double y, double xscale, double yscale, int blend, double alpha)
    {
        //var sprite = SpritePart(name, index, left, top, width, height);
        var sprite = GetSpritePageItem(name, index);
        var c = blend.ABGRToCol4(alpha);
        CustomWindow.Draw(new GMSpritePartJob()
        {
            texture = sprite,
            screenPos = new Vector2d(x, y),
            angle = 0,
            scale = new Vector2d(xscale, yscale),
            Colors = [c, c, c, c],
            origin = Vector2.Zero,
            left = left,
            top = top,
            width = width,
            height = height
        });
    }

    /// <summary>
    /// Draws an instance's own sprite. Deliberately does NOT test `visible`: that flag decides
    /// whether the runtime runs an instance's Draw event at all (DrawManager.ShouldDraw already
    /// applies it on that path), not what an explicit draw_self() does when a game calls it.
    ///
    /// The difference is load-bearing for the very common "render these objects into a surface once,
    /// then hide them" idiom. tijit engine 2's outlineRenderer does exactly that --
    /// `with (objectList[i]) { draw_self(); visible = false; }` -- so honouring `visible` here drew
    /// each of those objects into the surface once and then, on every later redraw, not at all:
    /// its moving blocks and trapped spikes vanished from the tile layer, uncovering the gravity
    /// switch a block was supposed to be sitting on top of.
    /// </summary>
    public static void DrawSelf(GamemakerObject obj)
    {
        if (obj.sprite_index == -1)
        {
            return;
        }

        var frameCount = GetNumberOfFrames(obj.sprite_index);

        if (frameCount == 0)
        {
            DebugLog.LogWarning($"Tried to DrawSelf on {obj.instanceId} ({obj.Definition.Name}) with sprite {GetSpriteAsset(obj.sprite_index)?.Name} that has 0 subimages!");
            DebugLog.PrintCallStack(DebugLog.LogType.Warning);
            return;
        }

        var image_index = GetIndexFromImageIndex(obj.image_index, frameCount);

        DrawSpriteExt(obj.sprite_index, image_index, obj.x, obj.y, obj.image_xscale, obj.image_yscale, obj.image_angle, obj.image_blend, obj.image_alpha);
    }

    private static int GetIndexFromImageIndex(double index, int imageNumber)
    {
        if (imageNumber == 0)
        {
            DebugLog.LogError("Tried to call GetIndexFromImageIndex with imageNumber of 0!");
            return 0;
        }

        // Plain floor-and-wrap. An animation resting at the end of its run sits just *below* the
        // frame count (3.99999833 for a 4-frame sprite stepped at 0.1 -- GamemakerObject.Animate
        // accumulates in single precision, as real GM does), so flooring already gives the last
        // frame and no special case is needed. This briefly had one, back when the accumulation was
        // done in double and landed on exactly 4.0.
        var ind = CustomMath.FloorToInt(index) % imageNumber;
        if (ind < 0)
        {
            ind += imageNumber;
        }

        return ind;
    }

    public static void draw_sprite_stretched(int name, int index, double x, double y, double w, double h, int color, double alpha)
    {
        var sprite = GetSpritePageItem(name, index);

        var spriteWidth = sprite.BoundingWidth;
        var spriteHeight = sprite.BoundingHeight;

        var c = color.ABGRToCol4(alpha);

        CustomWindow.Draw(new GMSpriteJob()
        {
            texture = sprite,
            screenPos = new Vector2d(x, y),
            angle = 0,
            scale = new Vector2d(w / spriteWidth, h / spriteHeight),
            Colors = [c, c, c, c],
            origin = Vector2.Zero,
            // draw_sprite_stretched is the call a nine-sliced UI box is usually drawn with.
            nineSlice = GetSpriteAsset(name)?.NineSlice
        });
    }

    public static int GetNumberOfFrames(int name)
    {
        if (!_spriteDict.TryGetValue(name, out var value))
        {
            DebugLog.LogWarning($"Tried to get number of frames for sprite {name} (count = {_spriteDict.Count})");
            DebugLog.PrintCallStack(DebugLog.LogType.Warning);
            return 0;
        }

        return value.Textures.Count;
    }


    public static int sprite_create_from_surface(int surfaceId, int x, int y, int w, int h, bool removeback, bool smooth, int xorig, int yorig)
    {
        // https://github.com/YoYoGames/GameMaker-HTML5/blob/develop/scripts/functions/Function_Sprite.js#L485
        // https://github.com/YoYoGames/GameMaker-HTML5/blob/develop/scripts/yyWebGL.js#L4370

        // we have to do a song and dance because sprites require texture pages and whatnot to draw

        var spriteId = _spriteDict.Keys.Max() + 1; // this probably breaks sometimes
        var texturePageName = $"sprite_create_from_surface {spriteId}"; // needed for texture page lookup

        // make a copy of the texture. theres better ways to do this but this should work
        GraphicsManager.PushMessage($"sprite_create_from_surface Surf:{surfaceId}, x:{x}, y:{y}, w:{w}, h:{h}");

        var pixels = new byte[w * h * 4];
        {
            GraphicsManager.PushMessage("read pixels");
            var texture = SurfaceManager.GetSurfaceTexture(surfaceId);
            GraphicsManager.FlushPolygonBatch(); // read back what's actually been drawn
            GL.GetTextureSubImage(texture, 0, x, y, 0, w, h, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels.Length, pixels);
            GraphicsManager.PopMessage();
        }

        // store it as a "page". its really just one texture that the sprite will use to draw
        var imageResult = new ImageResult()
        {
            Width = w,
            Height = h,
            Data = pixels
        };
        PageManager.UploadTexture(texturePageName, imageResult);
        GraphicsManager.PopMessage();

        // create a sprite with the single texture
        var spritePage = new SpritePageItem
        {
            SourceX = 0,
            SourceY = 0,
            SourceWidth = w,
            SourceHeight = h,
            TargetX = 0,
            TargetY = 0,
            TargetWidth = w,
            TargetHeight = h,
            BoundingWidth = w,
            BoundingHeight = h,
            Page = texturePageName
        };
        var sprite = new SpriteData
        {
            AssetIndex = spriteId,
            Name = texturePageName,
            Width = w,
            Height = h,
            MarginLeft = 0,
            MarginRight = 0,
            MarginBottom = 0,
            MarginTop = 0,
            BBoxMode = 0,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
            OriginX = xorig,
            OriginY = yorig,
            Textures = [spritePage],
            CollisionMasks = [], // no idea
            // Matches what GameConverter gives a loaded GM8/GMS1 sprite, and for the same reason:
            // GetIndexIncrement multiplies image_speed by this, so a zero here is a sprite that can
            // never animate. It was zero, which meant *any* sprite added at runtime was frozen --
            // including every animated sprite a mod adds through the SDK.
            PlaybackSpeed = 1,
            PlaybackSpeedType = AnimSpeedType.FramesPerGameFrame
        };
        _spriteDict.Add(sprite.AssetIndex, sprite);

        return sprite.AssetIndex;
    }

    /// <summary>
    /// Loads an external image file (a horizontal filmstrip when imgnumber &gt; 1, same convention
    /// GameMaker's own sprite editor uses for multi-frame imports) as a brand-new sprite asset --
    /// same texture-page song-and-dance as sprite_create_from_surface above, just decoding a real
    /// file via StbImageSharp instead of copying from a live surface, and sliced into imgnumber
    /// equal-width frames instead of always exactly one.
    /// </summary>
    /// <summary>
    /// The picture behind a `sprite_add` / `background_add`, from wherever the game put it.
    ///
    /// Not `File.ReadAllBytes`: on the browser build a game's own writes and the files it shipped
    /// with are separate mounts in a VFS, and neither is a real directory. See
    /// <see cref="GMFileSystemExtension.ReadGameFile"/> for all three places and why each is needed.
    /// </summary>
    private static byte[] ReadImage(string path) =>
        GMFileSystemExtension.ReadGameFile(path)
        ?? throw new FileNotFoundException("no such image", path);

    public static int sprite_add(string path, int imgnumber, bool removeback, bool smooth, int xorig, int yorig)
    {
        imgnumber = Math.Max(1, imgnumber);

        if (!_screenshotCache.Remove(path, out var image))
        {
            var bytes = ReadImage(path);
            image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        }

        var frameWidth = image.Width / imgnumber;
        var frameHeight = image.Height;

        if (removeback)
        {
            // Real GM8 "remove background" chroma-keys on the bottom-left pixel's own colour --
            // every pixel matching it exactly (RGB, ignoring existing alpha) becomes fully
            // transparent, everywhere in the image, not just contiguous background regions.
            var bottomLeftIndex = ((image.Height - 1) * image.Width) * 4;
            byte keyR = image.Data[bottomLeftIndex], keyG = image.Data[bottomLeftIndex + 1], keyB = image.Data[bottomLeftIndex + 2];
            for (var i = 0; i < image.Data.Length; i += 4)
            {
                if (image.Data[i] == keyR && image.Data[i + 1] == keyG && image.Data[i + 2] == keyB)
                {
                    image.Data[i + 3] = 0;
                }
            }
        }

        var spriteId = _spriteDict.Keys.Max() + 1; // this probably breaks sometimes
        var textures = new List<SpritePageItem>(imgnumber);

        for (var frame = 0; frame < imgnumber; frame++)
        {
            var frameData = new byte[frameWidth * frameHeight * 4];
            for (var row = 0; row < frameHeight; row++)
            {
                var srcOffset = ((row * image.Width) + (frame * frameWidth)) * 4;
                var dstOffset = row * frameWidth * 4;
                Array.Copy(image.Data, srcOffset, frameData, dstOffset, frameWidth * 4);
            }

            var texturePageName = $"sprite_add {spriteId}_{frame}";
            PageManager.UploadTexture(texturePageName, new ImageResult { Width = frameWidth, Height = frameHeight, Data = frameData });

            textures.Add(new SpritePageItem
            {
                SourceX = 0,
                SourceY = 0,
                SourceWidth = frameWidth,
                SourceHeight = frameHeight,
                TargetX = 0,
                TargetY = 0,
                TargetWidth = frameWidth,
                TargetHeight = frameHeight,
                BoundingWidth = frameWidth,
                BoundingHeight = frameHeight,
                Page = texturePageName
            });
        }

        var sprite = new SpriteData
        {
            AssetIndex = spriteId,
            Name = $"sprite_add {spriteId}",
            Width = frameWidth,
            Height = frameHeight,
            MarginLeft = 0,
            MarginRight = frameWidth - 1,
            MarginBottom = frameHeight - 1,
            MarginTop = 0,
            BBoxMode = 0,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
            OriginX = xorig,
            OriginY = yorig,
            Textures = textures,
            CollisionMasks = [],
            // Same as the single-image path above: image_speed is multiplied by this, so zero is a
            // sprite that never animates.
            PlaybackSpeed = 1,
            PlaybackSpeedType = AnimSpeedType.FramesPerGameFrame
        };
        _spriteDict.Add(sprite.AssetIndex, sprite);

        return sprite.AssetIndex;
    }

    /// <summary>
    /// Loads an external image file as a brand-new background asset -- single-image, unlike
    /// sprite_add's multi-frame filmstrip support, matching the Background record's own single
    /// Texture field (GM8/GMS backgrounds never had sprite-style animation frames).
    /// </summary>
    public static int background_add(string path, bool transparent, bool smooth)
    {
        var bytes = ReadImage(path);
        var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);

        if (transparent)
        {
            // Same bottom-left-pixel chroma-key convention as sprite_add's removeback.
            var bottomLeftIndex = ((image.Height - 1) * image.Width) * 4;
            byte keyR = image.Data[bottomLeftIndex], keyG = image.Data[bottomLeftIndex + 1], keyB = image.Data[bottomLeftIndex + 2];
            for (var i = 0; i < image.Data.Length; i += 4)
            {
                if (image.Data[i] == keyR && image.Data[i + 1] == keyG && image.Data[i + 2] == keyB)
                {
                    image.Data[i + 3] = 0;
                }
            }
        }

        var backgroundId = GameLoader.Backgrounds.Count == 0 ? 0 : GameLoader.Backgrounds.Keys.Max() + 1;
        var texturePageName = $"background_add {backgroundId}";
        PageManager.UploadTexture(texturePageName, image);

        GameLoader.Backgrounds[backgroundId] = new SerializedFiles.Background
        {
            AssetIndex = backgroundId,
            Name = texturePageName,
            Transparent = transparent,
            Smooth = smooth,
            Preload = true,
            Texture = new SpritePageItem
            {
                SourceX = 0,
                SourceY = 0,
                SourceWidth = image.Width,
                SourceHeight = image.Height,
                TargetX = 0,
                TargetY = 0,
                TargetWidth = image.Width,
                TargetHeight = image.Height,
                BoundingWidth = image.Width,
                BoundingHeight = image.Height,
                Page = texturePageName
            }
        };

        return backgroundId;
    }

    /// <summary>
    /// Reassigns background index "ind" to render as background "back"'s content -- same
    /// independent-copy reasoning as sprite_assign (see CopySpriteTextures's own doc comment): the
    /// background_add -&gt; background_assign -&gt; background_delete idiom (jtool's own
    /// bg.png skin loading) would otherwise have background_delete free the exact texture page
    /// "ind" was just pointed at, if the SpritePageItem were shared by reference instead of copied.
    /// </summary>
    public static void background_assign(int ind, int back)
    {
        if (!GameLoader.Backgrounds.TryGetValue(ind, out var target) || !GameLoader.Backgrounds.TryGetValue(back, out var source))
        {
            return;
        }

        target.Transparent = source.Transparent;
        target.Smooth = source.Smooth;
        target.Texture = source.Texture != null ? CopySpriteTextures([source.Texture])[0] : null;
    }

    /// <summary>
    /// Same texture-page song-and-dance as sprite_create_from_surface above, just wrapping the
    /// copied pixels in a Background record instead of a SpriteData.
    /// </summary>
    public static int background_create_from_surface(int surfaceId, int x, int y, int w, int h, bool removeback, bool smooth)
    {
        var backgroundId = GameLoader.Backgrounds.Count == 0 ? 0 : GameLoader.Backgrounds.Keys.Max() + 1;
        var texturePageName = $"background_create_from_surface {backgroundId}";

        var pixels = new byte[w * h * 4];
        {
            var texture = SurfaceManager.GetSurfaceTexture(surfaceId);
            GraphicsManager.FlushPolygonBatch(); // read back what's actually been drawn
            GL.GetTextureSubImage(texture, 0, x, y, 0, w, h, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels.Length, pixels);
        }

        PageManager.UploadTexture(texturePageName, new ImageResult { Width = w, Height = h, Data = pixels });

        GameLoader.Backgrounds[backgroundId] = new SerializedFiles.Background
        {
            AssetIndex = backgroundId,
            Name = texturePageName,
            Transparent = false,
            Smooth = smooth,
            Preload = true,
            Texture = new SpritePageItem
            {
                SourceX = 0,
                SourceY = 0,
                SourceWidth = w,
                SourceHeight = h,
                TargetX = 0,
                TargetY = 0,
                TargetWidth = w,
                TargetHeight = h,
                BoundingWidth = w,
                BoundingHeight = h,
                Page = texturePageName
            }
        };

        return backgroundId;
    }

    public static bool background_delete(int index)
    {
        if (GameLoader.Backgrounds.Remove(index, out var background))
        {
            if (background.Texture != null)
            {
                PageManager.DeleteTexture(background.Texture.Page);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Builds a brand-new, independent set of texture pages holding the same pixel content as
    /// sourceTextures (read back from the GPU, since PageManager.UploadTexture nulls out the CPU-side
    /// copy right after uploading), for sprite_assign. SpritePageItem.Page is just a lookup key into
    /// PageManager.TexturePages, so directly sharing the source's own SpritePageItem list (or even a
    /// shallow copy of it) would leave both sprites pointing at the exact same texture page by name
    /// -- fine until whichever sprite still "owns" that page gets deleted (sprite_delete calls
    /// PageManager.DeleteTexture(name)), which would then pull the texture out from under every other
    /// sprite still referencing it by that same name. This is exactly the sprite_add -&gt;
    /// sprite_assign -&gt; sprite_delete idiom real games commonly use (load a temp sprite, assign it
    /// onto a permanent slot, free the temp) -- without a real copy here, that idiom silently
    /// destroys the target sprite's own rendering the moment the temp one is deleted.
    /// </summary>
    public static List<SpritePageItem> CopySpriteTextures(List<SpritePageItem> sourceTextures)
    {
        var copies = new List<SpritePageItem>(sourceTextures.Count);
        foreach (var src in sourceTextures)
        {
            var pixels = new byte[src.SourceWidth * src.SourceHeight * 4];
            var (_, texture) = PageManager.TexturePages[src.Page];
            GraphicsManager.FlushPolygonBatch(); // read back what's actually been drawn
            GL.GetTextureSubImage(texture, 0, src.SourceX, src.SourceY, 0, src.SourceWidth, src.SourceHeight, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels.Length, pixels);

            var newPageName = $"sprite_assign {Guid.NewGuid():N}";
            PageManager.UploadTexture(newPageName, new ImageResult { Width = src.SourceWidth, Height = src.SourceHeight, Data = pixels });

            copies.Add(new SpritePageItem
            {
                SourceX = 0,
                SourceY = 0,
                SourceWidth = src.SourceWidth,
                SourceHeight = src.SourceHeight,
                TargetX = src.TargetX,
                TargetY = src.TargetY,
                TargetWidth = src.TargetWidth,
                TargetHeight = src.TargetHeight,
                BoundingWidth = src.BoundingWidth,
                BoundingHeight = src.BoundingHeight,
                Page = newPageName
            });
        }
        return copies;
    }

    /// <summary>
    /// Captures a region of the application surface (what's actually on screen) into
    /// _screenshotCache instead of encoding and writing a real PNG (see that field's own doc
    /// comment for why) -- keyed by the same resolved absolute path sprite_add computes, so the
    /// common screen_save_part-then-sprite_add-of-the-same-filename idiom works without ever
    /// touching disk.
    /// </summary>
    public static void screen_save_part(string path, int x, int y, int w, int h)
    {
        var pixels = new byte[w * h * 4];

        // The application surface isn't necessarily created yet the first time this is called
        // (e.g. an object's own Create event running before the room's ever completed a real
        // render pass) -- fall back to a blank capture rather than crashing on a nonexistent
        // surface, so a script chaining straight into sprite_add still gets a valid (just blank)
        // sprite instead of an unhandled exception.
        if (SurfaceManager.surface_exists(SurfaceManager.application_surface))
        {
            var texture = SurfaceManager.GetSurfaceTexture(SurfaceManager.application_surface);
            GraphicsManager.FlushPolygonBatch(); // read back what's actually been drawn
            GL.GetTextureSubImage(texture, 0, x, y, 0, w, h, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels.Length, pixels);
        }

        _screenshotCache[path] = new ImageResult { Width = w, Height = h, Data = pixels };
    }

    public static bool sprite_delete(int index)
    {
        GraphicsManager.PushMessage($"SpriteDelete {index}");
        // only used with sprite_create_from_surface

        // remove the sprite
        if (_spriteDict.Remove(index, out var sprite))
        {
            // remove the copied texture
            PageManager.DeleteTexture(sprite.Textures[0].Page);
            GraphicsManager.PopMessage();
            return true;
        }

        GraphicsManager.PopMessage();
        return false;
    }
}
