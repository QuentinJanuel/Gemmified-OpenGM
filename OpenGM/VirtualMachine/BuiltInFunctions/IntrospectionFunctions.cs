using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// Asset and instance introspection gaps, plus the filename/path string family.
///
/// Nothing here is complicated -- they're mostly one-liners over data the engine already holds --
/// but every one of them was an unresolved call, which aborts the whole event it appears in. That
/// makes a missing <c>object_get_solid</c> indistinguishable, from the game's point of view, from a
/// missing subsystem.
///
/// The filename functions are pinned to a capture from GMS 1.4.9999 rather than to intuition,
/// because three of the six do something slightly surprising -- see the comments there.
/// </summary>
public static class IntrospectionFunctions
{
    private static bool TryObject(object? arg, out SerializedFiles.ObjectDefinition def)
        => InstanceManager.ObjectDefinitions.TryGetValue(arg.Conv<int>(), out def!);

    /*
     * Object introspection
     */

    [GMLFunction("object_exists")]
    public static object object_exists(object?[] args) => TryObject(args[0], out _);

    // object_get_name/_sprite/_depth/_parent and object_is_ancestor already live in
    // ResourceFunctions; these are the rest of the getters plus the pre-Studio `object_name` alias.
    [GMLFunction("object_name")]
    public static object object_name(object?[] args)
        => TryObject(args[0], out var def) ? def.Name : "<undefined>";

    [GMLFunction("object_get_solid")]
    public static object object_get_solid(object?[] args)
        => TryObject(args[0], out var def) ? def.solid : false;

    [GMLFunction("object_get_visible")]
    public static object object_get_visible(object?[] args)
        => TryObject(args[0], out var def) ? def.visible : false;

    [GMLFunction("object_get_persistent")]
    public static object object_get_persistent(object?[] args)
        => TryObject(args[0], out var def) ? def.persistent : false;

    /// <summary>-1 when the object has no mask of its own, i.e. it uses its sprite.</summary>
    [GMLFunction("object_get_mask")]
    public static object object_get_mask(object?[] args)
        => TryObject(args[0], out var def) ? def.textureMaskId : -1;

    /*
     * Object mutation.
     *
     * These change the *definition*, so they affect instances created afterwards. Real GameMaker
     * does not retroactively rewrite instances that already exist -- each one copied the value into
     * its own variable when it was created -- and neither do these.
     */

    [GMLFunction("object_set_solid")]
    public static object? object_set_solid(object?[] args)
    {
        if (TryObject(args[0], out var def))
            def.solid = args[1].Conv<bool>();
        return null;
    }

    [GMLFunction("object_set_visible")]
    public static object? object_set_visible(object?[] args)
    {
        if (TryObject(args[0], out var def))
            def.visible = args[1].Conv<bool>();
        return null;
    }

    // object_set_depth and object_set_persistent are already registered in ResourceFunctions.

    [GMLFunction("object_set_sprite")]
    public static object? object_set_sprite(object?[] args)
    {
        if (TryObject(args[0], out var def))
            def.sprite = args[1].Conv<int>();
        return null;
    }

    [GMLFunction("object_set_mask")]
    public static object? object_set_mask(object?[] args)
    {
        if (TryObject(args[0], out var def))
            def.textureMaskId = args[1].Conv<int>();
        return null;
    }

    [GMLFunction("object_set_parent")]
    public static object? object_set_parent(object?[] args)
    {
        if (!TryObject(args[0], out var def))
        {
            return null;
        }

        var parentId = args[1].Conv<int>();

        // -100 is GameMaker's "no parent" sentinel here, the same value object_get_parent reports
        // (captured from 1.4.9999) -- not -1 and not noone.
        def.parent = parentId == -100 || !InstanceManager.ObjectDefinitions.TryGetValue(parentId, out var parent)
            ? null
            : parent;

        // Re-parenting changes which instance lists an object contributes to, and every by-object
        // lookup goes through that index -- the same trap instance_change fell into.
        InstanceManager.InitObjectMap();
        return null;
    }

    /*
     * Asset name lookups. Each is the pre-Studio spelling of a *_get_name that already exists.
     */

    [GMLFunction("script_get_name")]
    [GMLFunction("script_name")]
    public static object script_get_name(object?[] args)
    {
        var index = args[0].Conv<int>();
        return ScriptResolver.ScriptsByIndex.TryGetValue(index - GMConstants.FIRST_INSTANCE_ID, out var script)
            ? script.Name
            : "<undefined>";
    }

    /*
     * Instance queries
     */

    /// <summary>
    /// The instance of <c>obj</c> furthest from (x, y). Distance is measured from the instance's
    /// own x/y, not its bounding box -- the same metric instance_nearest uses.
    /// </summary>
    [GMLFunction("instance_furthest")]
    public static object instance_furthest(object?[] args)
    {
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();
        var obj = args[2].Conv<int>();

        var id = GMConstants.noone;
        var furthest = -1.0;

        foreach (var (_, instance) in InstanceManager.instances)
        {
            if (!InstanceManager.HasAssetInParents(instance.Definition, obj))
            {
                continue;
            }

            var dx = x - instance.x;
            var dy = y - instance.y;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (dist > furthest)
            {
                id = instance.instanceId;
                furthest = dist;
            }
        }

        return id;
    }

    /*
     * position_* -- "everything whose mask covers this point", as opposed to the instance_*
     * versions which answer with one instance.
     */

    [GMLFunction("position_destroy")]
    public static object? position_destroy(object?[] args)
    {
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();

        // Materialised before destroying: instance_destroy mutates the collection this walks.
        foreach (var instance in InstanceManager.instances.Values.ToList())
        {
            if (CollisionManager.Collision_Point(instance, x, y, precise: true))
            {
                InstanceManager.MarkForDestruction(instance, executeEvent: true);
            }
        }

        return null;
    }

    [GMLFunction("position_change")]
    public static object? position_change(object?[] args)
    {
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();
        var obj = args[2].Conv<int>();
        var performEvents = args[3].Conv<bool>();

        foreach (var instance in InstanceManager.instances.Values.ToList())
        {
            if (CollisionManager.Collision_Point(instance, x, y, precise: true))
            {
                GameFunctions.ChangeInstanceObject(instance, obj, performEvents);
            }
        }

        return null;
    }

    [GMLFunction("position_empty")]
    public static object position_empty(object?[] args)
    {
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();

        return !InstanceManager.instances.Values.Any(i => CollisionManager.Collision_Point(i, x, y, precise: true));
    }

    /*
     * The filename/path string family.
     *
     * Pure string manipulation, but three of the six are only obvious once measured. Captured from
     * GMS 1.4.9999 with "a/b/c.txt" (and "C:/b/c.txt" for the drive):
     *
     *   filename_ext   -> ".txt"    (leading dot included)
     *   filename_name  -> "c.txt"   (extension included)
     *   filename_dir   -> "a/b"     (no trailing separator)
     *   filename_path  -> "a/b\"    (trailing separator, and normalised to a BACKSLASH)
     *   filename_drive -> "C:"      (no separator)
     *   filename_change_ext(_, ".dat") -> "a/b/c.dat"
     *   filename_change_ext(_, "")     -> "a/b/c"
     *
     * filename_path and filename_dir differing only by a trailing separator -- of the opposite
     * slash kind to the input -- is the part nobody would guess.
     */

    private static int LastSeparator(string path) => Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));

    [GMLFunction("filename_name")]
    public static object filename_name(object?[] args)
    {
        var path = args[0].Conv<string>();
        return path[(LastSeparator(path) + 1)..];
    }

    [GMLFunction("filename_ext")]
    public static object filename_ext(object?[] args)
    {
        var path = args[0].Conv<string>();
        var name = path[(LastSeparator(path) + 1)..];
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }

    [GMLFunction("filename_dir")]
    public static object filename_dir(object?[] args)
    {
        var path = args[0].Conv<string>();
        var sep = LastSeparator(path);
        return sep < 0 ? "" : path[..sep];
    }

    [GMLFunction("filename_path")]
    public static object filename_path(object?[] args)
    {
        var path = args[0].Conv<string>();
        var sep = LastSeparator(path);
        // Everything up to and including the separator, but written back as a backslash whatever
        // the input used. Measured: "a/b/c.txt" gives "a/b\".
        return sep < 0 ? "" : path[..sep] + "\\";
    }

    [GMLFunction("filename_drive")]
    public static object filename_drive(object?[] args)
    {
        var path = args[0].Conv<string>();
        return path.Length >= 2 && path[1] == ':' ? path[..2] : "";
    }

    [GMLFunction("filename_change_ext")]
    public static object filename_change_ext(object?[] args)
    {
        var path = args[0].Conv<string>();
        var newExt = args[1].Conv<string>();

        var sep = LastSeparator(path);
        var dot = path.LastIndexOf('.');

        // A dot before the last separator belongs to a directory name, not to the file.
        return dot > sep ? path[..dot] + newExt : path + newExt;
    }

    /*
     * Small odds and ends, each of them previously an unresolved call.
     */

    /// <summary>
    /// Registered but inert. Blocking the thread is exactly what a browser build must never do --
    /// under wasm it would freeze the frame loop (and, in the worker, the audio bridge with it)
    /// rather than pausing the game. A game using it for a dramatic beat runs that beat instantly.
    /// </summary>
    [GMLFunction("sleep", GMLFunctionFlags.Stub)]
    public static object? sleep(object?[] args) => null;

    [GMLFunction("keyboard_clear")]
    public static object keyboard_clear(object?[] args)
    {
        var key = args[0].Conv<int>();
        var wasDown = InputHandler.KeyDown[key];
        InputHandler.KeyDown[key] = false;
        InputHandler.KeyPressed[key] = false;
        InputHandler.KeyReleased[key] = false;
        return wasDown;
    }

    [GMLFunction("mouse_clear")]
    public static object mouse_clear(object?[] args)
    {
        var button = args[0].Conv<int>();
        var wasDown = InputHandler.MouseDown[button];
        InputHandler.MouseDown[button] = false;
        InputHandler.MousePressed[button] = false;
        InputHandler.MouseReleased[button] = false;
        return wasDown;
    }
}

/// <summary>
/// Asset-table queries and runtime edits for fonts, rooms, sprites and backgrounds.
///
/// Split from <see cref="IntrospectionFunctions"/> only for size. As with the object family, these
/// are individually trivial -- but each one was an unresolved call, which aborts the entire event
/// it appears in, so a missing <c>font_exists</c> is as fatal to its Create event as a missing
/// subsystem would be.
/// </summary>
public static class AssetIntrospectionFunctions
{
    private const string Undefined = "<undefined>";

    /*
     * Fonts
     */

    private static FontAsset? Font(object? arg)
    {
        var index = arg.Conv<int>();
        return TextManager.FontAssets.FirstOrDefault(f => f.AssetIndex == index);
    }

    [GMLFunction("font_exists")]
    public static object font_exists(object?[] args) => Font(args[0]) != null;

    // font_get_name is already registered in GraphicFunctions; this is just the pre-Studio alias.
    [GMLFunction("font_name")]
    public static object font_name(object?[] args) => Font(args[0])?.name ?? Undefined;

    /// <summary>
    /// The name of the *typeface*, as opposed to the name of the font asset. OpenGM's FontAsset
    /// doesn't keep it separately, so this answers with the asset name -- which is what it is for
    /// a sprite font anyway, and close enough for the "show the player which font is active" use
    /// this gets put to.
    /// </summary>
    [GMLFunction("font_get_fontname")]
    public static object font_get_fontname(object?[] args) => Font(args[0])?.name ?? Undefined;

    // Bold and italic aren't carried through the converter, so these answer "no" rather than
    // guessing from the name. Registered because an unresolved call costs the whole event.
    [GMLFunction("font_get_bold")]
    public static object font_get_bold(object?[] args) => false;

    [GMLFunction("font_get_italic")]
    public static object font_get_italic(object?[] args) => false;

    /// <summary>First and last character codes the font actually has glyphs for.</summary>
    [GMLFunction("font_get_first")]
    public static object font_get_first(object?[] args)
    {
        var font = Font(args[0]);
        return font == null || font.entries.Count == 0 ? -1 : font.entries.Min(g => g.characterIndex);
    }

    [GMLFunction("font_get_last")]
    public static object font_get_last(object?[] args)
    {
        var font = Font(args[0]);
        return font == null || font.entries.Count == 0 ? -1 : font.entries.Max(g => g.characterIndex);
    }

    /*
     * Rooms
     */

    private static Room? RoomAsset(object? arg)
        => RoomManager.RoomList.TryGetValue(arg.Conv<int>(), out var room) ? room : null;

    [GMLFunction("room_name")]
    public static object room_name(object?[] args) => RoomAsset(args[0])?.Name ?? Undefined;

    // room_set_width/_height/_persistent and the background_exists/_get_width/_get_height trio are
    // already registered in ResourceFunctions.
    /*
     * Sprites and backgrounds -- the name lookups that were still missing.
     */

    [GMLFunction("sprite_name")]
    public static object sprite_name(object?[] args)
        => SpriteManager._spriteDict.TryGetValue(args[0].Conv<int>(), out var sprite) ? sprite.Name : Undefined;

    [GMLFunction("background_get_name")]
    [GMLFunction("background_name")]
    public static object background_get_name(object?[] args)
        => GameLoader.Backgrounds.TryGetValue(args[0].Conv<int>(), out var bg) ? bg.Name : Undefined;

}

/// <summary>
/// The last of the name-diff gaps: region instance activation, the loose-tile lookups, the legacy
/// variable-array accessors, and the effect_* family.
/// </summary>
public static class MiscGapFunctions
{
    /*
     * Region activation. instance_activate_region / _deactivate_region take a rectangle plus an
     * `inside` flag -- true acts on instances within the rectangle, false on everything outside it,
     * which is the form used to deactivate everything off-screen.
     *
     * Both walk InstanceManager.allInstances -- active AND inactive -- rather than only the list
     * they're moving instances out of, and both work purely in terms of NextActive. That matters
     * because activation is PENDING until the engine flushes it between frames (see
     * GamemakerObject.NextActive), while real GameMaker moves an instance the moment you ask. The
     * standard GM culling idiom does both halves in a single event:
     *
     *     instance_deactivate_object(objBlock);            // or instance_deactivate_all(true)
     *     instance_activate_region(view_x, view_y, view_w, view_h, true);
     *
     * Scanning only inactiveInstances here meant the freshly-deactivated instances -- still sitting
     * in `instances` with NextActive already false -- were invisible to the activate call, so
     * nothing was ever brought back and the whole object stayed deactivated for good. I wanna
     * CoinStack 1000 runs exactly this out of objTransitionCamera's User Event 0 on entering any
     * level room, so every solid block in the level was deactivated and the player fell straight
     * through the floor (the hub survived because its camera takes the followPlayer branch and
     * never calls the user event). Reading and writing only the pending flag makes the pair
     * commute the way it does in GM, whichever order the game calls them in.
     */

    private static bool InRegion(GamemakerObject inst, double left, double top, double width, double height)
    {
        // GM tests the instance's bounding box against the rectangle, not its origin -- an instance
        // straddling the edge counts as inside.
        var right = left + width;
        var bottom = top + height;
        return inst.bbox_right >= left && inst.bbox_left <= right
            && inst.bbox_bottom >= top && inst.bbox_top <= bottom;
    }

    [GMLFunction("instance_deactivate_region")]
    public static object? instance_deactivate_region(object?[] args)
    {
        var left = args[0].Conv<double>();
        var top = args[1].Conv<double>();
        var width = args[2].Conv<double>();
        var height = args[3].Conv<double>();
        var inside = args[4].Conv<bool>();
        var notme = args.Length > 5 && args[5].Conv<bool>();

        foreach (var inst in InstanceManager.allInstances.ToList())
        {
            if (notme && inst == VMExecutor.Self.GMSelf)
            {
                continue;
            }

            if (InRegion(inst, left, top, width, height) == inside)
            {
                // NextActive rather than a direct flip: the engine applies it between
                // frames, which is what stops an instance vanishing mid-event.
                inst.NextActive = false;
            }
        }

        return null;
    }

    [GMLFunction("instance_activate_region")]
    public static object? instance_activate_region(object?[] args)
    {
        var left = args[0].Conv<double>();
        var top = args[1].Conv<double>();
        var width = args[2].Conv<double>();
        var height = args[3].Conv<double>();
        var inside = args[4].Conv<bool>();

        foreach (var inst in InstanceManager.allInstances.ToList())
        {
            if (InRegion(inst, left, top, width, height) == inside)
            {
                inst.NextActive = true;
            }
        }

        return null;
    }

    /*
     * Legacy variable accessors. GM8 exposed array element access as its own functions; Studio kept
     * them for imported projects. `variable_global_array_get(name, index)` is `global.name[index]`.
     */

    [GMLFunction("variable_global_array_get")]
    public static object? variable_global_array_get(object?[] args)
        => ArrayElement(MiscFunctions.variable_global_get([args[0]]), args[1].Conv<int>());

    [GMLFunction("variable_global_array2_get")]
    public static object? variable_global_array2_get(object?[] args)
        => ArrayElement(ArrayElement(MiscFunctions.variable_global_get([args[0]]), args[1].Conv<int>()),
            args[2].Conv<int>());

    private static object? ArrayElement(object? array, int index)
        => array is System.Collections.IList list && index >= 0 && index < list.Count ? list[index] : null;

    /*
     * effect_* -- the built-in particle presets.
     *
     * Registered rather than implemented. The presets are a fixed set of sprite/particle recipes
     * (smoke, explosion, rain, snow, ...) that OpenGM has no assets for -- the built-in effect
     * sprites live in the runner, not in a game's data.win, so there is nothing to draw. Calling
     * these now does nothing instead of aborting the whole event, and the warning says what will be
     * missing on screen rather than leaving it a mystery.
     */

    [GMLFunction("effect_create_above", GMLFunctionFlags.Stub)]
    [GMLFunction("effect_create_below", GMLFunctionFlags.Stub)]
    public static object? effect_create(object?[] args)
    {
        DebugLog.LogWarning("effect_create_* is not implemented -- the built-in effect sprites ship "
                            + "with the runner rather than the game, so this effect will simply not appear.");
        return null;
    }

    [GMLFunction("effect_clear", GMLFunctionFlags.Stub)]
    public static object? effect_clear(object?[] args) => null;
}
