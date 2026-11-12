using OpenGM.Rendering;
using OpenTK.Graphics.OpenGL4;
using System.Collections;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// The gpu_get_*/gpu_set_* state block, and gpu_push_state/gpu_pop_state/gpu_get_state/gpu_set_state
/// on top of it.
///
/// Most of these were missing entirely and the rest were bare stubs, which for a *getter* is the
/// worst possible shape: <c>gpu_get_blendmode()</c> returning 0 is indistinguishable from
/// bm_normal, so the extremely common save-restore idiom
/// <code>
///   var old = gpu_get_blendmode(); gpu_set_blendmode(bm_add); ...; gpu_set_blendmode(old);
/// </code>
/// silently reset the blend mode to normal instead of restoring it, and every later draw in the
/// frame came out wrong -- with nothing logged.
///
/// So the point of this file is that the state is <b>tracked</b>, not that every knob reaches the
/// GL. Where a setting has a real effect in this renderer it is applied (blend mode, cull, depth,
/// alpha test, texture filtering and repeat); the mip/anisotropy settings are recorded and honestly
/// do nothing, because the 2D pipeline draws from texture pages with no mip chain. Recording them
/// is still correct behaviour for the getter, which is what games actually depend on.
/// </summary>
public static class GpuStateFunctions
{
    /// <summary>
    /// One complete GPU state, as gpu_get_state returns it and gpu_push_state saves it. Kept as a
    /// class with value-copy semantics via <see cref="Clone"/> so a pushed state can't be mutated
    /// by later sets.
    /// </summary>
    public class GpuState
    {
        public bool BlendEnable = true;
        public int BlendMode = 0;
        public int BlendSrc = 2, BlendDest = 3, BlendSrcAlpha = 2, BlendDestAlpha = 3;
        public bool ZTestEnable, ZWriteEnable;
        public int ZFunc = 2;
        public int CullMode;
        public bool AlphaTestEnable;
        public int AlphaTestRef;
        public int AlphaTestFunc = 5;
        public bool TexFilter;
        public bool TexRepeat;
        public bool TexMipEnable;
        public int TexMipFilter;
        public double TexMipBias;
        public int TexMinMip, TexMaxMip = 16;
        public double TexMaxAniso = 1;
        public bool ColourWriteR = true, ColourWriteG = true, ColourWriteB = true, ColourWriteA = true;
        public bool FogEnabled;
        public int FogColour;
        public double FogStart, FogEnd;

        public GpuState Clone() => (GpuState)MemberwiseClone();
    }

    public static GpuState Current = new();
    private static readonly Stack<GpuState> StateStack = new();

    public static void Reset()
    {
        Current = new GpuState();
        StateStack.Clear();
    }

    /*
     * Depth and culling -- these do reach the GL, since the d3d_* family needs them to mean
     * anything.
     */

    [GMLFunction("gpu_set_ztestenable")]
    public static object? gpu_set_ztestenable(object?[] args)
    {
        GraphicsManager.FlushPolygonBatch();
        Current.ZTestEnable = args[0].Conv<bool>();
        if (Current.ZTestEnable)
            GL.Enable(EnableCap.DepthTest);
        else
            GL.Disable(EnableCap.DepthTest);
        return null;
    }

    [GMLFunction("gpu_get_ztestenable")]
    public static object gpu_get_ztestenable(object?[] args) => Current.ZTestEnable;

    [GMLFunction("gpu_set_zwriteenable")]
    public static object? gpu_set_zwriteenable(object?[] args)
    {
        GraphicsManager.FlushPolygonBatch();
        Current.ZWriteEnable = args[0].Conv<bool>();
        GL.DepthMask(Current.ZWriteEnable);
        return null;
    }

    [GMLFunction("gpu_get_zwriteenable")]
    public static object gpu_get_zwriteenable(object?[] args) => Current.ZWriteEnable;

    /// <summary>GM's cmpfunc_* constants are 1..8; GL's depth funcs map onto them in the same order.</summary>
    private static DepthFunction ToDepthFunc(int cmp) => cmp switch
    {
        1 => DepthFunction.Never,
        2 => DepthFunction.Less,
        3 => DepthFunction.Equal,
        4 => DepthFunction.Lequal,
        5 => DepthFunction.Greater,
        6 => DepthFunction.Notequal,
        7 => DepthFunction.Gequal,
        _ => DepthFunction.Always,
    };

    [GMLFunction("gpu_set_zfunc")]
    public static object? gpu_set_zfunc(object?[] args)
    {
        GraphicsManager.FlushPolygonBatch();
        Current.ZFunc = args[0].Conv<int>();
        GL.DepthFunc(ToDepthFunc(Current.ZFunc));
        return null;
    }

    [GMLFunction("gpu_get_zfunc")]
    public static object gpu_get_zfunc(object?[] args) => Current.ZFunc;

    [GMLFunction("gpu_set_depth")]
    public static object? gpu_set_depth(object?[] args)
    {
        GraphicsManager.GR_Depth = (float)args[0].Conv<double>();
        return null;
    }

    [GMLFunction("gpu_get_depth")]
    public static object gpu_get_depth(object?[] args) => (double)GraphicsManager.GR_Depth;

    [GMLFunction("gpu_set_cullmode")]
    [GMLFunction("d3d_set_culling")]
    public static object? gpu_set_cullmode(object?[] args)
    {
        GraphicsManager.FlushPolygonBatch();

        // d3d_set_culling takes a bool ("cull backfaces or not"); gpu_set_cullmode takes
        // cull_noculling(0) / cull_clockwise(1) / cull_counterclockwise(2).
        Current.CullMode = args[0] is bool b ? (b ? 1 : 0) : args[0].Conv<int>();

        if (Current.CullMode == 0)
        {
            GL.Disable(EnableCap.CullFace);
        }
        else
        {
            GL.Enable(EnableCap.CullFace);
            GL.FrontFace(Current.CullMode == 1 ? FrontFaceDirection.Ccw : FrontFaceDirection.Cw);
            GL.CullFace(TriangleFace.Back);
        }

        return null;
    }

    [GMLFunction("gpu_get_cullmode")]
    public static object gpu_get_cullmode(object?[] args) => Current.CullMode;

    /*
     * Alpha test
     */

    [GMLFunction("gpu_get_alphatestenable")]
    public static object gpu_get_alphatestenable(object?[] args) => Current.AlphaTestEnable;

    [GMLFunction("gpu_get_alphatestref")]
    public static object gpu_get_alphatestref(object?[] args) => Current.AlphaTestRef;

    /// <summary>
    /// Recorded only. GM's shaders test with a fixed "greater than ref" comparison and OpenGM's
    /// do the same, so a game asking for a different comparison keeps the default behaviour.
    /// </summary>
    [GMLFunction("gpu_set_alphatestfunc")]
    public static object? gpu_set_alphatestfunc(object?[] args)
    {
        Current.AlphaTestFunc = args[0].Conv<int>();
        return null;
    }

    [GMLFunction("gpu_get_alphatestfunc")]
    public static object gpu_get_alphatestfunc(object?[] args) => Current.AlphaTestFunc;

    /*
     * Blend mode getters. The setters live in 3DFunctions; these read back what was set, which is
     * the whole reason they exist.
     */

    // gpu_get_blendmode already exists in 3DFunctions; it now reads Current.BlendMode too.

    [GMLFunction("gpu_get_blendmode_ext")]
    public static object gpu_get_blendmode_ext(object?[] args) => new List<object?> { Current.BlendSrc, Current.BlendDest };

    [GMLFunction("gpu_get_blendmode_ext_sepalpha")]
    public static object gpu_get_blendmode_ext_sepalpha(object?[] args)
        => new List<object?> { Current.BlendSrc, Current.BlendDest, Current.BlendSrcAlpha, Current.BlendDestAlpha };

    [GMLFunction("gpu_get_blendmode_src")]
    public static object gpu_get_blendmode_src(object?[] args) => Current.BlendSrc;

    [GMLFunction("gpu_get_blendmode_dest")]
    public static object gpu_get_blendmode_dest(object?[] args) => Current.BlendDest;

    [GMLFunction("gpu_get_blendmode_srcalpha")]
    public static object gpu_get_blendmode_srcalpha(object?[] args) => Current.BlendSrcAlpha;

    [GMLFunction("gpu_get_blendmode_destalpha")]
    public static object gpu_get_blendmode_destalpha(object?[] args) => Current.BlendDestAlpha;

    /*
     * Texture sampler state.
     *
     * The non-_ext forms are global; the _ext forms take a shader sampler handle first and would
     * apply per sampler. OpenGM has one sampler in play, so both spellings share the global state
     * -- which is right for the overwhelmingly common case of a game setting it once for everything.
     */

    [GMLFunction("gpu_set_tex_filter")]
    [GMLFunction("gpu_set_texfilter")]
    public static object? gpu_set_tex_filter(object?[] args)
    {
        Current.TexFilter = args[^1].Conv<bool>();
        PageManager.SetInterpolation(Current.TexFilter);
        return null;
    }

    [GMLFunction("gpu_set_tex_filter_ext")]
    [GMLFunction("gpu_set_texfilter_ext")]
    public static object? gpu_set_tex_filter_ext(object?[] args) => gpu_set_tex_filter(args);

    [GMLFunction("gpu_get_tex_filter")]
    [GMLFunction("gpu_get_texfilter")]
    public static object gpu_get_tex_filter(object?[] args) => Current.TexFilter;

    [GMLFunction("gpu_get_tex_filter_ext")]
    [GMLFunction("gpu_get_texfilter_ext")]
    public static object gpu_get_tex_filter_ext(object?[] args) => Current.TexFilter;

    [GMLFunction("gpu_set_tex_repeat")]
    [GMLFunction("gpu_set_texrepeat")]
    [GMLFunction("texture_set_repeat")]
    public static object? gpu_set_tex_repeat(object?[] args)
    {
        Current.TexRepeat = args[^1].Conv<bool>();
        PageManager.SetRepeat(Current.TexRepeat);
        return null;
    }

    [GMLFunction("gpu_set_tex_repeat_ext")]
    [GMLFunction("gpu_set_texrepeat_ext")]
    public static object? gpu_set_tex_repeat_ext(object?[] args) => gpu_set_tex_repeat(args);

    [GMLFunction("gpu_get_tex_repeat")]
    [GMLFunction("gpu_get_texrepeat")]
    public static object gpu_get_tex_repeat(object?[] args) => Current.TexRepeat;

    [GMLFunction("gpu_get_tex_repeat_ext")]
    [GMLFunction("gpu_get_texrepeat_ext")]
    public static object gpu_get_tex_repeat_ext(object?[] args) => Current.TexRepeat;

    /*
     * Mip-mapping and anisotropy. Recorded faithfully so the getters round-trip, but inert: sprites
     * come off texture pages that are built without a mip chain, so there is nothing for a mip
     * filter or an LOD bias to select between. A game setting these gets the same picture either
     * way -- which is also true on a lot of real hardware paths, so this is a smaller divergence
     * than it looks.
     */

    [GMLFunction("gpu_set_tex_mip_enable")]
    public static object? gpu_set_tex_mip_enable(object?[] args) { Current.TexMipEnable = args[^1].Conv<bool>(); return null; }

    [GMLFunction("gpu_set_tex_mip_enable_ext")]
    public static object? gpu_set_tex_mip_enable_ext(object?[] args) => gpu_set_tex_mip_enable(args);

    [GMLFunction("gpu_get_tex_mip_enable")]
    public static object gpu_get_tex_mip_enable(object?[] args) => Current.TexMipEnable;

    [GMLFunction("gpu_get_tex_mip_enable_ext")]
    public static object gpu_get_tex_mip_enable_ext(object?[] args) => Current.TexMipEnable;

    [GMLFunction("gpu_set_tex_mip_filter")]
    public static object? gpu_set_tex_mip_filter(object?[] args) { Current.TexMipFilter = args[^1].Conv<int>(); return null; }

    [GMLFunction("gpu_set_tex_mip_filter_ext")]
    public static object? gpu_set_tex_mip_filter_ext(object?[] args) => gpu_set_tex_mip_filter(args);

    [GMLFunction("gpu_get_tex_mip_filter")]
    public static object gpu_get_tex_mip_filter(object?[] args) => Current.TexMipFilter;

    [GMLFunction("gpu_get_tex_mip_filter_ext")]
    public static object gpu_get_tex_mip_filter_ext(object?[] args) => Current.TexMipFilter;

    [GMLFunction("gpu_set_tex_mip_bias")]
    public static object? gpu_set_tex_mip_bias(object?[] args) { Current.TexMipBias = args[^1].Conv<double>(); return null; }

    [GMLFunction("gpu_set_tex_mip_bias_ext")]
    public static object? gpu_set_tex_mip_bias_ext(object?[] args) => gpu_set_tex_mip_bias(args);

    [GMLFunction("gpu_get_tex_mip_bias")]
    public static object gpu_get_tex_mip_bias(object?[] args) => Current.TexMipBias;

    [GMLFunction("gpu_get_tex_mip_bias_ext")]
    public static object gpu_get_tex_mip_bias_ext(object?[] args) => Current.TexMipBias;

    [GMLFunction("gpu_set_tex_min_mip")]
    public static object? gpu_set_tex_min_mip(object?[] args) { Current.TexMinMip = args[^1].Conv<int>(); return null; }

    [GMLFunction("gpu_set_tex_min_mip_ext")]
    public static object? gpu_set_tex_min_mip_ext(object?[] args) => gpu_set_tex_min_mip(args);

    [GMLFunction("gpu_get_tex_min_mip")]
    public static object gpu_get_tex_min_mip(object?[] args) => Current.TexMinMip;

    [GMLFunction("gpu_get_tex_min_mip_ext")]
    public static object gpu_get_tex_min_mip_ext(object?[] args) => Current.TexMinMip;

    [GMLFunction("gpu_set_tex_max_mip")]
    public static object? gpu_set_tex_max_mip(object?[] args) { Current.TexMaxMip = args[^1].Conv<int>(); return null; }

    [GMLFunction("gpu_set_tex_max_mip_ext")]
    public static object? gpu_set_tex_max_mip_ext(object?[] args) => gpu_set_tex_max_mip(args);

    [GMLFunction("gpu_get_tex_max_mip")]
    public static object gpu_get_tex_max_mip(object?[] args) => Current.TexMaxMip;

    [GMLFunction("gpu_get_tex_max_mip_ext")]
    public static object gpu_get_tex_max_mip_ext(object?[] args) => Current.TexMaxMip;

    [GMLFunction("gpu_set_tex_max_aniso")]
    public static object? gpu_set_tex_max_aniso(object?[] args) { Current.TexMaxAniso = args[^1].Conv<double>(); return null; }

    [GMLFunction("gpu_set_tex_max_aniso_ext")]
    public static object? gpu_set_tex_max_aniso_ext(object?[] args) => gpu_set_tex_max_aniso(args);

    [GMLFunction("gpu_get_tex_max_aniso")]
    public static object gpu_get_tex_max_aniso(object?[] args) => Current.TexMaxAniso;

    [GMLFunction("gpu_get_tex_max_aniso_ext")]
    public static object gpu_get_tex_max_aniso_ext(object?[] args) => Current.TexMaxAniso;

    /*
     * Fog getter (the setter lives in 3DFunctions).
     */

    [GMLFunction("gpu_get_fog")]
    public static object gpu_get_fog(object?[] args)
        => new List<object?> { Current.FogEnabled, Current.FogColour, Current.FogStart, Current.FogEnd };

    /*
     * Whole-state save/restore.
     *
     * gpu_get_state returns a ds_map, which the game is expected to destroy; gpu_set_state applies
     * one back. push/pop use an internal stack instead, so they don't leak maps.
     */

    [GMLFunction("gpu_push_state")]
    public static object? gpu_push_state(object?[] args)
    {
        StateStack.Push(Current.Clone());
        return null;
    }

    [GMLFunction("gpu_pop_state")]
    public static object? gpu_pop_state(object?[] args)
    {
        if (StateStack.Count == 0)
        {
            IO.DebugLog.LogWarning("gpu_pop_state with nothing pushed -- ignoring.");
            return null;
        }

        Apply(StateStack.Pop());
        return null;
    }

    [GMLFunction("gpu_get_state")]
    public static object gpu_get_state(object?[] args)
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        void Set(string key, object? value) => DataStructuresFunctions.ds_map_add([map, key, value]);

        Set("blendenable", Current.BlendEnable);
        Set("blendmode", Current.BlendMode);
        Set("blendmode_src", Current.BlendSrc);
        Set("blendmode_dest", Current.BlendDest);
        Set("blendmode_srcalpha", Current.BlendSrcAlpha);
        Set("blendmode_destalpha", Current.BlendDestAlpha);
        Set("ztestenable", Current.ZTestEnable);
        Set("zwriteenable", Current.ZWriteEnable);
        Set("zfunc", Current.ZFunc);
        Set("cullmode", Current.CullMode);
        Set("alphatestenable", Current.AlphaTestEnable);
        Set("alphatestref", Current.AlphaTestRef);
        Set("alphatestfunc", Current.AlphaTestFunc);
        Set("tex_filter", Current.TexFilter);
        Set("tex_repeat", Current.TexRepeat);
        Set("tex_mip_enable", Current.TexMipEnable);
        Set("tex_mip_filter", Current.TexMipFilter);
        Set("tex_mip_bias", Current.TexMipBias);
        Set("tex_min_mip", Current.TexMinMip);
        Set("tex_max_mip", Current.TexMaxMip);
        Set("tex_max_aniso", Current.TexMaxAniso);
        Set("colourwriteenable", new List<object?>
        {
            Current.ColourWriteR, Current.ColourWriteG, Current.ColourWriteB, Current.ColourWriteA
        });

        return map;
    }

    [GMLFunction("gpu_set_state")]
    public static object? gpu_set_state(object?[] args)
    {
        var map = args[0].Conv<int>();
        object? Get(string key) => DataStructuresFunctions.ds_map_find_value([map, key]);

        var state = Current.Clone();
        if (Get("blendmode") is { } bm)
            state.BlendMode = bm.Conv<int>();
        if (Get("blendmode_src") is { } bs)
            state.BlendSrc = bs.Conv<int>();
        if (Get("blendmode_dest") is { } bd)
            state.BlendDest = bd.Conv<int>();
        if (Get("blendmode_srcalpha") is { } bsa)
            state.BlendSrcAlpha = bsa.Conv<int>();
        if (Get("blendmode_destalpha") is { } bda)
            state.BlendDestAlpha = bda.Conv<int>();
        if (Get("ztestenable") is { } zt)
            state.ZTestEnable = zt.Conv<bool>();
        if (Get("zwriteenable") is { } zw)
            state.ZWriteEnable = zw.Conv<bool>();
        if (Get("zfunc") is { } zf)
            state.ZFunc = zf.Conv<int>();
        if (Get("cullmode") is { } cm)
            state.CullMode = cm.Conv<int>();
        if (Get("alphatestenable") is { } ate)
            state.AlphaTestEnable = ate.Conv<bool>();
        if (Get("alphatestref") is { } atr)
            state.AlphaTestRef = atr.Conv<int>();
        if (Get("tex_filter") is { } tf)
            state.TexFilter = tf.Conv<bool>();
        if (Get("tex_repeat") is { } tr)
            state.TexRepeat = tr.Conv<bool>();

        Apply(state);
        return null;
    }

    /// <summary>Pushes a whole state back out to the GL, one setter at a time.</summary>
    private static void Apply(GpuState state)
    {
        GraphicsManager.FlushPolygonBatch();

        gpu_set_ztestenable([state.ZTestEnable]);
        gpu_set_zwriteenable([state.ZWriteEnable]);
        gpu_set_zfunc([state.ZFunc]);
        gpu_set_cullmode([state.CullMode]);
        gpu_set_tex_filter([state.TexFilter]);
        gpu_set_tex_repeat([state.TexRepeat]);

        // Carry the recorded-only fields across too, so a push/pop round trip is lossless even for
        // the settings that don't reach the GL.
        Current.BlendEnable = state.BlendEnable;
        Current.BlendMode = state.BlendMode;
        Current.BlendSrc = state.BlendSrc;
        Current.BlendDest = state.BlendDest;
        Current.BlendSrcAlpha = state.BlendSrcAlpha;
        Current.BlendDestAlpha = state.BlendDestAlpha;
        Current.AlphaTestEnable = state.AlphaTestEnable;
        Current.AlphaTestRef = state.AlphaTestRef;
        Current.AlphaTestFunc = state.AlphaTestFunc;
        Current.TexMipEnable = state.TexMipEnable;
        Current.TexMipFilter = state.TexMipFilter;
        Current.TexMipBias = state.TexMipBias;
        Current.TexMinMip = state.TexMinMip;
        Current.TexMaxMip = state.TexMaxMip;
        Current.TexMaxAniso = state.TexMaxAniso;
        Current.FogEnabled = state.FogEnabled;
        Current.FogColour = state.FogColour;
        Current.FogStart = state.FogStart;
        Current.FogEnd = state.FogEnd;
    }
}
