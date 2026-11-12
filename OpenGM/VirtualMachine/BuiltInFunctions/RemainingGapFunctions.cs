using OpenGM.IO;
using OpenGM.Rendering;
using System.Collections;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// The mechanical remainder of the name diff: ds_* type tagging and array conversion, the sprite and
/// surface property accessors, tile lookups, room queries and the emitter controls.
///
/// Two conventions run through the whole file:
///
///  - Where the engine genuinely holds the information, these read it. Where it doesn't, they return
///    GM's documented default and say so in a comment, rather than inventing a plausible-looking
///    number -- a wrong <c>sprite_get_speed</c> is harder to debug than an obviously-zero one.
///  - Nothing here throws. Every one of these used to be an unresolved call, which aborts the whole
///    calling event; a function that answers "0" for a missing asset is strictly better than one
///    that takes the rest of the Create event with it.
/// </summary>
public static class RemainingGapFunctions
{
    /*
     * ds_* type tagging.
     *
     * GM needs to know whether a ds_list entry is a nested list or map so that ds_map_write and the
     * JSON conversions can recurse. OpenGM stores nested structures as plain integer handles, which
     * is why these exist at all: the tag is the only way to tell a handle from an ordinary number.
     */

    private static readonly HashSet<(int Container, int Index)> ListsMarkedAsList = new();
    private static readonly HashSet<(int Container, int Index)> ListsMarkedAsMap = new();
    private static readonly HashSet<(int Container, object Key)> MapsMarkedAsList = new();
    private static readonly HashSet<(int Container, object Key)> MapsMarkedAsMap = new();

    public static void Reset()
    {
        ListsMarkedAsList.Clear();
        ListsMarkedAsMap.Clear();
        MapsMarkedAsList.Clear();
        MapsMarkedAsMap.Clear();
    }

    [GMLFunction("ds_list_mark_as_list")]
    public static object? ds_list_mark_as_list(object?[] args)
    {
        ListsMarkedAsList.Add((args[0].Conv<int>(), args[1].Conv<int>()));
        return null;
    }

    [GMLFunction("ds_list_mark_as_map")]
    public static object? ds_list_mark_as_map(object?[] args)
    {
        ListsMarkedAsMap.Add((args[0].Conv<int>(), args[1].Conv<int>()));
        return null;
    }

    [GMLFunction("ds_list_is_list")]
    public static object ds_list_is_list(object?[] args)
        => ListsMarkedAsList.Contains((args[0].Conv<int>(), args[1].Conv<int>()));

    [GMLFunction("ds_list_is_map")]
    public static object ds_list_is_map(object?[] args)
        => ListsMarkedAsMap.Contains((args[0].Conv<int>(), args[1].Conv<int>()));

    [GMLFunction("ds_map_add_list")]
    public static object? ds_map_add_list(object?[] args)
    {
        DataStructuresFunctions.ds_map_add([args[0], args[1], args[2]]);
        MapsMarkedAsList.Add((args[0].Conv<int>(), VMExecutor.DictHash(args[1])!));
        return null;
    }

    [GMLFunction("ds_map_add_map")]
    public static object? ds_map_add_map(object?[] args)
    {
        DataStructuresFunctions.ds_map_add([args[0], args[1], args[2]]);
        MapsMarkedAsMap.Add((args[0].Conv<int>(), VMExecutor.DictHash(args[1])!));
        return null;
    }

    [GMLFunction("ds_map_replace_list")]
    public static object? ds_map_replace_list(object?[] args)
    {
        DataStructuresFunctions.ds_map_replace([args[0], args[1], args[2]]);
        MapsMarkedAsList.Add((args[0].Conv<int>(), VMExecutor.DictHash(args[1])!));
        return null;
    }

    [GMLFunction("ds_map_replace_map")]
    public static object? ds_map_replace_map(object?[] args)
    {
        DataStructuresFunctions.ds_map_replace([args[0], args[1], args[2]]);
        MapsMarkedAsMap.Add((args[0].Conv<int>(), VMExecutor.DictHash(args[1])!));
        return null;
    }

    [GMLFunction("ds_map_is_list")]
    public static object ds_map_is_list(object?[] args)
        => MapsMarkedAsList.Contains((args[0].Conv<int>(), VMExecutor.DictHash(args[1])!));

    [GMLFunction("ds_map_is_map")]
    public static object ds_map_is_map(object?[] args)
        => MapsMarkedAsMap.Contains((args[0].Conv<int>(), VMExecutor.DictHash(args[1])!));

    [GMLFunction("ds_map_keys_to_array")]
    public static object ds_map_keys_to_array(object?[] args) => MapToArray(args, keys: true);

    [GMLFunction("ds_map_values_to_array")]
    public static object ds_map_values_to_array(object?[] args) => MapToArray(args, keys: false);

    private static object MapToArray(object?[] args, bool keys)
    {
        // Walks with find_first/find_next so the array comes out in the same order a manual walk
        // would -- otherwise a game that uses both would see two different orderings.
        var map = args[0].Conv<int>();
        var result = args.Length > 1 && args[1] is IList existing ? existing : new List<object?>();

        var key = DataStructuresFunctions.ds_map_find_first([map]);
        var guard = 0;
        while (key != null && guard++ < 1_000_000)
        {
            result.Add(keys ? key : DataStructuresFunctions.ds_map_find_value([map, key]));
            key = DataStructuresFunctions.ds_map_find_next([map, key]);
        }

        return result;
    }

    /// <summary>
    /// The set_pre/set_post family is GM's accessor plumbing for <c>list[| i] = v</c> and friends.
    /// The compiler lowers those accessors to the plain setters, so these are only ever reached by
    /// hand-written calls, and forwarding is exactly right.
    /// </summary>
    [GMLFunction("ds_list_set_pre")]
    [GMLFunction("ds_list_set_post")]
    public static object? ds_list_set(object?[] args) => DataStructuresFunctions.ds_list_set(args);

    [GMLFunction("ds_map_set_pre")]
    [GMLFunction("ds_map_set_post")]
    public static object? ds_map_set(object?[] args) => DataStructuresFunctions.ds_map_set(args);

    [GMLFunction("ds_grid_set_pre")]
    [GMLFunction("ds_grid_set_post")]
    public static object? ds_grid_set(object?[] args) => DataStructuresFunctions.ds_grid_set(args);

    /*
     * Sprite properties.
     */

    private static SerializedFiles.SpriteData? Sprite(object? arg)
        => SpriteManager._spriteDict.TryGetValue(arg.Conv<int>(), out var s) ? s : null;

    [GMLFunction("sprite_get_bbox_mode")]
    public static object sprite_get_bbox_mode(object?[] args) => Sprite(args[0])?.BBoxMode ?? 0;

    [GMLFunction("sprite_get_precise")]
    public static object sprite_get_precise(object?[] args)
        // "Precise" means a per-pixel mask rather than a rectangle -- which is exactly what a
        // non-AxisAlignedRect separation mask encodes.
        => Sprite(args[0])?.SepMasks != UndertaleModLib.Models.UndertaleSprite.SepMaskType.AxisAlignedRect;

    [GMLFunction("sprite_get_transparent")]
    public static object sprite_get_transparent(object?[] args) => true;

    [GMLFunction("sprite_get_smooth")]
    public static object sprite_get_smooth(object?[] args) => PageManager.TextureInterpolation;

    [GMLFunction("sprite_get_preload")]
    public static object sprite_get_preload(object?[] args) => true;

    /// <summary>
    /// The animation speed baked into the sprite asset. GMS 1.4 has no per-sprite speed -- it was
    /// added in 2.x -- so this reports 1 (one frame per step), which is what image_speed defaults to.
    /// </summary>
    [GMLFunction("sprite_get_speed")]
    public static object sprite_get_speed(object?[] args) => 1.0;

    // sprite_get_speed_type already exists in ResourceFunctions; sprite_get_tpe is its alias.
    [GMLFunction("sprite_get_tpe")]
    public static object sprite_get_speed_type(object?[] args) => 0;

    [GMLFunction("sprite_set_speed")]
    [GMLFunction("sprite_set_bbox_mode")]
    [GMLFunction("sprite_set_precise")]
    public static object? sprite_set_property(object?[] args) => null;

    [GMLFunction("sprite_set_bbox")]
    public static object? sprite_set_bbox(object?[] args)
    {
        var sprite = Sprite(args[0]);
        if (sprite == null)
        {
            return null;
        }

        // Writes the mask rectangle the collision code already reads, so this takes effect
        // immediately for every instance using the sprite.
        sprite.MarginLeft = args[1].Conv<int>();
        sprite.MarginTop = args[2].Conv<int>();
        sprite.MarginRight = args[3].Conv<int>();
        sprite.MarginBottom = args[4].Conv<int>();
        return null;
    }

    /// <summary>
    /// Texture cache management. OpenGM keeps every page resident for the life of the game, so
    /// there is nothing to flush or prefetch -- these are inert by construction rather than by
    /// omission, and a game calling them sees no difference beyond memory use.
    /// </summary>
    [GMLFunction("sprite_flush")]
    [GMLFunction("sprite_flush_multi")]
    [GMLFunction("sprite_prefetch_multi")]
    [GMLFunction("sprite_set_cache_size")]
    [GMLFunction("sprite_set_cache_size_ext")]
    [GMLFunction("texture_preload")]
    public static object? sprite_cache_noop(object?[] args) => null;

    [GMLFunction("texture_exists")]
    public static object texture_exists(object?[] args) => args[0] is SerializedFiles.SpritePageItem;

    [GMLFunction("texture_set_blending")]
    public static object? texture_set_blending(object?[] args) => null;

    [GMLFunction("texture_set_interpolation_ext")]
    public static object? texture_set_interpolation_ext(object?[] args)
        => GpuStateFunctions.gpu_set_tex_filter(args);

    [GMLFunction("texture_set_repeat_ext")]
    public static object? texture_set_repeat_ext(object?[] args)
        => GpuStateFunctions.gpu_set_tex_repeat(args);

    /// <summary>
    /// The sub-rectangle of the texture page a texture occupies, as
    /// [left, top, right, bottom, xoffset, yoffset, texelW, texelH]. Games use this to hand a
    /// sprite's real uv range to a shader.
    /// </summary>
    [GMLFunction("texture_get_uvs")]
    public static object texture_get_uvs(object?[] args)
    {
        if (args[0] is not SerializedFiles.SpritePageItem item
            || !PageManager.TexturePages.TryGetValue(item.Page, out var page))
        {
            return new List<object?> { 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 1.0 };
        }

        double w = page.image.Width, h = page.image.Height;
        return new List<object?>
        {
            item.SourceX / w, item.SourceY / h,
            (item.SourceX + item.SourceWidth) / w, (item.SourceY + item.SourceHeight) / h,
            (double)item.TargetX, (double)item.TargetY,
            1.0 / w, 1.0 / h,
        };
    }

    [GMLFunction("texture_global_scale")]
    public static object? texture_global_scale(object?[] args) => null;

    [GMLFunction("texture_debug_messages")]
    public static object? texture_debug_messages(object?[] args) => null;

    /*
     * Surfaces
     */

    /// <summary>
    /// surface_create_ext takes a format argument; OpenGM's surfaces are always RGBA8, so the format
    /// is accepted and ignored. A game asking for a float or single-channel surface gets an RGBA8
    /// one, which is visually identical for everything except HDR accumulation tricks.
    /// </summary>
    [GMLFunction("surface_create_ext")]
    public static object surface_create_ext(object?[] args)
        => GraphicFunctions.surface_create([args[0], args[1]]);

    [GMLFunction("surface_get_format")]
    public static object surface_get_format(object?[] args) => 0; // surface_rgba8unorm

    [GMLFunction("surface_format_is_supported")]
    public static object surface_format_is_supported(object?[] args) => args[0].Conv<int>() == 0;

    [GMLFunction("surface_get_depth_disable")]
    public static object surface_get_depth_disable(object?[] args) => true;

    [GMLFunction("surface_set_target_ext")]
    public static object? surface_set_target_ext(object?[] args)
        // Multiple render targets aren't wired up; target 0 is the only one that can be honoured.
        => args[0].Conv<int>() == 0 ? GraphicFunctions.surface_set_target([args[1]]) : null;

    [GMLFunction("surface_get_target_ext")]
    public static object surface_get_target_ext(object?[] args)
        => args[0].Conv<int>() == 0 ? GraphicFunctions.surface_get_target([]) ?? -1 : -1;

    /*
     * Tiles -- the loose-tile counters and lookups that complete the family.
     */

    [GMLFunction("tile_get_count")]
    public static object tile_get_count(object?[] args) => LayerFunctions.AllTilesPublic().Count();

    [GMLFunction("tile_get_id")]
    public static object tile_get_id(object?[] args)
    {
        var index = args[0].Conv<int>();
        var tiles = LayerFunctions.AllTilesPublic().ToList();
        return index >= 0 && index < tiles.Count ? tiles[index].instanceId : -1;
    }

    [GMLFunction("tile_get_ids")]
    public static object tile_get_ids(object?[] args)
        => LayerFunctions.AllTilesPublic().Select(t => (object?)t.instanceId).ToList();

    [GMLFunction("tile_get_ids_at_depth")]
    public static object tile_get_ids_at_depth(object?[] args)
    {
        var depth = args[0].Conv<int>();
        return LayerFunctions.AllTilesPublic().Where(t => t.depth == depth)
            .Select(t => (object?)t.instanceId).ToList();
    }

    /// <summary>The topmost tile whose rectangle covers the point, or -1.</summary>
    [GMLFunction("tile_find")]
    public static object tile_find(object?[] args)
    {
        var depth = args[0].Conv<int>();
        var x = args[1].Conv<double>();
        var y = args[2].Conv<double>();

        var hit = LayerFunctions.AllTilesPublic()
            .Where(t => t.depth == depth && x >= t.X && y >= t.Y && x < t.X + t.width && y < t.Y + t.height)
            .OrderByDescending(t => t.instanceId)
            .FirstOrDefault();

        return hit?.instanceId ?? -1;
    }

    [GMLFunction("tile_delete_at")]
    public static object? tile_delete_at(object?[] args)
    {
        var id = tile_find(args).Conv<int>();
        if (id != -1)
        {
            LayerFunctions.tile_delete([id]);
        }

        return null;
    }

    /*
     * Emitter controls added after the original part_emitter_* set.
     */

    [GMLFunction("part_emitter_enable")]
    public static object? part_emitter_enable(object?[] args)
    {
        if (Particles.ParticleManager.PartEmitters.TryGetValue(args[1].Conv<int>(), out var emitter))
        {
            // A disabled emitter keeps its settings but stops streaming, so re-enabling resumes
            // rather than restarting -- which is why this is a flag and not a number reset.
            emitter.Enabled = args[2].Conv<bool>();
        }

        return null;
    }

    [GMLFunction("part_particles_burst")]
    public static object? part_particles_burst(object?[] args)
        => ParticlesFunctions.part_particles_create(args);

    /*
     * Room queries.
     */

    [GMLFunction("room_get_info")]
    public static object room_get_info(object?[] args)
    {
        var map = DataStructuresFunctions.ds_map_create().Conv<int>();
        if (RoomManager.RoomList.TryGetValue(args[0].Conv<int>(), out var room))
        {
            DataStructuresFunctions.ds_map_add([map, "name", room.Name]);
            DataStructuresFunctions.ds_map_add([map, "width", room.SizeX]);
            DataStructuresFunctions.ds_map_add([map, "height", room.SizeY]);
            DataStructuresFunctions.ds_map_add([map, "persistent", room.Persistent]);
        }

        return map;
    }

    [GMLFunction("room_set_background_color")]
    [GMLFunction("room_set_background_colour")]
    public static object? room_set_background_color(object?[] args)
    {
        if (RoomManager.RoomList.TryGetValue(args[0].Conv<int>(), out var room))
        {
            room.BackgroundColor = (uint)args[1].Conv<int>();
            room.DrawBackgroundColor = args.Length <= 2 || args[2].Conv<bool>();
        }

        return null;
    }

    /*
     * Window / display odds and ends. These describe a real OS window; under the browser build there
     * is a canvas and nothing else, so the queries answer for the canvas and the setters that have
     * no browser equivalent are inert.
     */

    [GMLFunction("window_view_mouse_get_x")]
    public static object window_view_mouse_get_x(object?[] args) => GraphicFunctions.window_mouse_get_x([])!;

    [GMLFunction("window_view_mouse_get_y")]
    public static object window_view_mouse_get_y(object?[] args) => GraphicFunctions.window_mouse_get_y([])!;

    [GMLFunction("window_mouse_get_delta_x")]
    [GMLFunction("window_mouse_get_delta_y")]
    public static object window_mouse_get_delta(object?[] args) => 0;

    [GMLFunction("window_mouse_get_locked")]
    public static object window_mouse_get_locked(object?[] args) => false;

    [GMLFunction("window_mouse_set_locked")]
    [GMLFunction("window_mouse_set")]
    [GMLFunction("display_mouse_set")]
    public static object? window_mouse_set(object?[] args)
    {
        // Pointer lock and cursor warping both need a user gesture and a main-thread call in the
        // browser; neither is reachable from the worker the engine runs in.
        DebugLog.LogWarning("Moving or locking the mouse cursor isn't available in the browser build.");
        return null;
    }

    [GMLFunction("window_set_min_width")]
    [GMLFunction("window_set_max_width")]
    [GMLFunction("window_set_min_height")]
    [GMLFunction("window_set_max_height")]
    public static object? window_set_size_limit(object?[] args) => null;

    [GMLFunction("display_get_frequency")]
    public static object display_get_frequency(object?[] args) => 60;

    [GMLFunction("display_get_orientation")]
    public static object display_get_orientation(object?[] args) => 0; // display_landscape

    [GMLFunction("display_get_timing_method")]
    public static object display_get_timing_method(object?[] args) => 0;

    [GMLFunction("display_get_sleep_margin")]
    public static object display_get_sleep_margin(object?[] args) => 0;

    [GMLFunction("display_set_timing_method")]
    [GMLFunction("display_set_sleep_margin")]
    [GMLFunction("display_set_ui_visibility")]
    [GMLFunction("display_set_windows_alternate_sync")]
    [GMLFunction("display_set_windows_vertex_buffer_method")]
    public static object? display_set_noop(object?[] args) => null;

    [GMLFunction("display_get_windows_alternate_sync")]
    [GMLFunction("display_get_windows_vertex_buffer_method")]
    public static object display_get_windows(object?[] args) => 0;
}
