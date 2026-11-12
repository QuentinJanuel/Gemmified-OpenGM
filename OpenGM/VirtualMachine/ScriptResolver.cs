using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenTK.Mathematics;
using System.Collections;
using System.Reflection;

namespace OpenGM.VirtualMachine;

public static class ScriptResolver
{
    public static Dictionary<string, VMScript> ScriptsByName = new();
    public static Dictionary<int, VMScript> ScriptsByIndex = new();
    public static List<VMCode> GlobalInit = new();

    public static Dictionary<string, GMLFunctionType> BuiltInFunctions = new();

    public static List<string> LoggedStubs = [];
    public static bool AlwaysLogStubs = false;

    public static void InitGMLFunctions()
    {
        if (BuiltInFunctions.Count > 0)
        {
            // already init'd
            return;
        }

        GMLFunctionType MakeStubFunction(GMLFunctionType function, string functionName, DebugLog.LogType stubLogType)
        {
            return (object?[] args) =>
            {
                if (AlwaysLogStubs || !LoggedStubs.Contains(functionName))
                {
                    if (!AlwaysLogStubs)
                    {
                        LoggedStubs.Add(functionName);
                    }
                    DebugLog.Log($"STUB: {functionName} not implemented.", stubLogType);
                }
                return function.Invoke(args);
            };
        }
        ;

        var assembly = Assembly.GetExecutingAssembly();
        var methods = assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(GMLFunctionAttribute), false).Length > 0)
            .ToArray();

        var addedCount = 0;
        var stubCount = 0;

        foreach (var methodInfo in methods)
        {
            var func = (GMLFunctionType)Delegate.CreateDelegate(typeof(GMLFunctionType), methodInfo);
            var attributes = (GMLFunctionAttribute[])methodInfo.GetCustomAttributes(typeof(GMLFunctionAttribute), false);

            foreach (var attribute in attributes)
            {
                if (attribute.AddedVersion != null && VersionManager.EngineVersion < attribute.AddedVersion)
                {
                    continue;
                }

                if (attribute.RemovedVersion != null && VersionManager.EngineVersion >= attribute.RemovedVersion)
                {
                    continue;
                }

                var newFunc = func;
                if (attribute.FunctionFlags.HasFlag(GMLFunctionFlags.Stub))
                {
                    newFunc = MakeStubFunction(func, attribute.FunctionName, attribute.StubLogType);
                    stubCount++;
                }

                BuiltInFunctions.Add(attribute.FunctionName, newFunc);
                addedCount++;
            }
        }

        var totalCount = BuiltInFunctions.Count;
        var realCount = addedCount - stubCount;
        DebugLog.LogInfo($"Registered {addedCount}/{totalCount} GML functions ({realCount} implemented, {stubCount} stubbed.)");
    }

    // custom opengm functions

    [GMLFunction("opengm_break", GMLFunctionFlags.OpenGM)]
    public static object? opengm_break(object?[] args)
    {
        System.Diagnostics.Debugger.Break();
        return null;
    }

    [GMLFunction("opengm_callstack", GMLFunctionFlags.OpenGM)]
    public static object? opengm_callstack(object?[] args)
    {
        DebugLog.PrintCallStack();
        return null;
    }

    [GMLFunction("opengm_enable_instruction_break", GMLFunctionFlags.OpenGM)]
    public static object? opengm_enable_instruction_break(object?[] args)
    {
        VMExecutor.BreakOnInstruction = true;
        return null;
    }

    [GMLFunction("opengm_disable_instruction_break", GMLFunctionFlags.OpenGM)]
    public static object? opengm_disable_instruction_break(object?[] args)
    {
        VMExecutor.BreakOnInstruction = false;
        return null;
    }

    // any functions in here aren't in 2022.500 so idk where they go rn

    [GMLFunction("draw_background", before: "2.0.0.0")]
    public static object? draw_background(object?[] args)
    {
        var index = args[0].Conv<int>();
        var x = args[1].Conv<double>();
        var y = args[2].Conv<double>();

        var background = GameLoader.Backgrounds[index];

        // TODO : handle tiling
        // TODO : handle strech
        // TODO : handle foreground
        // TODO : handle speed

        var sprite = background.Texture;

        if (sprite == null)
        {
            return null;
        }

        CustomWindow.Draw(new GMSpriteJob()
        {
            texture = sprite,
            screenPos = new(x, y),
            Colors = [Color4.White, Color4.White, Color4.White, Color4.White],
            scale = Vector2d.One,
            angle = 0,
            origin = Vector2.Zero
        });

        return null;
    }

    /// <summary>
    /// Repeats the background across the whole current view rather than drawing it once, with
    /// (x, y) acting as the tiling offset instead of a position. GM wraps the offset into the first
    /// tile up and left of the view, then covers the view from there.
    /// </summary>
    [GMLFunction("draw_background_tiled", before: "2.0.0.0")]
    public static object? draw_background_tiled(object?[] args)
    {
        var index = args[0].Conv<int>();
        var x = args[1].Conv<double>();
        var y = args[2].Conv<double>();

        var background = GameLoader.Backgrounds[index];
        var sprite = background.Texture;

        if (sprite == null)
        {
            return null;
        }

        var width = sprite.TargetWidth;
        var height = sprite.TargetHeight;

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var view = GraphicsManager.ViewArea;

        // Step back to the first tile that can still overlap the view's top-left corner. The double
        // modulo keeps this right for offsets to the left of / above the view, where a single one
        // would leave a gap of unfilled background along those edges.
        var startX = view.X + (((x - view.X) % width) - width) % width;
        var startY = view.Y + (((y - view.Y) % height) - height) % height;

        for (var drawY = startY; drawY < view.Y + view.W; drawY += height)
        {
            for (var drawX = startX; drawX < view.X + view.Z; drawX += width)
            {
                CustomWindow.Draw(new GMSpriteJob()
                {
                    texture = sprite,
                    screenPos = new(drawX, drawY),
                    Colors = [Color4.White, Color4.White, Color4.White, Color4.White],
                    scale = Vector2d.One,
                    angle = 0,
                    origin = Vector2.Zero
                });
            }
        }

        return null;
    }

    /// <summary>
    /// Helper for the background draw family: resolves the asset's texture, or null if the index or
    /// the texture is missing. Every one of these returns quietly in that case rather than throwing
    /// -- a missing background should not take the rest of the Draw event with it.
    /// </summary>
    private static SpritePageItem? BackgroundTexture(object? index)
        => GameLoader.Backgrounds.TryGetValue(index.Conv<int>(), out var bg) ? bg.Texture : null;

    /// <summary>Draws a sub-rectangle of the background at (x, y), unscaled and unblended.</summary>
    [GMLFunction("draw_background_part", before: "2.0.0.0")]
    public static object? draw_background_part(object?[] args)
    {
        return DrawBackgroundPartExt(args[0], args[1].Conv<int>(), args[2].Conv<int>(),
            args[3].Conv<int>(), args[4].Conv<int>(), args[5].Conv<double>(), args[6].Conv<double>(),
            1, 1, 0xFFFFFF, SpriteManager.DrawAlpha);
    }

    [GMLFunction("draw_background_part_ext", before: "2.0.0.0")]
    public static object? draw_background_part_ext(object?[] args)
    {
        return DrawBackgroundPartExt(args[0], args[1].Conv<int>(), args[2].Conv<int>(),
            args[3].Conv<int>(), args[4].Conv<int>(), args[5].Conv<double>(), args[6].Conv<double>(),
            args[7].Conv<double>(), args[8].Conv<double>(), args[9].Conv<int>(), args[10].Conv<double>());
    }

    private static object? DrawBackgroundPartExt(object? index, int left, int top, int width, int height,
        double x, double y, double xscale, double yscale, int blend, double alpha)
    {
        var sprite = BackgroundTexture(index);
        if (sprite == null)
        {
            return null;
        }

        var c = blend.ABGRToCol4(alpha);
        CustomWindow.Draw(new GMSpritePartJob
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
        return null;
    }

    /// <summary>
    /// Stretches the whole background to fill the given rectangle. Note the last two arguments are a
    /// width and a height, not a second corner.
    /// </summary>
    [GMLFunction("draw_background_stretched", before: "2.0.0.0")]
    public static object? draw_background_stretched(object?[] args)
        => DrawBackgroundStretchedExt(args[0], args[1].Conv<double>(), args[2].Conv<double>(),
            args[3].Conv<double>(), args[4].Conv<double>(), 0xFFFFFF, SpriteManager.DrawAlpha);

    [GMLFunction("draw_background_stretched_ext", before: "2.0.0.0")]
    public static object? draw_background_stretched_ext(object?[] args)
        => DrawBackgroundStretchedExt(args[0], args[1].Conv<double>(), args[2].Conv<double>(),
            args[3].Conv<double>(), args[4].Conv<double>(), args[5].Conv<int>(), args[6].Conv<double>());

    private static object? DrawBackgroundStretchedExt(object? index, double x, double y,
        double w, double h, int blend, double alpha)
    {
        var sprite = BackgroundTexture(index);
        if (sprite == null || sprite.TargetWidth <= 0 || sprite.TargetHeight <= 0)
        {
            return null;
        }

        var c = blend.ABGRToCol4(alpha);
        CustomWindow.Draw(new GMSpriteJob
        {
            texture = sprite,
            screenPos = new(x, y),
            Colors = [c, c, c, c],
            // Stretching is expressed as a scale, so the target size has to be divided back out by
            // the texture's own size rather than passed through.
            scale = new Vector2d(w / sprite.TargetWidth, h / sprite.TargetHeight),
            angle = 0,
            origin = Vector2.Zero
        });

        return null;
    }

    /// <summary>The everything-at-once variant: a sub-rectangle, scaled, rotated, per-corner blended.</summary>
    [GMLFunction("draw_background_general", before: "2.0.0.0")]
    public static object? draw_background_general(object?[] args)
    {
        var sprite = BackgroundTexture(args[0]);
        if (sprite == null)
        {
            return null;
        }

        var left = args[1].Conv<int>();
        var top = args[2].Conv<int>();
        var width = args[3].Conv<int>();
        var height = args[4].Conv<int>();
        var x = args[5].Conv<double>();
        var y = args[6].Conv<double>();
        var xscale = args[7].Conv<double>();
        var yscale = args[8].Conv<double>();
        var rot = args[9].Conv<double>();
        var alpha = args[14].Conv<double>();

        // Four corner colours, in GM's order: top-left, top-right, bottom-right, bottom-left.
        Color4[] colours =
        [
            args[10].Conv<int>().ABGRToCol4(alpha),
            args[11].Conv<int>().ABGRToCol4(alpha),
            args[12].Conv<int>().ABGRToCol4(alpha),
            args[13].Conv<int>().ABGRToCol4(alpha),
        ];

        CustomWindow.Draw(new GMSpritePartJob
        {
            texture = sprite,
            screenPos = new Vector2d(x, y),
            angle = rot,
            scale = new Vector2d(xscale, yscale),
            Colors = colours,
            origin = Vector2.Zero,
            left = left,
            top = top,
            width = width,
            height = height
        });
        return null;
    }

    /// <summary>Like draw_background_tiled but scaled and blended.</summary>
    [GMLFunction("draw_background_tiled_ext", before: "2.0.0.0")]
    public static object? draw_background_tiled_ext(object?[] args)
    {
        var sprite = BackgroundTexture(args[0]);
        if (sprite == null)
        {
            return null;
        }

        var x = args[1].Conv<double>();
        var y = args[2].Conv<double>();
        var xscale = args[3].Conv<double>();
        var yscale = args[4].Conv<double>();
        var blend = args[5].Conv<int>();
        var alpha = args[6].Conv<double>();

        var width = sprite.TargetWidth * xscale;
        var height = sprite.TargetHeight * yscale;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var view = GraphicsManager.ViewArea;
        // Same double-modulo wrap draw_background_tiled uses -- a single one leaves a gap along the
        // top/left edges when the offset is above or left of the view.
        var startX = view.X + (((x - view.X) % width) - width) % width;
        var startY = view.Y + (((y - view.Y) % height) - height) % height;
        var c = blend.ABGRToCol4(alpha);

        for (var drawY = startY; drawY < view.Y + view.W; drawY += height)
        {
            for (var drawX = startX; drawX < view.X + view.Z; drawX += width)
            {
                CustomWindow.Draw(new GMSpriteJob
                {
                    texture = sprite,
                    screenPos = new(drawX, drawY),
                    Colors = [c, c, c, c],
                    scale = new Vector2d(xscale, yscale),
                    angle = 0,
                    origin = Vector2.Zero
                });
            }
        }

        return null;
    }

    [GMLFunction("draw_background_ext", before: "2.0.0.0")]
    public static object? draw_background_ext(object?[] args)
    {
        var index = args[0].Conv<int>();
        var x = args[1].Conv<double>();
        var y = args[2].Conv<double>();
        var xscale = args[3].Conv<double>();
        var yscale = args[4].Conv<double>();
        var rot = args[5].Conv<double>();
        var blend = args[6].Conv<int>();
        var alpha = args[7].Conv<double>();

        var background = GameLoader.Backgrounds[index];

        // TODO : handle tiling
        // TODO : handle foreground
        // TODO : handle speed

        var sprite = background.Texture;

        if (sprite == null)
        {
            return null;
        }

        var c = blend.ABGRToCol4(alpha);

        CustomWindow.Draw(new GMSpriteJob()
        {
            texture = sprite,
            screenPos = new(x, y),
            Colors = [c, c, c, c],
            scale = new Vector2d(xscale, yscale),
            angle = rot,
            origin = Vector2.Zero
        });

        return null;
    }

    [GMLFunction("steam_initialised")]
    public static object? steam_initialised(object?[] args)
    {
        // todo : implement
        return false;
    }

    [GMLFunction("instance_create", before: "2.0.0.0")]
    public static object? instance_create(object?[] args)
    {
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();
        var obj = args[2].Conv<int>();

        return InstanceManager.instance_create(x, y, obj);
    }

    [GMLFunction("joystick_exists", GMLFunctionFlags.Stub)]
    public static object? joystick_exists(object?[] args) => false; // TODO : implement

    [GMLFunction("game_change")]
    public static object? game_change(object?[] args)
    {
        var working_directory = args[0].Conv<string>();
        var launch_parameters = args[1].Conv<string>();

        var winLocation = Path.Combine(Entry.DataWinFolder + working_directory, "data.win");
        DebugLog.LogInfo($"game_change working_directory:{working_directory} winLocation:{winLocation} launch_parameters:{launch_parameters}");

        Entry.LoadGame(winLocation, launch_parameters.Split(" "));

        return null;
    }

    [GMLFunction("string_split")]
    public static object? string_split(object?[] args)
    {
        var str = args[0].Conv<string>();
        var delimiter = args[1].Conv<string>();

        var remove_empty = false;
        if (args.Length > 2)
        {
            remove_empty = args[2].Conv<bool>();
        }

        var max_splits = -1;
        if (args.Length > 3)
        {
            max_splits = args[3].Conv<int>();
        }

        var option = remove_empty ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None;

        if (max_splits == -1)
        {
            return str.Split(delimiter, option).ToList();
        }
        else
        {
            return str.Split(delimiter, max_splits + 1, option).ToList();
        }
    }

    [GMLFunction("steam_is_screenshot_requested", GMLFunctionFlags.Stub)]
    public static object? steam_is_screenshot_requested(object?[] args)
    {
        // TODO : implement
        return false;
    }

    [GMLFunction("@@SetStatic@@")]
    public static object? SetStatic(object?[] args)
    {
        var currentFunc = VMExecutor.Call.Function;

        if (currentFunc == null)
        {
            // uhhhhh fuckin uhhh
            throw new NotImplementedException();
        }

        // TODO: wtf does this do

        //currentFunc.HasStaticInitRan = true;
        return null;
    }

    [GMLFunction("is_debug_overlay_open")]
    public static object? is_debug_overlay_open(object?[] args)
    {
        // TODO: implement? not stubbing out because it would spam the console
        return false;
    }

    [GMLFunction("is_mouse_over_debug_overlay")]
    public static object? is_mouse_over_debug_overlay(object?[] args)
    {
        // TODO: implement? not stubbing out because it would spam the console
        return false;
    }

    [GMLFunction("dbg_view", GMLFunctionFlags.Stub)]
    public static object? dbg_view(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_section", GMLFunctionFlags.Stub)]
    public static object? dbg_section(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_slider", GMLFunctionFlags.Stub)]
    public static object? dbg_slider(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_button", GMLFunctionFlags.Stub)]
    public static object? dbg_button(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_text", GMLFunctionFlags.Stub)]
    public static object? dbg_text(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_text_input", GMLFunctionFlags.Stub)]
    public static object? dbg_text_input(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_watch", GMLFunctionFlags.Stub)]
    public static object? dbg_watch(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_checkbox", GMLFunctionFlags.Stub)]
    public static object? dbg_checkbox(object?[] args)
    {
        return null;
    }

    [GMLFunction("dbg_color", GMLFunctionFlags.Stub)]
    public static object? dbg_color(object?[] args)
    {
        return null;
    }

    [GMLFunction("show_debug_overlay", GMLFunctionFlags.Stub)]
    public static object? show_debug_overlay(object?[] args)
    {
        return null;
    }

    [GMLFunction("ref_create", GMLFunctionFlags.Stub)]
    public static object? ref_create(object?[] args)
    {
        return null;
    }

    [GMLFunction("array_contains")]
    public static object? array_contains(object?[] args)
    {
        // TODO : test this
        var array = args[0].Conv<IList>();
        var value = args[1];

        var offset = 0;
        var length = array.Count;

        if (args.Length >= 3)
        {
            offset = args[2].Conv<int>();
        }

        if (args.Length >= 4)
        {
            length = args[3].Conv<int>();
        }

        var startingIndex = 0;
        if (offset < 0)
        {
            startingIndex = array.Count + offset;
        }
        else
        {
            startingIndex = offset;
        }

        startingIndex = Math.Clamp(startingIndex, 0, array.Count - 1);

        if (length > 0)
        {
            // normal iteration

            for (var i = 0; i < length; i++)
            {
                var checkIndex = startingIndex + i;

                if (i >= array.Count)
                {
                    continue;
                }

                if (Equals(array[checkIndex], value))
                {
                    return true;
                }
            }
        }
        else if (length == 0)
        {
            return false;
        }
        else
        {
            // backwards iteration

            for (var i = -length; i >= 0; i--)
            {
                var checkIndex = startingIndex + i;

                if (i >= array.Count)
                {
                    continue;
                }

                DebugLog.Log($"- index {checkIndex} - {array[checkIndex]} vs {value}");

                if (Equals(array[checkIndex], value))
                {
                    DebugLog.Log($"- MATCH");
                    return true;
                }
            }
        }

        return false;
    }

    [GMLFunction("@@string@@", GMLFunctionFlags.Stub)]
    public static object? js_string(object?[] args)
    {
        var format = args[0].Conv<string>();
        var values = args[1..].ConvAll<string>();

        return string.Format(format, values);
    }

    [GMLFunction("string_starts_with")]
    public static object? string_starts_with(object?[] args)
    {
        var str = args[0].Conv<string>();
        var substr = args[1].Conv<string>();
        // TODO: should this be case-sensitive or not?
        return str.StartsWith(substr);
    }

    public delegate object? GMLFunctionType(object?[] args);
}
