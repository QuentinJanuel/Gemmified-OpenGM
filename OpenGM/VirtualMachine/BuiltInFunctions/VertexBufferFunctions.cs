using OpenGM.IO;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// GameMaker Studio's vertex buffer API: build a mesh once with vertex_begin / vertex_position /
/// vertex_texcoord / vertex_colour / vertex_end, then draw the whole thing with one vertex_submit.
/// This is how a GMS game draws geometry it wants to keep -- a room's worth of tiles as a single
/// mesh, rather than one draw call per tile -- and a game built around it draws nothing at all
/// without it, since a failed call takes the whole event down with it.
///
/// Like <see cref="D3DGeometryFunctions"/>, this builds on what already exists rather than adding a
/// second renderer: a buffer is a list of the same <see cref="GraphicsManager.Vertex"/> that
/// draw_primitive_* and d3d_vertex_* fill, and vertex_submit is <see cref="GraphicsManager.Draw"/>
/// with the given texture bound. So it inherits the current shader, the matrices and the depth
/// state for free.
///
/// <b>Vertices are stored decoded, not as GM's raw attribute bytes.</b> Real GM appends bytes at a
/// write cursor and divides by the format's stride to count vertices; here each vertex_* call fills
/// its slot of the vertex under construction and advances an element cursor, which completes a
/// vertex once every element of the format has been written. Same result for the well-formed case
/// (attributes written in the order the format declares them, which is the only case that draws
/// correctly on real GM either), and it means a buffer submitted every frame needs no re-decoding.
///
/// <b>Texture coordinates are raw page coordinates.</b> Unlike draw_vertex_texture, whose 0..1 is
/// mapped into the sprite's own sub-rect (GraphicFunctions.PrimitiveUV), a vertex buffer's uv goes
/// to the GPU untouched: in GMS a "texture" IS the whole packed page, which is exactly why
/// sprite_get_uvs exists and why games call it when filling a vertex buffer. Remapping here would
/// double-apply the sub-rect for every game that does the normal thing.
/// </summary>
public static class VertexBufferFunctions
{
    public class VertexBuffer
    {
        /// <summary>The format handed to vertex_begin; null before the first one.</summary>
        public VertexFormat? Format;

        public readonly List<GraphicsManager.Vertex> Vertices = new();

        /// <summary>True between vertex_begin and vertex_end.</summary>
        public bool Building;

        /// <summary>
        /// Which element of <see cref="Format"/> the next vertex_* call fills. Wrapping back to 0
        /// is what completes a vertex.
        /// </summary>
        public int ElementCursor;

        // The vertex under construction. Defaults matter: a format that declares an attribute the
        // game never writes leaves it at these values.
        public Vector3d Position;
        public Color4 Colour = Color4.White;
        public Vector2d Uv;

        public void ResetVertex()
        {
            Position = default;
            Colour = Color4.White;
            Uv = default;
        }
    }

    private static readonly Dictionary<int, VertexBuffer> _buffers = new();

    /// <summary>
    /// Ids start at 1 for the same reason vertex formats do -- 0 is false in GML.
    /// </summary>
    private static int _nextBufferId = 1;

    /// <summary>The vertices a buffer holds, in submission order. For tests.</summary>
    public static ReadOnlySpan<GraphicsManager.Vertex> PeekBuffer(int id)
        => _buffers.TryGetValue(id, out var buffer)
            ? CollectionsMarshal.AsSpan(buffer.Vertices)
            : ReadOnlySpan<GraphicsManager.Vertex>.Empty;

    /// <summary>Throws every buffer away, so one test's ids can't reach another's. For tests.</summary>
    public static void DiscardBuffers()
    {
        _buffers.Clear();
        _nextBufferId = 1;
    }

    [GMLFunction("vertex_create_buffer")]
    public static object? vertex_create_buffer(object?[] args)
    {
        var id = _nextBufferId++;
        _buffers[id] = new VertexBuffer();
        return id;
    }

    /// <summary>
    /// The _ext form only pre-sizes GM's allocation. A List grows on its own, so the size is
    /// genuinely nothing to honour here rather than something being ignored.
    /// </summary>
    [GMLFunction("vertex_create_buffer_ext")]
    public static object? vertex_create_buffer_ext(object?[] args) => vertex_create_buffer(args);

    [GMLFunction("vertex_delete_buffer")]
    public static object? vertex_delete_buffer(object?[] args)
    {
        _buffers.Remove(args[0].Conv<int>());
        return null;
    }

    [GMLFunction("vertex_begin")]
    public static object? vertex_begin(object?[] args)
    {
        var buffer = Get(args[0], nameof(vertex_begin));
        if (buffer == null)
        {
            return null;
        }

        var formatId = args[1].Conv<int>();
        if (!VertexFormatFunctions.Formats.TryGetValue(formatId, out var format))
        {
            DebugLog.LogWarning($"vertex_begin with unknown vertex format {formatId} - buffer will stay empty.");
            return null;
        }

        // vertex_begin starts the buffer over; a game rebuilding its world mesh on room start calls
        // it again on the same buffer every time.
        buffer.Format = format;
        buffer.Vertices.Clear();
        buffer.Building = true;
        buffer.ElementCursor = 0;
        buffer.ResetVertex();
        return null;
    }

    [GMLFunction("vertex_end")]
    public static object? vertex_end(object?[] args)
    {
        var buffer = Get(args[0], nameof(vertex_end));
        if (buffer == null)
        {
            return null;
        }

        if (buffer.ElementCursor != 0)
        {
            // Real GM would keep the half-written vertex's bytes and mis-align every vertex after
            // it; dropping it is the same amount of wrong with a name attached.
            DebugLog.LogWarning($"vertex_end with a partially written vertex ({buffer.ElementCursor} of " +
                                $"{buffer.Format?.Format.Count} attributes) - dropping it.");
            buffer.ElementCursor = 0;
            buffer.ResetVertex();
        }

        buffer.Building = false;
        return null;
    }

    [GMLFunction("vertex_position")]
    public static object? vertex_position(object?[] args)
    {
        var buffer = GetBuilding(args[0], nameof(vertex_position));
        if (buffer == null)
        {
            return null;
        }

        // z stays 0, which is what GM writes for a 2D position -- NOT GraphicsManager.GR_Depth the
        // way draw_vertex uses. A vertex buffer is submitted whole, long after the instance whose
        // depth GR_Depth happens to hold at that moment.
        buffer.Position = new Vector3d(args[1].Conv<double>(), args[2].Conv<double>(), 0);
        Advance(buffer, VertexUsage.POSITION, nameof(vertex_position));
        return null;
    }

    [GMLFunction("vertex_position_3d")]
    public static object? vertex_position_3d(object?[] args)
    {
        var buffer = GetBuilding(args[0], nameof(vertex_position_3d));
        if (buffer == null)
        {
            return null;
        }

        buffer.Position = new Vector3d(args[1].Conv<double>(), args[2].Conv<double>(), args[3].Conv<double>());
        Advance(buffer, VertexUsage.POSITION, nameof(vertex_position_3d));
        return null;
    }

    [GMLFunction("vertex_color")]
    [GMLFunction("vertex_colour")]
    public static object? vertex_colour(object?[] args)
    {
        var buffer = GetBuilding(args[0], nameof(vertex_colour));
        if (buffer == null)
        {
            return null;
        }

        buffer.Colour = args[1].Conv<int>().ABGRToCol4(args[2].Conv<double>());
        Advance(buffer, VertexUsage.COLOR, nameof(vertex_colour));
        return null;
    }

    [GMLFunction("vertex_argb")]
    public static object? vertex_argb(object?[] args)
    {
        var buffer = GetBuilding(args[0], nameof(vertex_argb));
        if (buffer == null)
        {
            return null;
        }

        // One packed 0xAARRGGBB, rather than the colour-plus-alpha pair vertex_colour takes.
        var argb = (uint)args[1].Conv<int>();
        buffer.Colour = new Color4(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f,
            ((argb >> 24) & 0xFF) / 255f);
        Advance(buffer, VertexUsage.COLOR, nameof(vertex_argb));
        return null;
    }

    [GMLFunction("vertex_texcoord")]
    public static object? vertex_texcoord(object?[] args)
    {
        var buffer = GetBuilding(args[0], nameof(vertex_texcoord));
        if (buffer == null)
        {
            return null;
        }

        buffer.Uv = new Vector2d(args[1].Conv<double>(), args[2].Conv<double>());
        Advance(buffer, VertexUsage.TEXCOORD, nameof(vertex_texcoord));
        return null;
    }

    /// <summary>
    /// Accepted and counted so a format declaring a normal still completes its vertices, but the
    /// value goes nowhere: the shader has no light model (the same gap d3d_light_* documents), so a
    /// normal could not change a pixel even if it were passed through.
    /// </summary>
    [GMLFunction("vertex_normal")]
    public static object? vertex_normal(object?[] args)
    {
        var buffer = GetBuilding(args[0], nameof(vertex_normal));
        if (buffer == null)
        {
            return null;
        }

        Advance(buffer, VertexUsage.NORMAL, nameof(vertex_normal));
        return null;
    }

    [GMLFunction("vertex_submit")]
    public static object? vertex_submit(object?[] args)
    {
        return Submit(args, 0, -1);
    }

    /// <summary>Draws part of a buffer: vertex_submit_ext(buffer, prim, texture, offset, count).</summary>
    [GMLFunction("vertex_submit_ext")]
    public static object? vertex_submit_ext(object?[] args)
    {
        return Submit(args, args[3].Conv<int>(), args[4].Conv<int>());
    }

    private static object? Submit(object?[] args, int offset, int count)
    {
        var buffer = Get(args[0], nameof(vertex_submit));
        if (buffer == null)
        {
            return null;
        }

        if (buffer.Building)
        {
            DebugLog.LogWarning("vertex_submit on a buffer that is still between vertex_begin and vertex_end.");
        }

        if (buffer.Vertices.Count == 0)
        {
            return null;
        }

        offset = Math.Clamp(offset, 0, buffer.Vertices.Count);
        var available = buffer.Vertices.Count - offset;
        count = count < 0 ? available : Math.Min(count, available);
        if (count == 0)
        {
            return null;
        }

        BindTexture(args[2]);

        var vertices = CollectionsMarshal.AsSpan(buffer.Vertices).Slice(offset, count);
        // cull: false -- these coordinates are model space, and the world matrix is what decides
        // where they land. Culling them against the room-space view would throw away geometry a
        // matrix_set(matrix_world, ...) was about to bring on screen.
        GraphicsManager.Draw(GraphicsManager.ToPrimitiveType(args[1].Conv<int>()), vertices, cull: false);
        return null;
    }

    /// <summary>
    /// GM's vertex_freeze moves the buffer to GPU memory and makes it read-only. Every draw path
    /// here re-uploads its vertices per call anyway (see GraphicsManager.Draw), so there is nothing
    /// to freeze -- and the buffer deliberately stays writable rather than pretending otherwise.
    /// </summary>
    [GMLFunction("vertex_freeze")]
    public static object? vertex_freeze(object?[] args) => null;

    [GMLFunction("vertex_get_number")]
    public static object? vertex_get_number(object?[] args)
    {
        var buffer = Get(args[0], nameof(vertex_get_number));
        return buffer == null ? 0 : buffer.Vertices.Count;
    }

    [GMLFunction("vertex_get_buffer_size")]
    public static object? vertex_get_buffer_size(object?[] args)
    {
        var buffer = Get(args[0], nameof(vertex_get_buffer_size));
        return buffer?.Format == null ? 0 : buffer.Vertices.Count * (int)buffer.Format.ByteSize;
    }

    /// <summary>
    /// Binds whatever vertex_submit was handed: a sprite/background's page, a surface's texture, or
    /// the 1x1 white default for a buffer submitted without one. Mirrors texture_set_stage, which
    /// takes the same two kinds of "texture pointer".
    /// </summary>
    private static void BindTexture(object? texture)
    {
        switch (texture)
        {
            case SurfaceTexture surface:
                GraphicsManager.BindTexture0(surface.GLHandle);
                return;
            case SpritePageItem page when PageManager.TexturePages.TryGetValue(page.Page, out var texturePage):
                GraphicsManager.BindTexture0(texturePage.id);
                return;
            default:
                // Same reasoning as draw_primitive_end: leaving the previous draw's texture bound
                // would multiply this geometry by whatever it happened to sample, transparent
                // included, which reads as "my mesh silently vanished".
                GraphicsManager.BindTexture0(GraphicsManager.DefaultTexture);
                return;
        }
    }

    private static VertexBuffer? Get(object? id, string caller)
    {
        var bufferId = id.Conv<int>();
        if (_buffers.TryGetValue(bufferId, out var buffer))
        {
            return buffer;
        }

        DebugLog.LogWarning($"{caller} on nonexistent vertex buffer {bufferId}.");
        return null;
    }

    private static VertexBuffer? GetBuilding(object? id, string caller)
    {
        var buffer = Get(id, caller);
        if (buffer == null)
        {
            return null;
        }

        if (!buffer.Building || buffer.Format == null)
        {
            DebugLog.LogWarning($"{caller} outside vertex_begin/vertex_end - ignoring.");
            return null;
        }

        return buffer;
    }

    /// <summary>
    /// Moves to the next attribute of the format, completing the vertex when the last one is
    /// filled. <paramref name="wrote"/> is what the caller actually supplied: if the format expects
    /// something else at this position the data is wrong (on real GM too, which would write these
    /// bytes into the next attribute's slot), so it is worth naming rather than passing silently.
    /// </summary>
    private static void Advance(VertexBuffer buffer, VertexUsage wrote, string caller)
    {
        var elements = buffer.Format!.Format;
        if (elements.Count == 0)
        {
            return;
        }

        var expected = elements[buffer.ElementCursor].Usage;
        if (expected != wrote)
        {
            DebugLog.LogWarning($"{caller} wrote {wrote} where the vertex format's attribute " +
                                $"{buffer.ElementCursor} is {expected}.");
        }

        buffer.ElementCursor++;
        if (buffer.ElementCursor < elements.Count)
        {
            return;
        }

        buffer.Vertices.Add(new GraphicsManager.Vertex(buffer.Position, buffer.Colour, buffer.Uv));
        buffer.ElementCursor = 0;
        buffer.ResetVertex();
    }
}
