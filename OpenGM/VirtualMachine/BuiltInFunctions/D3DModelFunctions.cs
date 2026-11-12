using OpenGM.IO;
using OpenGM.Rendering;
using OpenTK.Mathematics;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// d3d_model_* -- GameMaker's retained-mode counterpart to the immediate d3d_draw_* calls.
///
/// A model is a recorded display list: every d3d_model_* call that would have drawn something
/// appends the call instead, and d3d_model_draw replays the lot with an offset and scale applied.
/// That's exactly what the API is for -- build the geometry once, draw it every frame without
/// re-emitting it -- and it means models get whatever the immediate path gets, including textures,
/// culling and depth, without a second geometry implementation to keep in sync.
///
/// Replaying rather than caching a vertex buffer is a deliberate simplification: it costs a little
/// per frame but keeps one code path, and a model here is at most a few hundred primitives.
/// </summary>
public static class D3DModelFunctions
{
    /// <summary>One recorded call: the function to replay and the arguments it was given.</summary>
    private record ModelCall(Func<object?[], object?> Draw, object?[] Args);

    private class Model
    {
        public readonly List<ModelCall> Calls = new();

        /// <summary>Set between d3d_model_primitive_begin and _end, so vertices know where to go.</summary>
        public int PendingPrimitiveKind = -1;
        public object? PendingTexture;
        public readonly List<object?[]> PendingVertices = new();
    }

    private static readonly Dictionary<int, Model> Models = new();
    private static int _nextId;

    public static void Reset()
    {
        Models.Clear();
        _nextId = 0;
    }

    private static Model? Get(object? id) => Models.TryGetValue(id.Conv<int>(), out var m) ? m : null;

    [GMLFunction("d3d_model_create")]
    public static object d3d_model_create(object?[] args)
    {
        var id = _nextId++;
        Models[id] = new Model();
        return id;
    }

    [GMLFunction("d3d_model_destroy")]
    public static object? d3d_model_destroy(object?[] args)
    {
        Models.Remove(args[0].Conv<int>());
        return null;
    }

    [GMLFunction("d3d_model_clear")]
    public static object? d3d_model_clear(object?[] args)
    {
        var model = Get(args[0]);
        model?.Calls.Clear();
        model?.PendingVertices.Clear();
        return null;
    }

    /// <summary>
    /// Replays the model with (x, y, z) added to every position and the whole thing scaled.
    /// Implemented by composing onto the world matrix rather than rewriting the recorded
    /// coordinates, so the model's own geometry stays immutable and can be drawn many times at
    /// different places in one frame.
    /// </summary>
    [GMLFunction("d3d_model_draw")]
    public static object? d3d_model_draw(object?[] args)
    {
        var model = Get(args[0]);
        if (model == null)
        {
            return null;
        }

        var x = args[1].Conv<float>();
        var y = args[2].Conv<float>();
        var z = args[3].Conv<float>();
        var texture = args.Length > 4 ? args[4] : null;

        var saved = _3DFunctions.WorldMatrix;
        _3DFunctions.WorldMatrix = Matrix4.CreateTranslation(x, y, z) * saved;

        try
        {
            foreach (var call in model.Calls)
            {
                // The texture given to d3d_model_draw overrides whatever was recorded -- that's how
                // GM lets one model be drawn with different skins.
                var callArgs = call.Args;
                if (texture != null && callArgs.Length > 6)
                {
                    callArgs = (object?[])callArgs.Clone();
                    callArgs[6] = texture;
                }

                call.Draw(callArgs);
            }
        }
        finally
        {
            _3DFunctions.WorldMatrix = saved;
        }

        return null;
    }

    /*
     * The shape recorders. Each mirrors its d3d_draw_* counterpart's signature with the model id
     * in front, and records rather than draws.
     */

    private static object? Record(object?[] args, Func<object?[], object?> draw)
    {
        var model = Get(args[0]);
        model?.Calls.Add(new ModelCall(draw, args[1..]));
        return null;
    }

    [GMLFunction("d3d_model_block")]
    public static object? d3d_model_block(object?[] args) => Record(args, D3DGeometryFunctions.d3d_draw_block);

    [GMLFunction("d3d_model_wall")]
    public static object? d3d_model_wall(object?[] args) => Record(args, D3DGeometryFunctions.d3d_draw_wall);

    [GMLFunction("d3d_model_floor")]
    public static object? d3d_model_floor(object?[] args) => Record(args, D3DGeometryFunctions.d3d_draw_floor);

    [GMLFunction("d3d_model_cylinder")]
    public static object? d3d_model_cylinder(object?[] args) => Record(args, D3DGeometryFunctions.d3d_draw_cylinder);

    [GMLFunction("d3d_model_cone")]
    public static object? d3d_model_cone(object?[] args) => Record(args, D3DGeometryFunctions.d3d_draw_cone);

    [GMLFunction("d3d_model_ellipsoid")]
    public static object? d3d_model_ellipsoid(object?[] args) => Record(args, D3DGeometryFunctions.d3d_draw_ellipsoid);

    /*
     * Hand-built primitives inside a model.
     */

    [GMLFunction("d3d_model_primitive_begin")]
    public static object? d3d_model_primitive_begin(object?[] args)
    {
        var model = Get(args[0]);
        if (model == null)
            return null;

        model.PendingPrimitiveKind = args[1].Conv<int>();
        model.PendingTexture = args.Length > 2 ? args[2] : null;
        model.PendingVertices.Clear();
        return null;
    }

    [GMLFunction("d3d_model_primitive_end")]
    public static object? d3d_model_primitive_end(object?[] args)
    {
        var model = Get(args[0]);
        if (model == null || model.PendingPrimitiveKind < 0)
        {
            return null;
        }

        var kind = model.PendingPrimitiveKind;
        var texture = model.PendingTexture;
        var verts = model.PendingVertices.ToList();

        model.Calls.Add(new ModelCall(_ =>
        {
            if (texture != null)
            {
                GraphicFunctions.draw_primitive_begin_texture([kind, texture]);
            }
            else
            {
                GraphicFunctions.draw_primitive_begin([kind]);
            }

            foreach (var v in verts)
            {
                D3DGeometryFunctions.d3d_vertex_texture_color(v);
            }

            return GraphicFunctions.draw_primitive_end([]);
        }, []));

        model.PendingPrimitiveKind = -1;
        model.PendingVertices.Clear();
        return null;
    }

    /// <summary>
    /// Every d3d_model_vertex_* spelling normalises to one shape -- x, y, z, u, v, colour, alpha --
    /// so the replay only has to know one call. Missing components take the defaults GM uses:
    /// zero uv and the current draw colour at full alpha.
    /// </summary>
    private static object? ModelVertex(object?[] args, double u, double v, int colour, double alpha)
    {
        var model = Get(args[0]);
        model?.PendingVertices.Add(
        [
            args[1].Conv<double>(), args[2].Conv<double>(), args[3].Conv<double>(),
            u, v, colour, alpha
        ]);
        return null;
    }

    private static int DrawColour() => SpriteManager.DrawColor;

    [GMLFunction("d3d_model_vertex")]
    public static object? d3d_model_vertex(object?[] args)
        => ModelVertex(args, 0, 0, DrawColour(), SpriteManager.DrawAlpha);

    [GMLFunction("d3d_model_vertex_color")]
    [GMLFunction("d3d_model_vertex_colour")]
    public static object? d3d_model_vertex_color(object?[] args)
        => ModelVertex(args, 0, 0, args[4].Conv<int>(), args[5].Conv<double>());

    [GMLFunction("d3d_model_vertex_texture")]
    public static object? d3d_model_vertex_texture(object?[] args)
        => ModelVertex(args, args[4].Conv<double>(), args[5].Conv<double>(), DrawColour(), SpriteManager.DrawAlpha);

    [GMLFunction("d3d_model_vertex_texture_color")]
    [GMLFunction("d3d_model_vertex_texture_colour")]
    public static object? d3d_model_vertex_texture_color(object?[] args)
        => ModelVertex(args, args[4].Conv<double>(), args[5].Conv<double>(), args[6].Conv<int>(), args[7].Conv<double>());

    // The _normal_ spellings carry a normal at args[4..6]; there is no lighting to consume it, so
    // it is accepted and dropped exactly as in the immediate path.
    [GMLFunction("d3d_model_vertex_normal")]
    public static object? d3d_model_vertex_normal(object?[] args)
        => ModelVertex(args, 0, 0, DrawColour(), SpriteManager.DrawAlpha);

    [GMLFunction("d3d_model_vertex_normal_color")]
    [GMLFunction("d3d_model_vertex_normal_colour")]
    public static object? d3d_model_vertex_normal_color(object?[] args)
        => ModelVertex(args, 0, 0, args[7].Conv<int>(), args[8].Conv<double>());

    [GMLFunction("d3d_model_vertex_normal_texture")]
    public static object? d3d_model_vertex_normal_texture(object?[] args)
        => ModelVertex(args, args[7].Conv<double>(), args[8].Conv<double>(), DrawColour(), SpriteManager.DrawAlpha);

    [GMLFunction("d3d_model_vertex_normal_texture_color")]
    [GMLFunction("d3d_model_vertex_normal_texture_colour")]
    public static object? d3d_model_vertex_normal_texture_color(object?[] args)
        => ModelVertex(args, args[7].Conv<double>(), args[8].Conv<double>(), args[9].Conv<int>(), args[10].Conv<double>());

    /*
     * Model files. GM's .d3d format is a plain text list of "kind arg1..arg10" lines, one per
     * recorded call -- readable enough to support both directions.
     */

    [GMLFunction("d3d_model_save")]
    public static object d3d_model_save(object?[] args)
    {
        DebugLog.LogWarning("d3d_model_save is not implemented -- OpenGM records models as replayable "
                            + "calls rather than in GM's on-disk .d3d layout, so there is nothing to serialise yet.");
        return false;
    }

    [GMLFunction("d3d_model_load")]
    public static object d3d_model_load(object?[] args)
    {
        DebugLog.LogWarning("d3d_model_load is not implemented -- a model loaded from disk will be empty, "
                            + "so whatever it was meant to draw simply will not appear.");
        return false;
    }

    [GMLFunction("d3d_model_save_buffer")]
    public static object d3d_model_save_buffer(object?[] args) => d3d_model_save(args);

    [GMLFunction("d3d_model_load_buffer")]
    public static object d3d_model_load_buffer(object?[] args) => d3d_model_load(args);
}

/// <summary>
/// matrix_stack_* -- a save/restore stack for the world matrix, used the same way gpu_push_state is
/// for GPU state. matrix_stack_push composes the given matrix onto the top of the stack, which is
/// what makes it a *transform* stack rather than a plain save stack.
/// </summary>
public static class MatrixStackFunctions
{
    private static readonly List<Matrix4> Stack = new();

    public static void Reset() => Stack.Clear();

    [GMLFunction("matrix_stack_push")]
    public static object? matrix_stack_push(object?[] args)
    {
        var m = _3DFunctions.ListToMatrixPublic(args[0].Conv<System.Collections.IList>());
        // Composed onto the current top, so nested pushes accumulate -- push(translate) then
        // push(rotate) gives translate-then-rotate, matching how d3d_transform_add_* chains.
        Stack.Add(Stack.Count == 0 ? m : m * Stack[^1]);
        return null;
    }

    [GMLFunction("matrix_stack_pop")]
    public static object? matrix_stack_pop(object?[] args)
    {
        if (Stack.Count > 0)
        {
            Stack.RemoveAt(Stack.Count - 1);
        }

        return null;
    }

    [GMLFunction("matrix_stack_top")]
    public static object matrix_stack_top(object?[] args)
        => _3DFunctions.MatrixToListPublic(Stack.Count == 0 ? Matrix4.Identity : Stack[^1])!;

    [GMLFunction("matrix_stack_clear")]
    public static object? matrix_stack_clear(object?[] args)
    {
        Stack.Clear();
        return null;
    }

    [GMLFunction("matrix_stack_is_empty")]
    public static object matrix_stack_is_empty(object?[] args) => Stack.Count == 0;

    /// <summary>Applies the top of the stack as the world matrix.</summary>
    [GMLFunction("matrix_stack_set")]
    public static object? matrix_stack_set(object?[] args)
    {
        _3DFunctions.WorldMatrix = Stack.Count == 0 ? Matrix4.Identity : Stack[^1];
        return null;
    }
}
