using OpenGM.IO;
using OpenGM.Rendering;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenGM;

/// <summary>
/// handles drawing using modern opengl
/// </summary>
public static class GraphicsManager
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Vertex(Vector3d pos, Color4 color, Vector2d uv)
    {
        [FieldOffset(0 * sizeof(float))] public Vector3 pos = (Vector3)pos;
        [FieldOffset(3 * sizeof(float))] public Vector4 color = (Vector4)color;
        [FieldOffset((3 + 4) * sizeof(float))] public Vector2 uv = (Vector2)uv;
    }

    /// <summary>
    /// 1x1 white image is used when for things that dont need textures
    /// </summary>
    public static int DefaultTexture;

    public static double GR_Depth;
    public static bool ForceDepth;
    public static double ForcedDepth;

    public static bool EnableCulling = true;

    /// <summary>
    /// Whether <see cref="Init"/> has run, i.e. whether there is a GL context and a shader to
    /// upload uniforms to. <see cref="UploadMatrices"/> can be reached from GML before that (and
    /// from unit tests, which have no context at all).
    /// </summary>
    private static bool _initialised;

    /// <summary>
    /// setup shader and buffer
    /// </summary>
    public static void Init()
    {
        GL.CreateTextures(TextureTarget.Texture2D, 1, out DefaultTexture);
        LabelObject(ObjectLabelIdentifier.Texture, DefaultTexture, nameof(DefaultTexture));
        GL.TextureStorage2D(DefaultTexture, 1, SizedInternalFormat.Rgba8, 1, 1);
        GL.TextureSubImage2D(DefaultTexture, 0, 0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 255, 255, 255 });
        GL.TextureParameter(DefaultTexture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TextureParameter(DefaultTexture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        // use one buffer for everything
        // could replace this with dsa version, but for now we always bind one vao and vbo so theres no point
        var vao = GL.GenVertexArray();
        var vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        LabelObject(ObjectLabelIdentifier.VertexArray, vao, "main vao");
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        LabelObject(ObjectLabelIdentifier.Buffer, vbo, "main vbo");

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 0 * sizeof(float));
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), (3 + 4) * sizeof(float));
        GL.EnableVertexAttribArray(2);

        _initialised = true;
    }

    /// <summary>
    /// Room-space bounds of everything the given view area can show. A rotated view still shows
    /// everything inside the *rotated* rectangle, whose axis-aligned bounds are bigger than the view
    /// rect itself -- culling against the unrotated rect would wrongly discard geometry that's still
    /// on screen near the corners.
    /// </summary>
    public static (float Left, float Top, float Right, float Bottom) GetCullBounds(Vector4 viewArea, float angle)
    {
        if (angle == 0)
        {
            return (viewArea.X, viewArea.Y, viewArea.X + viewArea.Z, viewArea.Y + viewArea.W);
        }

        var rad = angle * CustomMath.Deg2Rad;
        var absCos = (float)Math.Abs(Math.Cos(rad));
        var absSin = (float)Math.Abs(Math.Sin(rad));
        var halfW = ((viewArea.Z * absCos) + (viewArea.W * absSin)) / 2;
        var halfH = ((viewArea.Z * absSin) + (viewArea.W * absCos)) / 2;
        var centerX = viewArea.X + (viewArea.Z / 2);
        var centerY = viewArea.Y + (viewArea.W / 2);

        return (centerX - halfW, centerY - halfH, centerX + halfW, centerY + halfH);
    }

    /// <summary>
    /// Pending untextured filled convex polygons, expanded to a triangle list so that any number of
    /// them can go out in a single draw call. See <see cref="BatchConvexPolygon"/>.
    /// </summary>
    private static readonly List<Vertex> _polygonBatch = new(4096);

    /// <summary>
    /// Queues one untextured filled convex polygon, given as a triangle FAN (the layout
    /// draw_circle/draw_ellipse/draw_triangle/... already build), for drawing later alongside
    /// whatever else is queued.
    ///
    /// Why this exists: under browser-wasm every GL.* call is a [JSImport] crossing into JS/WebGL2,
    /// far pricier than a native GL call, and <see cref="Draw"/> issues two of them per call with a
    /// texture bind on top. GML's idiomatic radial gradient is a stack of ~50 concentric
    /// draw_circles PER LIGHT, every frame -- I Wanna Spook Jam's RandomFangamer mansion draws its
    /// darkness overlay with 15 busted bulbs plus its lit lamps, which is ~1000 draw_circle calls
    /// and so ~3000 boundary crossings in a single frame. Native GameMaker shrugs that off; the
    /// browser build cannot. Batched, the whole overlay is one crossing's worth of geometry.
    ///
    /// A fan can't be concatenated with another fan, so each polygon is expanded into independent
    /// triangles (n-2 of them). That's ~3x the vertex data for a 24-gon circle, paid once as a
    /// memcpy into the buffer, against ~1000x fewer boundary crossings.
    /// </summary>
    public static void BatchConvexPolygon(ReadOnlySpan<Vertex> fan)
    {
        if (fan.Length < 3)
        {
            return;
        }

        for (var i = 1; i < fan.Length - 1; i++)
        {
            _polygonBatch.Add(fan[0]);
            _polygonBatch.Add(fan[i]);
            _polygonBatch.Add(fan[i + 1]);
        }
    }

    /// <summary>How many vertices are queued but not yet drawn. For tests.</summary>
    public static int PendingPolygonVertexCount => _polygonBatch.Count;

    /// <summary>The queued vertices, in draw order. For tests.</summary>
    public static ReadOnlySpan<Vertex> PeekPolygonBatch() => CollectionsMarshal.AsSpan(_polygonBatch);

    /// <summary>Throws the queue away without drawing it -- for tests, which have no GL context.</summary>
    public static void DiscardPolygonBatch() => _polygonBatch.Clear();

    /// <summary>
    /// Issues everything <see cref="BatchConvexPolygon"/> has queued, if anything. MUST be called
    /// before any GL state the queued geometry depends on changes (blend mode, shader, render
    /// target, the view/projection matrices), before anything reads pixels back, and before the
    /// frame ends -- otherwise the queued polygons get drawn under the wrong state, or not at all.
    /// <see cref="Draw"/> calls it for every non-batched draw, which covers the ordering against
    /// other geometry; the rest is up to the state-changing call sites themselves.
    /// </summary>
    public static void FlushPolygonBatch()
    {
        if (_polygonBatch.Count == 0)
        {
            return;
        }

        var vertices = CollectionsMarshal.AsSpan(_polygonBatch);

        // A flush can land in the middle of someone else's "bind my texture, then draw" pair --
        // Draw() below calls this AFTER its caller has already bound. Put the binding back so the
        // draw that triggered the flush still gets the texture it asked for.
        var previous = _boundTexture0;

        // Deliberately not via Draw(): that flushes this batch first, which would recurse. Culling
        // is skipped too -- a batch spanning the whole screen is never wholly off it, so the test
        // would only ever cost time.
        GL.BindTextureUnit(0, DefaultTexture);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Unsafe.SizeOf<Vertex>(), ref vertices.GetPinnableReference(), BufferUsageHint.StreamDraw);
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertices.Length);

        _polygonBatch.Clear();

        if (previous >= 0 && previous != DefaultTexture)
        {
            GL.BindTextureUnit(0, previous);
        }
        else
        {
            _boundTexture0 = DefaultTexture;
        }
    }

    /// <summary>
    /// What's currently bound to texture unit 0, or -1 if nothing has been bound yet. Only tracked
    /// for unit 0 -- shaders bind their extra samplers to higher stages, which no draw here relies on.
    /// </summary>
    private static int _boundTexture0 = -1;

    /// <summary>
    /// Binds a texture to unit 0. Everything that draws should use this rather than GL.BindTextureUnit
    /// directly, so <see cref="FlushPolygonBatch"/> knows what to restore after it borrows the unit
    /// for the white default texture.
    /// </summary>
    public static void BindTexture0(int id)
    {
        GL.BindTextureUnit(0, id);
        _boundTexture0 = id;
    }

    /// <summary>
    /// Whether a draw can be skipped because none of it can land on screen.
    ///
    /// The test compares vertex positions straight against the view area, so it is only meaningful
    /// while those positions ARE view-space positions -- that is, while the world matrix is
    /// identity. A game drawing through one has put its geometry somewhere else entirely, and
    /// testing the untransformed coordinates throws away draws that do land on screen.
    ///
    /// tijit engine 2 renders its tile layer into a camera-sized surface under
    /// matrix_build(-x, -y, ...). On the first screenful, room and surface coordinates coincide and
    /// everything passed; once the camera scrolled right, every sprite drawn that way sat past the
    /// surface's right edge and was culled, so its moving blocks went invisible from the second
    /// screen on -- the player standing on nothing, a gravity switch left uncovered -- while the
    /// static tile mesh (which passes cull: false) stayed put.
    /// </summary>
    public static bool ShouldCull(ReadOnlySpan<Vertex> vertices)
        => ShouldCull(vertices, GetCullBounds(ViewArea, ViewAreaAngle));

    /// <summary>
    /// <inheritdoc cref="ShouldCull(ReadOnlySpan{Vertex})"/>
    /// </summary>
    /// <param name="bounds">The view's room-space bounds, so this can be exercised without a view.</param>
    public static bool ShouldCull(ReadOnlySpan<Vertex> vertices, (float Left, float Top, float Right, float Bottom) bounds)
    {
        if (!EnableCulling || _3DFunctions.WorldMatrixField != Matrix4.Identity)
        {
            return false;
        }

        var (screenLeft, screenTop, screenRight, screenBottom) = bounds;

        var allPastLeft = true;
        var allPastRight = true;
        var allPastTop = true;
        var allPastBottom = true;

        foreach (var vert in vertices)
        {
            if (vert.pos.X > screenLeft)
            {
                allPastLeft = false;
            }
            if (vert.pos.X < screenRight)
            {
                allPastRight = false;
            }
            if (vert.pos.Y > screenTop)
            {
                allPastTop = false;
            }
            if (vert.pos.Y < screenBottom)
            {
                allPastBottom = false;
            }
        }

        return allPastLeft || allPastRight || allPastTop || allPastBottom;
    }

    /// <summary>
    /// GML's pr_* primitive kind as the matching GL enum. Not a straight cast: GML has no LineLoop,
    /// so the two numberings diverge after pr_linestrip.
    /// </summary>
    public static PrimitiveType ToPrimitiveType(int kind) => kind switch
    {
        1 => PrimitiveType.Points,
        2 => PrimitiveType.Lines,
        3 => PrimitiveType.LineStrip,
        4 => PrimitiveType.Triangles,
        5 => PrimitiveType.TriangleStrip,
        6 => PrimitiveType.TriangleFan,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"Unknown primitive kind {kind}"),
    };

    /// <summary>
    /// draw some vertices
    /// </summary>
    /// <param name="cull">
    /// Whether to drop the draw when every vertex is off-view. Only valid when the vertices are in
    /// room space, which is what everything built by the draw_* / d3d_* families is. A vertex buffer
    /// is NOT: it holds model-space geometry that the world matrix places, so its coordinates say
    /// nothing about where it lands on screen -- see VertexBufferFunctions.vertex_submit.
    /// </param>
    public static void Draw(PrimitiveType primitiveType, Span<Vertex> vertices, bool cull = true)
    {
        // Anything drawn the normal way has to appear ON TOP of polygons queued before it, and
        // under whatever texture/state it is about to set for itself -- so the queue goes out first.
        FlushPolygonBatch();

        if (cull && ShouldCull(vertices))
        {
            return;
        }

        // Every draw call here is its own GL.BufferData + GL.DrawArrays pair (no batching) --
        // under browser-wasm each GL.* call is a [JSImport] crossing into JS/WebGL2, which costs
        // meaningfully more than a native GL call. A game issuing many small draw calls per frame
        // (e.g. one per sprite/tile, no batching) can be fine natively but slow specifically in
        // the browser build purely from interop overhead.
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Unsafe.SizeOf<Vertex>(), ref vertices.GetPinnableReference(), BufferUsageHint.StreamDraw);
        GL.DrawArrays(primitiveType, 0, vertices.Length);
    }

    public static Vector4i ViewPort { get; private set; }
    public static void SetViewPort(Vector4i viewPort) => SetViewPort(viewPort.X, viewPort.Y, viewPort.Z, viewPort.W);
    public static void SetViewPort(int x, int y, int w, int h)
    {
        FlushPolygonBatch(); // queued geometry belongs to the OLD viewport
        PushMessage($"SetViewPort {x} {y} {w} {h}");
        ViewPort = new(x, y, w, h);
        GL.Viewport(x, y, w, h);
        PopMessage();
    }

    // rn we're just replicating g_isZeus = false, where instead of doing camera stuff we just have view area globals
    public static Vector4 ViewArea { get; private set; }

    /// <summary>
    /// Rotation (degrees) of the current view area, from view_angle / camera_set_view_angle.
    /// Part of the view state, so anything that saves and restores a view area (the surface stack,
    /// the shader-change matrix reload) has to carry it too, or setting a shader mid-frame would
    /// silently un-rotate the view.
    /// </summary>
    public static float ViewAreaAngle { get; private set; }

    /// <summary>
    /// The view/projection pair for a view area. Split out of <see cref="SetViewArea"/> so the
    /// angle-sensitive maths can be exercised without a GL context.
    /// </summary>
    public static (Matrix4 View, Matrix4 Projection) BuildViewMatrices(float x, float y, float w, float h, float angle, bool flipY)
    {
        // literally just copy from camera building and applying matrices

        x = x + (w / 2);
        y = y + (h / 2);

        var pos = new Vector3(x, y, -16000);
        var at = new Vector3(x, y, 16000);
        var up = new Vector3((float)Math.Sin(-angle * CustomMath.Deg2Rad), (float)Math.Cos(-angle * CustomMath.Deg2Rad),
            0);

        var view = Matrix4.LookAt(at, pos, up);
        var proj = Matrix4.CreateOrthographic(w, h, 0, 32000);

        if (flipY)
        {
            // flip when drawing to backbuffer
            var flipMat = Matrix4.Identity;
            flipMat[1, 1] = -1;

            proj = proj * flipMat; // TODO right way round?
        }

        return (view, proj);
    }

    public static void SetViewArea(Vector4 viewArea, float angle) => SetViewArea(viewArea.X, viewArea.Y, viewArea.Z, viewArea.W, angle);
    public static void SetViewArea(float x, float y, float w, float h, float angle)
    {
        FlushPolygonBatch(); // queued geometry belongs to the OLD view/projection matrices
        PushMessage($"SetViewArea {x} {y} {w} {h} {angle}");
        ViewArea = new(x, y, w, h);
        ViewAreaAngle = angle;

        // no flip when drawing to non-backbuffer
        var (view, proj) = BuildViewMatrices(x, y, w, h, angle, SurfaceManager.DrawingToBackbuffer());

        // Straight to the backing fields: setting the properties would upload once each, and the
        // upload below covers both. A view being applied is also GM's own reset point for these --
        // a game that wants its own projection sets it again every frame.
        _3DFunctions.ViewMatrixField = view;
        _3DFunctions.ProjectionMatrixField = proj;
        UploadMatrices();
        PopMessage();
    }

    /// <summary>
    /// Pushes the view/projection/world matrices into the shader's gm_Matrices array.
    ///
    /// This is the ONLY thing that uploads them, and it has to run whenever any of the three
    /// changes -- which is why <see cref="_3DFunctions"/> exposes them as properties that call
    /// here. Before, the world matrix was hardcoded to identity at the one upload site (a view
    /// change), and the only code that read the real one was ShaderManager.LoadMatrices, which
    /// nothing ever called: so matrix_set(matrix_world, ...) and the whole d3d_transform_* family
    /// silently did nothing to what was drawn.
    ///
    /// tijit engine 2 draws its world mesh into a camera-sized surface under
    /// matrix_build(-x, -y, ...) and composites that surface back at (x, y). With the world matrix
    /// dropped, the mesh landed at raw room coordinates inside the surface, so its drop shadows sat
    /// ~30px off (the camera object's position, less the intended 2px offset) and geometry past the
    /// surface's edge was clipped away entirely.
    /// </summary>
    public static void UploadMatrices()
    {
        if (!_initialised)
        {
            // No GL context yet (or a unit test) -- SetViewArea will upload once there is one.
            return;
        }

        FlushPolygonBatch(); // queued geometry belongs to the OLD matrices

        var view = _3DFunctions.ViewMatrixField;
        var proj = _3DFunctions.ProjectionMatrixField;
        var world = _3DFunctions.WorldMatrixField;
        var worldView = world * view;
        var worldviewProjection = worldView * proj;

        var matrices = new Matrix4[] { view, proj, world, worldView, worldviewProjection };

        unsafe
        {
            fixed (Matrix4* ptr = &matrices[0])
            {
                GL.UniformMatrix4(ShaderManager.gm_Matrices, matrices.Length, false, (float*)ptr);
            }
        }
    }

    public static void SetFog(bool enable, Color4 color, float start, float end)
    {
        PushMessage($"SetFog Enable:{enable}, Color:{color}, Start:{start}, End:{end}");
        GL.Uniform1(ShaderManager.gm_FogStart, start);

        var range = end - start;
        var rcpRange = range == 0 ? 0 : 1 / range;
        GL.Uniform1(ShaderManager.gm_RcpFogRange, rcpRange);

        GL.Uniform1(ShaderManager.gm_PS_FogEnabled, enable ? 1 : 0);
        GL.Uniform4(ShaderManager.gm_FogColour, color);
        GL.Uniform1(ShaderManager.gm_VS_FogEnabled, enable ? 1 : 0);
        PopMessage();
    }

    [Conditional("DEBUG_EXTRA")]
    public static void PushMessage(string message) => GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, message.Length, message);
    [Conditional("DEBUG_EXTRA")]
    public static void PopMessage() => GL.PopDebugGroup(); // TODO: check that were popping what we pushed

    [Conditional("DEBUG_EXTRA")]
    public static void LabelObject(ObjectLabelIdentifier id, int obj, string label) => GL.ObjectLabel(id, obj, label.Length, label);
}
