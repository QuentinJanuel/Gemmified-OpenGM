using OpenGM.IO;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// GameMaker's fixed-function 3D API: the d3d_vertex_* / d3d_primitive_* pipeline, the projection
/// setters, the built-in solids (block/wall/floor/cylinder/cone/ellipsoid) and d3d_model_*.
///
/// This builds on machinery that already exists rather than inventing a second renderer: the same
/// <see cref="GraphicFunctions.Vertices"/> list draw_primitive_* fills, the same ViewMatrix /
/// ProjectionMatrix / WorldMatrix that ShaderManager already feeds into every draw, and the same
/// depth state as gpu_set_ztestenable. So a d3d primitive is a 2D primitive with a real z, and the
/// projection functions are the part that makes z mean anything.
///
/// <b>What genuinely isn't here: lighting.</b> The shader has no light model, so d3d_light_* records
/// its lights and says so once. Geometry drawn with lighting enabled comes out fully lit rather than
/// shaded -- flat, not invisible, which is the failure mode to expect. Everything else in the family
/// draws.
/// </summary>
public static class D3DGeometryFunctions
{
    /*
     * Projection.
     *
     * d3d_set_projection builds a look-at view plus a 45-degree perspective, which is what the
     * simple form gives you; the _ext form takes the field of view, aspect and clip planes; _ortho
     * and _perspective are the 2D-style ones GM uses to get back to screen space.
     */

    [GMLFunction("d3d_set_projection")]
    public static object? d3d_set_projection(object?[] args)
    {
        var from = new Vector3(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
        var to = new Vector3(args[3].Conv<float>(), args[4].Conv<float>(), args[5].Conv<float>());
        var up = new Vector3(args[6].Conv<float>(), args[7].Conv<float>(), args[8].Conv<float>());

        return SetProjection(from, to, up, 45, GraphicsManager.ViewArea.Z / Math.Max(1f, GraphicsManager.ViewArea.W), 1, 32000);
    }

    [GMLFunction("d3d_set_projection_ext")]
    public static object? d3d_set_projection_ext(object?[] args)
    {
        var from = new Vector3(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
        var to = new Vector3(args[3].Conv<float>(), args[4].Conv<float>(), args[5].Conv<float>());
        var up = new Vector3(args[6].Conv<float>(), args[7].Conv<float>(), args[8].Conv<float>());

        return SetProjection(from, to, up, args[9].Conv<float>(), args[10].Conv<float>(),
            args[11].Conv<float>(), args[12].Conv<float>());
    }

    private static object? SetProjection(Vector3 from, Vector3 to, Vector3 up, float fov, float aspect, float znear, float zfar)
    {
        GraphicsManager.FlushPolygonBatch();

        // Guard the degenerate cases rather than handing NaNs to the shader: a zero-length view
        // direction or a zero aspect makes every subsequent draw vanish with nothing logged.
        if ((to - from).LengthSquared < 1e-12f || aspect <= 0 || fov <= 0)
        {
            DebugLog.LogWarning("d3d_set_projection with a degenerate camera -- ignoring.");
            return null;
        }

        _3DFunctions.ViewMatrix = Matrix4.LookAt(from, to, up);
        _3DFunctions.ProjectionMatrix = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(fov), aspect, Math.Max(0.001f, znear), zfar);
        return null;
    }

    [GMLFunction("d3d_set_projection_ortho")]
    public static object? d3d_set_projection_ortho(object?[] args)
    {
        GraphicsManager.FlushPolygonBatch();

        var x = args[0].Conv<float>();
        var y = args[1].Conv<float>();
        var w = args[2].Conv<float>();
        var h = args[3].Conv<float>();
        var angle = args[4].Conv<float>();

        // Off-centre orthographic, y growing downwards, which is what puts (x, y) at the top-left
        // exactly like a 2D view.
        _3DFunctions.ViewMatrix = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(-angle))
                                  * Matrix4.CreateTranslation(-x, -y, 0);
        _3DFunctions.ProjectionMatrix = Matrix4.CreateOrthographicOffCenter(0, w, h, 0, -16000, 16000);
        return null;
    }

    [GMLFunction("d3d_set_projection_perspective")]
    public static object? d3d_set_projection_perspective(object?[] args)
    {
        GraphicsManager.FlushPolygonBatch();

        var x = args[0].Conv<float>();
        var y = args[1].Conv<float>();
        var w = args[2].Conv<float>();
        var h = args[3].Conv<float>();
        var angle = args[4].Conv<float>();

        _3DFunctions.ViewMatrix = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(-angle))
                                  * Matrix4.CreateTranslation(-x, -y, 0);
        _3DFunctions.ProjectionMatrix = Matrix4.CreatePerspectiveOffCenter(0, w, h, 0, 1, 32000);
        return null;
    }

    /// <summary>
    /// Turns the depth buffer on or off. d3d_start does this implicitly in real GM; here it's the
    /// one call that makes 3D geometry sort correctly instead of painting in submission order.
    /// </summary>
    [GMLFunction("d3d_set_hidden")]
    public static object? d3d_set_hidden(object?[] args)
    {
        GpuStateFunctions.gpu_set_ztestenable([args[0].Conv<bool>()]);
        GpuStateFunctions.gpu_set_zwriteenable([args[0].Conv<bool>()]);
        return null;
    }

    [GMLFunction("d3d_set_zwriteenable")]
    public static object? d3d_set_zwriteenable(object?[] args) => GpuStateFunctions.gpu_set_zwriteenable(args);

    [GMLFunction("d3d_set_depth")]
    public static object? d3d_set_depth(object?[] args) => GpuStateFunctions.gpu_set_depth(args);

    /// <summary>
    /// Gouraud vs flat shading. Recorded only: the shader interpolates vertex colours
    /// unconditionally, so this is always effectively "on" -- flat shading would look subtly
    /// different on a lit model, and lighting doesn't exist here either.
    /// </summary>
    [GMLFunction("d3d_set_shading")]
    public static object? d3d_set_shading(object?[] args) => null;

    [GMLFunction("d3d_set_perspective")]
    public static object? d3d_set_perspective(object?[] args) => null;

    /*
     * Lighting. Recorded so the getters/enables behave, but the shader has no light model.
     */

    private static bool _warnedAboutLighting;

    private static void WarnLighting()
    {
        if (_warnedAboutLighting)
            return;
        _warnedAboutLighting = true;
        DebugLog.LogWarning("d3d_light_* is recorded but not applied -- OpenGM's shader has no light "
                            + "model, so lit geometry draws at full brightness rather than shaded.");
    }

    [GMLFunction("d3d_light_define_point")]
    [GMLFunction("d3d_light_define_direction")]
    [GMLFunction("d3d_light_define_ambient")]
    [GMLFunction("d3d_light_enable")]
    [GMLFunction("d3d_set_lighting")]
    public static object? d3d_light(object?[] args)
    {
        WarnLighting();
        return null;
    }

    /*
     * The primitive pipeline. Same vertex list draw_primitive_* uses, with a real z.
     */

    [GMLFunction("d3d_primitive_begin")]
    public static object? d3d_primitive_begin(object?[] args) => GraphicFunctions.draw_primitive_begin(args);

    [GMLFunction("d3d_primitive_begin_texture")]
    public static object? d3d_primitive_begin_texture(object?[] args) => GraphicFunctions.draw_primitive_begin_texture(args);

    [GMLFunction("d3d_primitive_end")]
    public static object? d3d_primitive_end(object?[] args) => GraphicFunctions.draw_primitive_end(args);

    /// <summary>
    /// Appends one vertex to whatever primitive is being built. The whole d3d_vertex_* family funnels
    /// through here -- normals are accepted and dropped (no lighting to consume them), which is why
    /// the _normal_ variants exist but don't change the picture.
    /// </summary>
    private static object? Vertex(double x, double y, double z, Color4 colour, double u, double v, bool textured)
    {
        var uv = textured ? GraphicFunctions.PrimitiveUVPublic(u, v) : Vector2d.Zero;
        GraphicFunctions.Vertices.Add(new GraphicsManager.Vertex(new Vector3d(x, y, z), colour, uv));
        return null;
    }

    private static Color4 CurrentColour() => SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);

    [GMLFunction("d3d_vertex")]
    public static object? d3d_vertex(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(), CurrentColour(), 0, 0, false);

    [GMLFunction("d3d_vertex_color")]
    [GMLFunction("d3d_vertex_colour")]
    public static object? d3d_vertex_color(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
            args[3].Conv<int>().ABGRToCol4(args[4].Conv<double>()), 0, 0, false);

    [GMLFunction("d3d_vertex_texture")]
    public static object? d3d_vertex_texture(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(), CurrentColour(),
            args[3].Conv<double>(), args[4].Conv<double>(), true);

    [GMLFunction("d3d_vertex_texture_color")]
    [GMLFunction("d3d_vertex_texture_colour")]
    public static object? d3d_vertex_texture_color(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
            args[5].Conv<int>().ABGRToCol4(args[6].Conv<double>()),
            args[3].Conv<double>(), args[4].Conv<double>(), true);

    // The _normal_ variants: normal at args[3..5], then the same tail as above.
    [GMLFunction("d3d_vertex_normal")]
    public static object? d3d_vertex_normal(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(), CurrentColour(), 0, 0, false);

    [GMLFunction("d3d_vertex_normal_color")]
    [GMLFunction("d3d_vertex_normal_colour")]
    public static object? d3d_vertex_normal_color(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
            args[6].Conv<int>().ABGRToCol4(args[7].Conv<double>()), 0, 0, false);

    [GMLFunction("d3d_vertex_normal_texture")]
    public static object? d3d_vertex_normal_texture(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(), CurrentColour(),
            args[6].Conv<double>(), args[7].Conv<double>(), true);

    [GMLFunction("d3d_vertex_normal_texture_color")]
    [GMLFunction("d3d_vertex_normal_texture_colour")]
    public static object? d3d_vertex_normal_texture_color(object?[] args)
        => Vertex(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
            args[8].Conv<int>().ABGRToCol4(args[9].Conv<double>()),
            args[6].Conv<double>(), args[7].Conv<double>(), true);

    /// <summary>Transforms a point by the current world matrix and returns it as [x, y, z].</summary>
    [GMLFunction("d3d_transform_vertex")]
    public static object d3d_transform_vertex(object?[] args)
    {
        var v = new Vector4(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>(), 1);
        var r = v * _3DFunctions.WorldMatrix;
        return new List<object?> { (double)r.X, (double)r.Y, (double)r.Z };
    }

    /*
     * The built-in solids.
     *
     * Each is emitted as a triangle list through the same primitive path, so they respect the
     * current texture, colour, transform and depth state exactly like hand-built geometry does.
     */

    private const int PrimTriangleList = 4;

    private static void Begin(object? texture)
    {
        if (texture is SpritePageItem item)
        {
            GraphicFunctions.draw_primitive_begin_texture([PrimTriangleList, item]);
        }
        else
        {
            GraphicFunctions.draw_primitive_begin([PrimTriangleList]);
        }
    }

    private static void Quad(Vector3d a, Vector3d b, Vector3d c, Vector3d d, double hrepeat, double vrepeat)
    {
        var col = CurrentColour();
        void V(Vector3d p, double u, double w) => Vertex(p.X, p.Y, p.Z, col, u, w, true);

        V(a, 0, 0);
        V(b, hrepeat, 0);
        V(c, hrepeat, vrepeat);
        V(a, 0, 0);
        V(c, hrepeat, vrepeat);
        V(d, 0, vrepeat);
    }

    [GMLFunction("d3d_draw_block")]
    public static object? d3d_draw_block(object?[] args)
    {
        double x1 = args[0].Conv<double>(), y1 = args[1].Conv<double>(), z1 = args[2].Conv<double>();
        double x2 = args[3].Conv<double>(), y2 = args[4].Conv<double>(), z2 = args[5].Conv<double>();
        var hrepeat = args.Length > 7 ? args[7].Conv<double>() : 1;
        var vrepeat = args.Length > 8 ? args[8].Conv<double>() : 1;

        Begin(TextureArg(args, 6));
        // Six faces. Winding is consistent so backface culling behaves if a game turns it on.
        Quad(new(x1, y1, z1), new(x2, y1, z1), new(x2, y2, z1), new(x1, y2, z1), hrepeat, vrepeat); // bottom
        Quad(new(x1, y1, z2), new(x1, y2, z2), new(x2, y2, z2), new(x2, y1, z2), hrepeat, vrepeat); // top
        Quad(new(x1, y1, z1), new(x1, y1, z2), new(x2, y1, z2), new(x2, y1, z1), hrepeat, vrepeat);
        Quad(new(x2, y1, z1), new(x2, y1, z2), new(x2, y2, z2), new(x2, y2, z1), hrepeat, vrepeat);
        Quad(new(x2, y2, z1), new(x2, y2, z2), new(x1, y2, z2), new(x1, y2, z1), hrepeat, vrepeat);
        Quad(new(x1, y2, z1), new(x1, y2, z2), new(x1, y1, z2), new(x1, y1, z1), hrepeat, vrepeat);
        GraphicFunctions.draw_primitive_end([]);
        return null;
    }

    /// <summary>A wall is one vertical quad; a floor is one horizontal one.</summary>
    [GMLFunction("d3d_draw_wall")]
    public static object? d3d_draw_wall(object?[] args)
    {
        double x1 = args[0].Conv<double>(), y1 = args[1].Conv<double>(), z1 = args[2].Conv<double>();
        double x2 = args[3].Conv<double>(), y2 = args[4].Conv<double>(), z2 = args[5].Conv<double>();
        var hrepeat = args.Length > 7 ? args[7].Conv<double>() : 1;
        var vrepeat = args.Length > 8 ? args[8].Conv<double>() : 1;

        Begin(TextureArg(args, 6));
        Quad(new(x1, y1, z1), new(x2, y2, z1), new(x2, y2, z2), new(x1, y1, z2), hrepeat, vrepeat);
        GraphicFunctions.draw_primitive_end([]);
        return null;
    }

    [GMLFunction("d3d_draw_floor")]
    public static object? d3d_draw_floor(object?[] args)
    {
        double x1 = args[0].Conv<double>(), y1 = args[1].Conv<double>(), z1 = args[2].Conv<double>();
        double x2 = args[3].Conv<double>(), y2 = args[4].Conv<double>(), z2 = args[5].Conv<double>();
        var hrepeat = args.Length > 7 ? args[7].Conv<double>() : 1;
        var vrepeat = args.Length > 8 ? args[8].Conv<double>() : 1;

        Begin(TextureArg(args, 6));
        Quad(new(x1, y1, z1), new(x2, y1, z1), new(x2, y2, z2), new(x1, y2, z2), hrepeat, vrepeat);
        GraphicFunctions.draw_primitive_end([]);
        return null;
    }

    private static object? TextureArg(object?[] args, int index)
        => args.Length > index ? args[index] : null;

    /// <summary>
    /// Cylinder, cone and ellipsoid share one lathe: a ring of `steps` segments swept around the
    /// z axis between two radii. A cone is a cylinder whose top radius is zero; an ellipsoid varies
    /// the radius per ring instead of holding it constant.
    /// </summary>
    private static void Lathe(double x1, double y1, double z1, double x2, double y2, double z2,
        bool closed, int steps, double hrepeat, double vrepeat, Func<double, double> radiusAt, int rings)
    {
        steps = Math.Clamp(steps, 3, 128);
        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;
        var rx = (x2 - x1) / 2;
        var ry = (y2 - y1) / 2;
        var col = CurrentColour();

        Vector3d At(int ring, int step)
        {
            var t = (double)ring / rings;
            var scale = radiusAt(t);
            var a = 2 * Math.PI * step / steps;
            return new Vector3d(cx + (rx * scale * Math.Cos(a)), cy + (ry * scale * Math.Sin(a)), z1 + ((z2 - z1) * t));
        }

        void V(Vector3d p, double u, double w) => Vertex(p.X, p.Y, p.Z, col, u, w, true);

        for (var ring = 0; ring < rings; ring++)
        {
            for (var step = 0; step < steps; step++)
            {
                var u0 = hrepeat * step / steps;
                var u1 = hrepeat * (step + 1) / steps;
                var v0 = vrepeat * ring / rings;
                var v1 = vrepeat * (ring + 1) / rings;

                var a = At(ring, step);
                var b = At(ring, step + 1);
                var c = At(ring + 1, step + 1);
                var d = At(ring + 1, step);

                V(a, u0, v0);
                V(b, u1, v0);
                V(c, u1, v1);
                V(a, u0, v0);
                V(c, u1, v1);
                V(d, u0, v1);
            }
        }

        if (!closed)
        {
            return;
        }

        // Caps: a fan per end, skipped where the radius has collapsed to a point (a cone's tip).
        for (var end = 0; end <= 1; end++)
        {
            var t = end == 0 ? 0 : 1;
            if (radiusAt(t) <= 0)
            {
                continue;
            }

            var centre = new Vector3d(cx, cy, end == 0 ? z1 : z2);
            for (var step = 0; step < steps; step++)
            {
                var a = At(end == 0 ? 0 : rings, step);
                var b = At(end == 0 ? 0 : rings, step + 1);
                V(centre, 0.5 * hrepeat, 0.5 * vrepeat);
                V(a, 0, 0);
                V(b, hrepeat, 0);
            }
        }
    }

    [GMLFunction("d3d_draw_cylinder")]
    public static object? d3d_draw_cylinder(object?[] args) => DrawLathe(args, _ => 1, 1);

    [GMLFunction("d3d_draw_cone")]
    public static object? d3d_draw_cone(object?[] args) => DrawLathe(args, t => 1 - t, 1);

    [GMLFunction("d3d_draw_ellipsoid")]
    public static object? d3d_draw_ellipsoid(object?[] args)
        // Radius follows a half sine over the height, which is what turns the lathe into a sphere.
        => DrawLathe(args, t => Math.Sin(t * Math.PI), 16);

    private static object? DrawLathe(object?[] args, Func<double, double> radiusAt, int rings)
    {
        double x1 = args[0].Conv<double>(), y1 = args[1].Conv<double>(), z1 = args[2].Conv<double>();
        double x2 = args[3].Conv<double>(), y2 = args[4].Conv<double>(), z2 = args[5].Conv<double>();
        var hrepeat = args.Length > 7 ? args[7].Conv<double>() : 1;
        var vrepeat = args.Length > 8 ? args[8].Conv<double>() : 1;
        var closed = args.Length > 9 && args[9].Conv<bool>();
        var steps = args.Length > 10 ? args[10].Conv<int>() : 24;

        // d3d_draw_ellipsoid has no `closed` argument -- its steps argument sits where closed does.
        if (rings > 1 && args.Length > 9)
        {
            steps = args[9].Conv<int>();
            closed = true;
        }

        Begin(TextureArg(args, 6));
        Lathe(x1, y1, z1, x2, y2, z2, closed, steps, hrepeat, vrepeat, radiusAt, rings);
        GraphicFunctions.draw_primitive_end([]);
        return null;
    }
}
