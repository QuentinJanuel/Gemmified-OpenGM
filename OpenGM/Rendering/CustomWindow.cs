using OpenGM.IO;
using OpenGM.SerializedFiles;
using OpenTK.Core.Native;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using StbImageSharp;
using System.Runtime.InteropServices;
using UndertaleModLib.Models;

namespace OpenGM.Rendering;

public class CustomWindow : GameWindow
{
    public static CustomWindow Instance { get; private set; } = null!;

    public static List<GMBaseJob> DebugJobs = new();

#if DEBUG_EXTRA
    private static readonly DebugProc DebugMessageDelegate = OnDebugMessage; // prevents gc
    private static void OnDebugMessage(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr messagePtr, IntPtr param)
    {
        var message = MarshalTk.MarshalPtrToString(messagePtr);
        if (type == DebugType.DebugTypeError) throw new Exception($"GL error from {source}: {message}");
        DebugLog.LogInfo($"GL message from {source}: {message}");
    }
#endif

    public CustomWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
#if DEBUG_EXTRA
        // https://opentk.net/learn/appendix_opengl/debug_callback.html
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous); // for callstack
        GL.DebugMessageCallback(DebugMessageDelegate, IntPtr.Zero);
        unsafe
        {
            GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DebugTypePushGroup, DebugSeverityControl.DontCare, 0, (int*)null, false);
            GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DebugTypePopGroup, DebugSeverityControl.DontCare, 0, (int*)null, false);
        }
#endif

        GLFWProvider.SetErrorCallback((code, msg) => DebugLog.LogError($"GLFW error {code}: {msg}"));

        Instance = this;

        DebugLog.LogInfo($"-- CustomWindow .ctor --");
        DebugLog.LogInfo($"  Version: {nativeWindowSettings.API} {nativeWindowSettings.APIVersion}");
        DebugLog.LogInfo($"  Profile: {nativeWindowSettings.Profile}");
        DebugLog.LogInfo($"  Flags: {nativeWindowSettings.Flags}");
        DebugLog.LogInfo($"------------------------");
        DebugLog.LogInfo($"  GL Version: {GL.GetString(StringName.Version)}");
        DebugLog.LogInfo($"  GLSL Version: {GL.GetString(StringName.ShadingLanguageVersion)}");
        DebugLog.LogInfo($"------------------------");

        // https://github.com/YoYoGames/GameMaker-HTML5/blob/develop/scripts/_GameMaker.js#L721
        SurfaceManager.ApplicationWidth = FramebufferSize.X;
        SurfaceManager.ApplicationHeight = FramebufferSize.Y;

        GraphicsManager.Init();

        /*
        GL.DebugMessageCallback((source, type, id, severity, length, messagePtr, param) =>
        {
            var message = MarshalTk.MarshalPtrToString(messagePtr);
            if (type == DebugType.DebugTypeError) throw new Exception($"GL error from {source}: {message}");
            DebugLog.LogInfo($"GL message from {source}: {message}");
        }, 0);
        GL.Enable(EnableCap.DebugOutput);

        GL.Enable(EnableCap.Texture2D); // always allow a texture to be drawn. does nothing if no texture is bound
        */
        GL.Enable(EnableCap.Blend); // always allow blending

        // bm_normal
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
    }

#if BROWSER
    /// <summary>
    /// Browser-wasm-only: JS drives frames via requestAnimationFrame calling this (through
    /// WasmEntry.RenderFrame) instead of the real GLFW event loop's OnRenderFrame callback, since
    /// there's no real windowing system driving callbacks here -- everything else about the frame
    /// (DrawManager.FixedUpdate, DebugJobs, timing) is identical to the native path.
    /// </summary>
    public void RenderFrame(double time) => OnRenderFrame(new FrameEventArgs(time));
#endif

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        TimingManager.BeginFrame(args.Time);

        InputHandler.UpdateMouseState(MouseState);
        InputHandler.UpdateKeyboardState(KeyboardState);

        // DrawManager.FixedUpdate() covers both stepping and drawing.
        DrawManager.FixedUpdate();

        AudioManager.Update();

        foreach (var item in DebugJobs)
        {
            Draw(item);
        }

        DebugJobs.Clear();

        // Last line of defence: nothing may still be queued when the frame is presented.
        GraphicsManager.FlushPolygonBatch();

        SwapBuffers();

        TimingManager.EndFrame();
    }

    public static void Draw(GMBaseJob baseJob)
    {
        switch (baseJob)
        {
            case GMTextJob textJob:
                Draw(textJob);
                return;
            case GMSpritePartJob spritePartJob:
                Draw(spritePartJob);
                return;
            case GMSpriteJob spriteJob:
                Draw(spriteJob);
                return;
            case GMLineJob lineJob:
                Draw(lineJob);
                return;
            case GMLinesJob linesJob:
                Draw(linesJob);
                return;
            case GMPolygonJob polygonJob:
                Draw(polygonJob);
                return;
            default:
                throw new NotImplementedException($"Don't know how to draw {baseJob}");
        }
    }

    public static void Draw(GMTextJob textJob)
    {

        if (string.IsNullOrEmpty(textJob.text))
        {
            return;
        }

        textJob.text = CompatFlags.ApplyHashNewlines(textJob.text);

        // Wrapping has to run on the post-# text (a # is a real line break to a pre-GMS2 game, so
        // wrapping before it would measure lines that never get drawn), and it has to run here
        // rather than at the call site because ApplyHashNewlines isn't idempotent -- it unescapes
        // \# to #, which a second pass would then turn into a newline.
        var lines = TextManager.WrapText(textJob.text, textJob.wrapWidth);
        var lineYOffset = 0f;

        // Full-text width/height derived from the lines we just split, instead of calling
        // TextManager.StringWidth/StringHeight (which would each SplitLines() the whole string
        // again). With ~85 text jobs/frame that's ~2 redundant full-string splits per job removed;
        // per-line LineWidth/LineHeight below likewise avoid re-splitting single lines. Numerically
        // identical to the old StringWidth/StringHeight (which now route through the same helpers).
        var maxLineWidth = 0;
        foreach (var l in lines)
        {
            var w = TextManager.LineWidth(l);
            if (w > maxLineWidth)
            {
                maxLineWidth = w;
            }
        }

        var textWidth = (int)(maxLineWidth * textJob.scale.X);
        // Paced by THIS job's line separation, not the font's default one: the draw_text_ext family
        // can pass an explicit separation, and the block that valign centres/bottom-aligns against
        // has to be the block actually about to be drawn below.
        var textHeight = (int)((((lines.Length - 1) * textJob.lineSep) + TextManager.LineHeight(lines[^1])) * textJob.scale.Y);

        var textLeft = textJob.halign switch
        {
            HAlign.fa_center => textJob.screenPos.X - (textWidth / 2),
            HAlign.fa_right => textJob.screenPos.X - textWidth,
            _ => textJob.screenPos.X
        };

        var textTop = textJob.valign switch
        {
            VAlign.fa_middle => textJob.screenPos.Y - (textHeight / 2),
            VAlign.fa_bottom => textJob.screenPos.Y - textHeight,
            _ => textJob.screenPos.Y
        };

        var textRight = textLeft + textWidth;
        var textBottom = textTop + textHeight;

        // Batches every glyph quad from this call into as few GraphicsManager.Draw calls as
        // possible instead of one draw call (+ one redundant texture rebind) per character. Under
        // browser-wasm every GL.* call is a [JSImport]/WebGL2 boundary crossing, meaningfully
        // pricier than a native GL call -- a profiler run against a real game (jtool) showed text
        // glyphs alone accounting for ~78% of all draw calls issued in a frame (650 of ~835),
        // dwarfing everything else, purely from this. A normal (non-sprite) font uses the exact
        // same texture page for every character, so there's no correctness reason it couldn't be
        // one draw call -- sprite fonts CAN reference a different page per glyph frame, so this
        // flushes (issues the accumulated draw call so far) whenever the page actually changes
        // rather than assuming a single page; for a normal font that never happens, so the whole
        // string collapses to exactly one flush.
        var batch = new List<GraphicsManager.Vertex>(64);
        var batchPageId = -1;

        void FlushBatch()
        {
            if (batch.Count == 0)
            {
                return;
            }

            GraphicsManager.BindTexture0(batchPageId);
            GraphicsManager.Draw(PrimitiveType.Triangles, CollectionsMarshal.AsSpan(batch));
            batch.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var width = (int)(TextManager.LineWidth(line) * textJob.scale.X);

            var xOffset = 0d;
            if (textJob.halign == HAlign.fa_center)
            {
                xOffset = -(width / 2);
            }
            else if (textJob.halign == HAlign.fa_right)
            {
                xOffset = -width;
            }

            var yOffset = 0f;
            if (textJob.valign == VAlign.fa_middle)
            {
                yOffset = -(textHeight / 2);
            }
            else if (textJob.valign == VAlign.fa_bottom)
            {
                yOffset = -textHeight;
            }

            yOffset += lineYOffset;
            lineYOffset += textJob.lineSep;

            var stringLeft = textJob.screenPos.X + xOffset;
            var stringRight = textJob.screenPos.X + xOffset + width;
            var stringTop = -textJob.screenPos.Y - yOffset;
            var stringBottom = -textJob.screenPos.Y - yOffset - (int)(TextManager.LineHeight(line) * textJob.scale.Y);

            var points = new Vector2d[]
            {
                (stringLeft, stringTop),
                (stringRight, stringTop),
                (stringRight, stringBottom),
                (stringLeft, stringBottom)
            };

            for (var j = 0; j < line.Length; j++)
            {
                var character = line[j];

                if (!textJob.asset.entriesDict.TryGetValue(character, out var glyph))
                {
                    // offset sprite text for spaces
                    if (textJob.asset.IsSpriteFont() && character == ' ')
                    {
                        xOffset += textJob.asset.Size;
                    }

                    continue;
                }

                var pageItem = textJob.asset.texture;
                if (textJob.asset.IsSpriteFont())
                {
                    pageItem = SpriteManager.GetSpritePageItem(textJob.asset.spriteIndex, glyph.frameIndex);
                }

                var (texturePage, pageId) = PageManager.TexturePages[pageItem!.Page];

                var pageX = 0;
                var pageY = 0;

                if (!glyph.IsSpriteBased())
                {
                    pageX = pageItem.SourceX;
                    pageY = pageItem.SourceY;
                }

                var topLeftX = textJob.screenPos.X + xOffset + glyph.xOffset;
                var topLeftY = textJob.screenPos.Y + yOffset + glyph.yOffset;

                var leftX = (pageX + glyph.x) / (float)texturePage.Width;
                var rightX = (pageX + glyph.x + glyph.w) / (float)texturePage.Width;
                var topY = (pageY + glyph.y) / (float)texturePage.Height;
                var bottomY = (pageY + glyph.y + glyph.h) / (float)texturePage.Height;

                if (pageId != batchPageId)
                {
                    FlushBatch();
                    batchPageId = pageId;
                }

                var topLeftPos = new Vector3d(topLeftX, topLeftY, GraphicsManager.GR_Depth);
                var topRightPos = new Vector3d(topLeftX + glyph.w * textJob.scale.X, topLeftY, GraphicsManager.GR_Depth);
                var bottomRightPos = new Vector3d(topLeftX + glyph.w * textJob.scale.X, (topLeftY + glyph.h * textJob.scale.Y), GraphicsManager.GR_Depth);
                var bottomLeftPos = new Vector3d(topLeftX, topLeftY + glyph.h * textJob.scale.Y, GraphicsManager.GR_Depth);

                var topLeftCol = CustomMath.BlendBetweenPoints(topLeftPos, points, textJob.Colors);
                var topRightCol = CustomMath.BlendBetweenPoints(topRightPos, points, textJob.Colors);
                var bottomRightCol = CustomMath.BlendBetweenPoints(bottomRightPos, points, textJob.Colors);
                var bottomLeftCol = CustomMath.BlendBetweenPoints(bottomLeftPos, points, textJob.Colors);

                // Same two triangles a TriangleFan over [TL,TR,BR,BL] would produce (TL,TR,BR) and
                // (TL,BR,BL) -- just appended to the shared batch instead of drawn immediately.
                batch.Add(new(topLeftPos, topLeftCol, new(leftX, topY)));
                batch.Add(new(topRightPos, topRightCol, new(rightX, topY)));
                batch.Add(new(bottomRightPos, bottomRightCol, new(rightX, bottomY)));
                batch.Add(new(topLeftPos, topLeftCol, new(leftX, topY)));
                batch.Add(new(bottomRightPos, bottomRightCol, new(rightX, bottomY)));
                batch.Add(new(bottomLeftPos, bottomLeftCol, new(leftX, bottomY)));

                xOffset += glyph.shift * textJob.scale.X;
                if (textJob.asset.IsSpriteFont())
                {
                    xOffset += textJob.asset.sep * textJob.scale.X;
                }
            }
        }

        FlushBatch();
    }

    public static void Draw(GMSpriteJob spriteJob)
    {

        var (pageTexture, id) = PageManager.TexturePages[spriteJob.texture.Page];
        GraphicsManager.BindTexture0(id);

        // A nine-sliced sprite drawn at anything other than its own size is nine separate quads,
        // not one stretched one. Only when it's actually scaled: at 1:1 the slicing is a no-op and
        // this is the hot path for every ordinary sprite in the game.
        if (spriteJob.nineSlice is { Enabled: true }
            && (spriteJob.scale.X != 1 || spriteJob.scale.Y != 1))
        {
            DrawNineSlice(spriteJob, pageTexture);
            return;
        }

        // Gonna define some terminology here to make this easer
        // "Full Sprite" is the sprite area with padding around the outside - the bounding box.
        // "Draw Area" is the part of the screen the actual data from the page is being drawn to - the target.

        var fullSpriteLeft = spriteJob.screenPos.X - (spriteJob.origin.X * spriteJob.scale.X);
        var fullSpriteTop = spriteJob.screenPos.Y - (spriteJob.origin.Y * spriteJob.scale.Y);

        var drawAreaLeft = fullSpriteLeft + (spriteJob.texture.TargetX * spriteJob.scale.X);
        var drawAreaTop = fullSpriteTop + (spriteJob.texture.TargetY * spriteJob.scale.Y);
        var drawAreaWidth = spriteJob.texture.TargetWidth * spriteJob.scale.X;
        var drawAreaHeight = spriteJob.texture.TargetHeight * spriteJob.scale.Y;

        var drawAreaTopLeft = new Vector3d(drawAreaLeft, drawAreaTop, GraphicsManager.GR_Depth);
        var drawAreaTopRight = new Vector3d(drawAreaLeft + drawAreaWidth, drawAreaTop, GraphicsManager.GR_Depth);
        var drawAreaBottomRight = new Vector3d(drawAreaLeft + drawAreaWidth, drawAreaTop + drawAreaHeight, GraphicsManager.GR_Depth);
        var drawAreaBottomLeft = new Vector3d(drawAreaLeft, drawAreaTop + drawAreaHeight, GraphicsManager.GR_Depth);

        var topLeftUV = new Vector2d(
            (double)spriteJob.texture.SourceX / pageTexture.Width,
            (double)spriteJob.texture.SourceY / pageTexture.Height);

        var UVWidth = (double)spriteJob.texture.SourceWidth / pageTexture.Width;
        var UVHeight = (double)spriteJob.texture.SourceHeight / pageTexture.Height;

        var topRightUV = new Vector2d(topLeftUV.X + UVWidth, topLeftUV.Y);
        var bottomRightUV = new Vector2d(topLeftUV.X + UVWidth, topLeftUV.Y + UVHeight);
        var bottomLeftUV = new Vector2d(topLeftUV.X, topLeftUV.Y + UVHeight);

        drawAreaTopLeft = drawAreaTopLeft.RotateAroundPoint(spriteJob.screenPos, spriteJob.angle);
        drawAreaTopRight = drawAreaTopRight.RotateAroundPoint(spriteJob.screenPos, spriteJob.angle);
        drawAreaBottomRight = drawAreaBottomRight.RotateAroundPoint(spriteJob.screenPos, spriteJob.angle);
        drawAreaBottomLeft = drawAreaBottomLeft.RotateAroundPoint(spriteJob.screenPos, spriteJob.angle);

        GraphicsManager.Draw(PrimitiveType.TriangleFan, [
            new(drawAreaTopLeft, spriteJob.Colors[0], topLeftUV),
            new(drawAreaTopRight, spriteJob.Colors[1], topRightUV),
            new(drawAreaBottomRight, spriteJob.Colors[2], bottomRightUV),
            new(drawAreaBottomLeft, spriteJob.Colors[3], bottomLeftUV),
        ]);
    }

    /// <summary>
    /// Draws a nine-sliced sprite: the four margins carve the sprite into corners, edges and a
    /// centre, and each is drawn to its own destination rather than the whole thing being scaled.
    /// Corners keep their exact pixel size, edges scale along one axis only, and the centre takes
    /// whatever is left -- so a UI box keeps crisp borders at any size, and a strip with a
    /// repeating region repeats instead of smearing.
    ///
    /// Everything below works in the sprite's own pixel space (0..W, 0..H) and converts to page UVs
    /// per slice, which keeps trimmed sprites honest: the page only holds the non-empty part, and
    /// TargetX/Y say where that sits inside the sprite.
    /// </summary>
    private static void DrawNineSlice(GMSpriteJob job, ImageResult pageTexture)
    {
        var slice = job.nineSlice!;
        var texture = job.texture;

        // Sprite-space size, and the size it is being drawn at.
        double spriteW = texture.BoundingWidth;
        double spriteH = texture.BoundingHeight;
        var scaledW = spriteW * job.scale.X;
        var scaledH = spriteH * job.scale.Y;

        // Margins can't overlap once the sprite is squeezed below their combined size; GM keeps the
        // corners and lets the middle vanish, so clamp rather than produce negative middles.
        var left = Math.Min(slice.Left, Math.Abs(scaledW) / 2);
        var right = Math.Min(slice.Right, Math.Abs(scaledW) / 2);
        var top = Math.Min(slice.Top, Math.Abs(scaledH) / 2);
        var bottom = Math.Min(slice.Bottom, Math.Abs(scaledH) / 2);

        // Column/row boundaries: in sprite space (source) and in drawn space (destination).
        Span<double> srcX = [0, slice.Left, spriteW - slice.Right, spriteW];
        Span<double> srcY = [0, slice.Top, spriteH - slice.Bottom, spriteH];
        Span<double> dstX = [0, left, Math.Abs(scaledW) - right, Math.Abs(scaledW)];
        Span<double> dstY = [0, top, Math.Abs(scaledH) - bottom, Math.Abs(scaledH)];

        // Where the sprite's top-left corner lands on screen, before per-slice offsets.
        var originLeft = job.screenPos.X - (job.origin.X * job.scale.X);
        var originTop = job.screenPos.Y - (job.origin.Y * job.scale.Y);

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var mode = SliceTileMode(slice, col, row);
                if (mode == (int)UndertaleSprite.NineSlice.TileMode.Hide)
                {
                    continue;
                }

                var sourceRect = (X: srcX[col], Y: srcY[row], W: srcX[col + 1] - srcX[col], H: srcY[row + 1] - srcY[row]);
                var destRect = (X: dstX[col], Y: dstY[row], W: dstX[col + 1] - dstX[col], H: dstY[row + 1] - dstY[row]);

                if (sourceRect.W <= 0 || sourceRect.H <= 0 || destRect.W <= 0 || destRect.H <= 0)
                {
                    continue;
                }

                // Repeat/Mirror tile the source across the destination; Stretch is one quad. A
                // corner is never tiled -- it is drawn at its own size, so its source and
                // destination match anyway and the loop below runs exactly once.
                var repeat = mode is (int)UndertaleSprite.NineSlice.TileMode.Repeat
                    or (int)UndertaleSprite.NineSlice.TileMode.Mirror
                    or (int)UndertaleSprite.NineSlice.TileMode.BlankRepeat;

                var stepW = repeat ? sourceRect.W : destRect.W;
                var stepH = repeat ? sourceRect.H : destRect.H;

                for (double y = 0; y < destRect.H; y += stepH)
                {
                    for (double x = 0; x < destRect.W; x += stepW)
                    {
                        // The last tile in a row/column usually only partly fits. Repeat clips it;
                        // BlankRepeat leaves it out entirely rather than drawing a partial copy.
                        var tileW = Math.Min(stepW, destRect.W - x);
                        var tileH = Math.Min(stepH, destRect.H - y);
                        if (mode == (int)UndertaleSprite.NineSlice.TileMode.BlankRepeat
                            && (tileW < stepW || tileH < stepH))
                        {
                            continue;
                        }

                        // Clipping the destination clips the source by the same fraction.
                        var srcW = sourceRect.W * (tileW / stepW);
                        var srcH = sourceRect.H * (tileH / stepH);

                        DrawSpriteSubRect(
                            job, pageTexture,
                            spriteSrc: (sourceRect.X, sourceRect.Y, srcW, srcH),
                            dest: (originLeft + ((destRect.X + x) * Math.Sign(job.scale.X == 0 ? 1 : job.scale.X)),
                                   originTop + ((destRect.Y + y) * Math.Sign(job.scale.Y == 0 ? 1 : job.scale.Y)),
                                   tileW * Math.Sign(job.scale.X == 0 ? 1 : job.scale.X),
                                   tileH * Math.Sign(job.scale.Y == 0 ? 1 : job.scale.Y)));
                    }
                }
            }
        }
    }

    /// <summary>GM's tile-mode order is left, top, right, bottom, centre.</summary>
    private static int SliceTileMode(NineSliceData slice, int col, int row)
    {
        if (col == 1 && row == 1)
        {
            return slice.TileModes[4];
        }

        // An edge takes its own mode; a corner is always drawn at its native size, so its mode only
        // decides whether it is drawn at all (Hide).
        if (row == 1)
        {
            return col == 0 ? slice.TileModes[0] : slice.TileModes[2];
        }
        if (col == 1)
        {
            return row == 0 ? slice.TileModes[1] : slice.TileModes[3];
        }

        return (int)UndertaleSprite.NineSlice.TileMode.Stretch;
    }

    /// <summary>
    /// One quad of a nine-sliced sprite: a rectangle of the sprite (in sprite pixel space) drawn to
    /// a rectangle on screen. Clips against the page item's own trimmed area, so an empty region of
    /// a trimmed sprite draws nothing instead of sampling its neighbour's pixels.
    /// </summary>
    private static void DrawSpriteSubRect(GMSpriteJob job, ImageResult pageTexture,
        (double X, double Y, double W, double H) spriteSrc,
        (double X, double Y, double W, double H) dest)
    {
        var texture = job.texture;

        // Intersect the requested sprite-space rect with the part of the sprite the page actually
        // holds (TargetX/Y/W/H), and carry the same clip over to the destination.
        var clipLeft = Math.Max(spriteSrc.X, texture.TargetX);
        var clipTop = Math.Max(spriteSrc.Y, texture.TargetY);
        var clipRight = Math.Min(spriteSrc.X + spriteSrc.W, texture.TargetX + texture.TargetWidth);
        var clipBottom = Math.Min(spriteSrc.Y + spriteSrc.H, texture.TargetY + texture.TargetHeight);
        if (clipRight <= clipLeft || clipBottom <= clipTop)
        {
            return;
        }

        var destX = dest.X + (dest.W * ((clipLeft - spriteSrc.X) / spriteSrc.W));
        var destY = dest.Y + (dest.H * ((clipTop - spriteSrc.Y) / spriteSrc.H));
        var destW = dest.W * ((clipRight - clipLeft) / spriteSrc.W);
        var destH = dest.H * ((clipBottom - clipTop) / spriteSrc.H);

        var uvLeft = (texture.SourceX + (clipLeft - texture.TargetX)) / pageTexture.Width;
        var uvTop = (texture.SourceY + (clipTop - texture.TargetY)) / pageTexture.Height;
        var uvRight = (texture.SourceX + (clipRight - texture.TargetX)) / pageTexture.Width;
        var uvBottom = (texture.SourceY + (clipBottom - texture.TargetY)) / pageTexture.Height;

        var topLeft = new Vector3d(destX, destY, GraphicsManager.GR_Depth).RotateAroundPoint(job.screenPos, job.angle);
        var topRight = new Vector3d(destX + destW, destY, GraphicsManager.GR_Depth).RotateAroundPoint(job.screenPos, job.angle);
        var bottomRight = new Vector3d(destX + destW, destY + destH, GraphicsManager.GR_Depth).RotateAroundPoint(job.screenPos, job.angle);
        var bottomLeft = new Vector3d(destX, destY + destH, GraphicsManager.GR_Depth).RotateAroundPoint(job.screenPos, job.angle);

        GraphicsManager.Draw(PrimitiveType.TriangleFan, [
            new(topLeft, job.Colors[0], new Vector2d(uvLeft, uvTop)),
            new(topRight, job.Colors[1], new Vector2d(uvRight, uvTop)),
            new(bottomRight, job.Colors[2], new Vector2d(uvRight, uvBottom)),
            new(bottomLeft, job.Colors[3], new Vector2d(uvLeft, uvBottom)),
        ]);
    }

    public static void Draw(GMSpritePartJob partJob)
    {

        var (pageTexture, id) = PageManager.TexturePages[partJob.texture.Page];
        GraphicsManager.BindTexture0(id);

        var left = (double)partJob.left;
        var top = (double)partJob.top;
        var width = (double)partJob.width;
        var height = (double)partJob.height;
        var x = partJob.screenPos.X;
        var y = partJob.screenPos.Y;
        var xscale = partJob.scale.X;
        var yscale = partJob.scale.Y;

        var sinAngle = Math.Sin(CustomMath.Deg2Rad * partJob.angle);
        var cosAngle = Math.Cos(CustomMath.Deg2Rad * partJob.angle);

        double xUVOffset;
        var fVar7 = (double)partJob.texture.TargetX;
        if (fVar7 <= left)
        {
            xUVOffset = left - fVar7;
        }
        else
        {
            fVar7 -= left;
            xUVOffset = 0.0f;
            width -= fVar7;
            x += fVar7 * cosAngle * xscale;
            y -= fVar7 * sinAngle * yscale;
        }

        double yUVOffset;
        fVar7 = partJob.texture.TargetY;
        if (fVar7 <= top)
        {
            yUVOffset = top - fVar7;
        }
        else
        {
            fVar7 -= top;
            yUVOffset = 0.0f;
            height -= fVar7;
            x += fVar7 * sinAngle * xscale;
            y += fVar7 * cosAngle * yscale;
        }

        if (partJob.texture.TargetWidth < xUVOffset + width)
        {
            width = partJob.texture.TargetWidth - xUVOffset;
        }

        if (partJob.texture.TargetHeight < yUVOffset + height)
        {
            height = partJob.texture.TargetHeight - yUVOffset;
        }

        if ((0.0 < width) && (0.0 < height))
        {
            var widthScale = partJob.texture.SourceWidth / partJob.texture.TargetWidth;
            var heightScale = partJob.texture.SourceHeight / partJob.texture.TargetHeight;

            var uvLeft = (partJob.texture.SourceX + xUVOffset) / pageTexture.Width;
            var uvTop = (partJob.texture.SourceY + yUVOffset) / pageTexture.Height;
            var uvRight = (partJob.texture.SourceX + xUVOffset + widthScale * width) / pageTexture.Width;
            var uvBottom = (partJob.texture.SourceY + yUVOffset + heightScale * height) / pageTexture.Height;
            var uv0 = new Vector2d(uvLeft, uvTop);
            var uv1 = new Vector2d(uvRight, uvTop);
            var uv2 = new Vector2d(uvRight, uvBottom);
            var uv3 = new Vector2d(uvLeft, uvBottom);

            var widthCos = width * xscale * cosAngle;
            var widthSin = -width * xscale * sinAngle;
            var heightCos = height * yscale * cosAngle;
            var heightSin = height * yscale * sinAngle;

            var bottomVector = new Vector3d(heightSin, heightCos, 0);

            var topLeft = new Vector3d(x, y, GraphicsManager.GR_Depth);
            var bottomLeft = topLeft + bottomVector;

            var topRight = topLeft + new Vector3d(widthCos, widthSin, 0);
            var bottomRight = topRight + bottomVector;

            GraphicsManager.Draw(PrimitiveType.TriangleFan, [
                new(topLeft, partJob.Colors[0], uv0),
                new(topRight, partJob.Colors[1], uv1),
                new(bottomRight, partJob.Colors[2], uv2),
                new(bottomLeft, partJob.Colors[3], uv3),
            ]);
        }

        /*var left = partJob.left;
        var top = partJob.top;
        var width = partJob.width;
        var height = partJob.height;
        var x = partJob.screenPos.X;
        var y = partJob.screenPos.Y;
        var xscale = partJob.scale.X;
        var yscale = partJob.scale.Y;
        var angle = partJob.angle;

        var _pTPE = partJob.texture;

        var sin = Math.Sin(angle * CustomMath.Deg2Rad);
        var cos = Math.Cos(angle * CustomMath.Deg2Rad);

        var xoffset = _pTPE.TargetX;
        var yoffset = _pTPE.TargetY;

        double adjustedLeft;
        double adjustedTop;
        var adjustedWidth = width;
        var adjustedHeight = height;
        var rotatedX = x;
        var rotatedY = y;

        if (xoffset <= left)
        {
            adjustedLeft = left - xoffset;
        }
        else
        {
            var off = xoffset - left;
            adjustedLeft = 0.0;
            adjustedWidth -= off;
            rotatedX = x + (off * cos) * xscale;
            rotatedY = y - (off * sin) * yscale;
        }

        if (yoffset <= top)
        {
            adjustedTop = top - yoffset;
        }
        else
        {
            var off = yoffset - top;
            adjustedTop = 0.0;
            adjustedHeight -= off;
            rotatedX += (off * sin) * xscale;
            rotatedY += (off * cos) * yscale;
        }

        var scaledw = Math.Min(_pTPE.TargetWidth, adjustedWidth) * xscale;
        var scaledh = Math.Min(_pTPE.TargetHeight, adjustedHeight) * yscale;

        var x1 = rotatedX;
        var y1 = rotatedY;
        double x2, y2, x3, y3, x4, y4;

        if (angle >= 0.0001)
        {
            x2 = (scaledw * cos) + rotatedX;
            y2 = rotatedY - (scaledw * sin);

            x3 = (scaledw * cos) + rotatedX + (scaledh * sin);
            y3 = (rotatedY - (scaledw * sin)) + (scaledh * cos);

            x4 = rotatedX + (scaledh * sin);
            y4 = rotatedY + (scaledh * cos);
        }
        else
        {
            x2 = scaledw + rotatedX;
            y2 = rotatedY;

            x3 = scaledw + rotatedX;
            y3 = scaledh + rotatedY;

            x4 = rotatedX;
            y4 = scaledh + rotatedY;
        }

        var uvW = 1.0 / pageTexture.Width;
        var uvH = 1.0 / pageTexture.Height;

        var u1 = uvW * (_pTPE.SourceX + adjustedLeft);
        var v1 = uvH * (_pTPE.SourceY + adjustedTop);

        var u2 = uvW * ((_pTPE.SourceX + adjustedLeft) + Math.Min(_pTPE.TargetWidth, adjustedWidth));
        var v2 = uvH * ((_pTPE.SourceY + adjustedTop) + Math.Min(_pTPE.TargetHeight, adjustedHeight));

        DrawTexture(
            x1, y1, x2, y2, x3, y3, x4, y4,
            u1, v1, u2, v2,
            partJob.Colors[0], partJob.Colors[1], partJob.Colors[2], partJob.Colors[3]);*/
    }

    public static void DrawTexture(
        double x1, double y1, double x2, double y2, double x3, double y3, double x4,
        double y4, double u1, double v1, double u2, double v2,
        Color4 c1, Color4 c2, Color4 c3, Color4 c4)
    {
        GraphicsManager.Draw(PrimitiveType.Triangles, [
            new(new(x1, y1, GraphicsManager.GR_Depth), c1, new(u1, v1)),
            new(new(x2, y2, GraphicsManager.GR_Depth), c2, new(u2, v1)),
            new(new(x3, y3, GraphicsManager.GR_Depth), c3, new(u2, v2)),
            new(new(x3, y3, GraphicsManager.GR_Depth), c3, new(u2, v2)),
            new(new(x4, y4, GraphicsManager.GR_Depth), c4, new(u1, v2)),
            new(new(x1, y1, GraphicsManager.GR_Depth), c1, new(u1, v1))
        ]);
    }

    public static void Draw(GMLineJob lineJob)
    {

        // platform pixel adjustment
        lineJob.x1 += 1;
        lineJob.y1 += 1;
        lineJob.x2 += 1;
        lineJob.y2 += 1;

        var width = lineJob.x2 - lineJob.x1;
        var height = lineJob.y2 - lineJob.y1;
        var length = MathF.Sqrt((width * width) + (height * height));

        width = lineJob.width * 0.5f * width / length;
        height = lineJob.width * 0.5f * height / length;

        GraphicsManager.BindTexture0(GraphicsManager.DefaultTexture);
        GraphicsManager.Draw(PrimitiveType.TriangleFan, [
            new(new(lineJob.x1 - height, lineJob.y1 + width, GraphicsManager.GR_Depth), lineJob.col1, Vector2.Zero),
            new(new(lineJob.x2 - height, lineJob.y2 + width, GraphicsManager.GR_Depth), lineJob.col2, Vector2.Zero),
            new(new(lineJob.x2 + height, lineJob.y2 - width, GraphicsManager.GR_Depth), lineJob.col2, Vector2.Zero),
            new(new(lineJob.x1 + height, lineJob.y1 - width, GraphicsManager.GR_Depth), lineJob.col1, Vector2.Zero),
        ]);
    }

    public static void Draw(GMLinesJob linesJob)
    {

        Span<GraphicsManager.Vertex> v = stackalloc GraphicsManager.Vertex[linesJob.Vertices.Length];
        for (var i = 0; i < linesJob.Vertices.Length; i++)
        {
            v[i] = new(linesJob.Vertices[i], linesJob.Colors[i], Vector2d.Zero);
        }

        GraphicsManager.BindTexture0(GraphicsManager.DefaultTexture);
        GraphicsManager.Draw(PrimitiveType.LineStrip, v);
    }

    public static void Draw(GMPolygonJob polyJob)
    {

        Span<GraphicsManager.Vertex> v = stackalloc GraphicsManager.Vertex[polyJob.Vertices.Length];
        for (var i = 0; i < polyJob.Vertices.Length; i++)
        {
            v[i] = new(polyJob.Vertices[i], polyJob.Colors[i], Vector2d.Zero);
        }

        if (!polyJob.Outline)
        {
            // Filled polygons are untextured and all use the same state, so they queue up and go out
            // together -- see GraphicsManager.BatchConvexPolygon for why that matters so much here.
            // Outlines are LineLoops, which don't concatenate, so they stay one draw call each.
            GraphicsManager.BatchConvexPolygon(v);
            return;
        }

        GraphicsManager.BindTexture0(GraphicsManager.DefaultTexture);
        // guessing polygon works with triangle fan since quad worked with that and polygons must be convex i think
        GraphicsManager.Draw(PrimitiveType.LineLoop, v);
    }

    public static void Draw(GMTexturedPolygonJob texPolyJob)
    {

        var (pageTexture, id) = PageManager.TexturePages[texPolyJob.Texture.Page];
        GraphicsManager.BindTexture0(id);

        Span<GraphicsManager.Vertex> vArr = stackalloc GraphicsManager.Vertex[texPolyJob.Vertices.Length];
        for (var i = 0; i < texPolyJob.Vertices.Length; i++)
        {
            vArr[i] = new GraphicsManager.Vertex(texPolyJob.Vertices[i], texPolyJob.Colors[i], texPolyJob.UVs[i]);
        }
        GraphicsManager.Draw(PrimitiveType.TriangleFan, vArr);
    }
}

public class GMLineJob : GMBaseJob
{
    public required float x1;
    public required float y1;
    public required float x2;
    public required float y2;
    public required float width;
    public required Color4 col1;
    public required Color4 col2;
}

public class GMLinesJob : GMBaseJob
{
    public required Vector3d[] Vertices;
    public required Color4[] Colors;
}

public class GMSpriteJob : GMBaseJob
{
    public required Vector2d screenPos;
    public required SpritePageItem texture;
    public required Vector2d scale;
    public required double angle;
    public required Vector2 origin;
    public required Color4[] Colors;

    /// <summary>
    /// The sprite's nine-slice setup, when it has one and the caller knows which sprite this page
    /// item came from. Null everywhere else, which is the overwhelming majority of draws.
    /// </summary>
    public NineSliceData? nineSlice;
}

public class GMSpritePartJob : GMSpriteJob
{
    public required double left;
    public required double top;
    public required double width;
    public required double height;
}

public class GMTextJob : GMBaseJob
{
    public required Vector2d screenPos;
    public required string text;
    public required Vector2d scale;
    public required HAlign halign;
    public required VAlign valign;
    public required double angle;
    public required FontAsset asset;
    public required float lineSep;
    public required Color4[] Colors;

    /// <summary>
    /// draw_text_ext's max line width in pixels, wrapped at draw time so it happens after (and
    /// only after) <see cref="CompatFlags.ApplyHashNewlines"/>. Negative means no wrapping.
    /// </summary>
    public int wrapWidth = -1;
}

public class GMPolygonJob : GMBaseJob
{
    public required Vector3d[] Vertices;
    public required Color4[] Colors;
    public required bool Outline;
}

public class GMTexturedPolygonJob : GMBaseJob
{
    public required Vector3d[] Vertices;
    public required Vector2d[] UVs;
    public required Color4[] Colors;
    public required SpritePageItem Texture;
}

public abstract class GMBaseJob;
