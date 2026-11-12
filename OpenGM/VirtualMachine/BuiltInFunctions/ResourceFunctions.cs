using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenTK.Mathematics;
using System.Collections;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class ResourceFunctions
    {
        [GMLFunction("sprite_exists")]
        public static object sprite_exists(object?[] args)
        {
            var index = args[0].Conv<int>();
            return SpriteManager._spriteDict.ContainsKey(index);
        }

        [GMLFunction("sprite_get_name")]
        public static object? sprite_get_name(object?[] args)
        {
            var index = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(index, out var sprite))
            {
                return "";
            }

            return sprite.Name;
        }

        [GMLFunction("sprite_get_number")]
        public static object sprite_get_number(object?[] args)
        {
            var index = args[0].Conv<int>();
            return SpriteManager.GetNumberOfFrames(index);
        }

        [GMLFunction("sprite_get_width")]
        public static object sprite_get_width(object?[] args)
        {
            var index = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(index, out var sprite))
            {
                return 0;
            }

            return sprite.Width;
        }

        [GMLFunction("sprite_get_height")]
        public static object sprite_get_height(object?[] args)
        {
            var index = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(index, out var sprite))
            {
                return 0;
            }

            return sprite.Height;
        }

        [GMLFunction("sprite_get_xoffset")]
        public static object sprite_get_xoffset(object?[] args)
        {
            var index = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(index, out var sprite))
            {
                return 0;
            }

            return sprite.OriginX;
        }

        [GMLFunction("sprite_get_yoffset")]
        public static object sprite_get_yoffset(object?[] args)
        {
            var index = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(index, out var sprite))
            {
                return 0;
            }

            return sprite.OriginY;
        }

        // sprite_get_bbox_mode

        [GMLFunction("sprite_get_bbox_left")]
        public static object sprite_get_bbox_left(object?[] args)
        {
            var ind = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(ind, out var sprite))
            {
                return 0;
            }

            return sprite.MarginLeft;
        }

        [GMLFunction("sprite_get_bbox_right")]
        public static object sprite_get_bbox_right(object?[] args)
        {
            var ind = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(ind, out var sprite))
            {
                return 0;
            }

            return sprite.MarginRight;
        }

        [GMLFunction("sprite_get_bbox_top")]
        public static object sprite_get_bbox_top(object?[] args)
        {
            var ind = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(ind, out var sprite))
            {
                return 0;
            }

            return sprite.MarginTop;
        }

        [GMLFunction("sprite_get_bbox_bottom")]
        public static object sprite_get_bbox_bottom(object?[] args)
        {
            var ind = args[0].Conv<int>();
            if (!SpriteManager._spriteDict.TryGetValue(ind, out var sprite))
            {
                return 0;
            }

            return sprite.MarginBottom;
        }

        // sprite_collision_mask
        // sprite_set_cache_size
        // sprite_set_cache_size_ext
        // font_set_cache_size
        // sprite_get_tpe

        [GMLFunction("sprite_set_offset")]
        public static object? sprite_set_offset(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var xoff = args[1].Conv<int>();
            var yoff = args[2].Conv<int>();

            var data = SpriteManager._spriteDict[ind];
            data.OriginX = xoff;
            data.OriginY = yoff;
            return null;
        }

        // sprite_set_bbox_mode
        // sprite_set_bbox
        // sprite_set_alpha_from_sprite

        [GMLFunction("sprite_add")]
        public static object sprite_add(object?[] args)
        {
            var fname = args[0].Conv<string>();
            var imgnumber = args[1].Conv<int>();
            var removeback = args[2].Conv<bool>();
            var smooth = args[3].Conv<bool>();
            var xorig = args[4].Conv<int>();
            var yorig = args[5].Conv<int>();

            var path = Entry.ResolveDataWinPath(fname);
            return SpriteManager.sprite_add(path, imgnumber, removeback, smooth, xorig, yorig);
        }

        [GMLFunction("background_add")]
        public static object background_add(object?[] args)
        {
            var fname = args[0].Conv<string>();
            var transparent = args[1].Conv<bool>();
            var smooth = args[2].Conv<bool>();

            var path = Entry.ResolveDataWinPath(fname);
            return SpriteManager.background_add(path, transparent, smooth);
        }

        // Note these take a background ASSET index, unlike the background_width/background_height
        // variables, which index the current room's background slots.

        [GMLFunction("background_get_width")]
        public static object background_get_width(object?[] args)
        {
            var index = args[0].Conv<int>();

            return GameLoader.Backgrounds.TryGetValue(index, out var background)
                ? background.Texture?.BoundingWidth ?? 0
                : 0;
        }

        [GMLFunction("background_get_height")]
        public static object background_get_height(object?[] args)
        {
            var index = args[0].Conv<int>();

            return GameLoader.Backgrounds.TryGetValue(index, out var background)
                ? background.Texture?.BoundingHeight ?? 0
                : 0;
        }

        [GMLFunction("background_exists")]
        public static object background_exists(object?[] args)
        {
            var index = args[0].Conv<int>();
            return GameLoader.Backgrounds.ContainsKey(index);
        }

        [GMLFunction("background_assign")]
        public static object? background_assign(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var back = args[1].Conv<int>();

            SpriteManager.background_assign(ind, back);
            return null;
        }

        [GMLFunction("background_delete")]
        public static object background_delete(object?[] args)
        {
            var index = args[0].Conv<int>();
            return SpriteManager.background_delete(index);
        }

        [GMLFunction("background_create_from_surface")]
        public static object background_create_from_surface(object?[] args)
        {
            var surfaceId = args[0].Conv<int>();
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();
            var w = args[3].Conv<int>();
            var h = args[4].Conv<int>();
            var removeback = args[5].Conv<bool>();
            var smooth = args[6].Conv<bool>();

            return SpriteManager.background_create_from_surface(surfaceId, x, y, w, h, removeback, smooth);
        }

        [GMLFunction("sprite_create_from_surface")]
        public static object sprite_create_from_surface(object?[] args)
        {
            var index = args[0].Conv<int>();
            var x = args[1].Conv<int>(); // unused by html5. probably sourcepos but deltarune doesnt use it so idc to find out
            var y = args[2].Conv<int>();
            var w = args[3].Conv<int>();
            var h = args[4].Conv<int>();
            var removeback = args[5].Conv<bool>(); // TODO: implement
            var smooth = args[6].Conv<bool>();
            var xorig = args[7].Conv<int>();
            var yorig = args[8].Conv<int>();

            return SpriteManager.sprite_create_from_surface(index, x, y, w, h, removeback, smooth, xorig, yorig);
        }

        // sprite_sprite_add_from_surface
        // sprite_replace
        // sprite_save_strip

        [GMLFunction("sprite_delete")]
        public static object? sprite_delete(object?[] args)
        {
            var index = args[0].Conv<int>();

            return SpriteManager.sprite_delete(index);
        }

        // sprite_duplicate

        // Reassigns sprite index "ind" to render as sprite "spr"'s content -- copies spr's visual
        // data (dimensions/margins/origin/frames/collision masks) into ind's existing SpriteData
        // in place, keeping ind's own AssetIndex/Name identity so every other reference to it by
        // index keeps working, just now showing different pixels. Real GM semantics make ind an
        // independent copy, not a live alias -- see SpriteManager.CopySpriteTextures's own doc
        // comment for why that has to extend to the actual GPU texture, not just the SpritePageItem
        // list.
        [GMLFunction("sprite_assign")]
        public static object? sprite_assign(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var spr = args[1].Conv<int>();

            if (!SpriteManager._spriteDict.TryGetValue(ind, out var target) || !SpriteManager._spriteDict.TryGetValue(spr, out var source))
            {
                return null;
            }

            target.Width = source.Width;
            target.Height = source.Height;
            target.MarginLeft = source.MarginLeft;
            target.MarginRight = source.MarginRight;
            target.MarginBottom = source.MarginBottom;
            target.MarginTop = source.MarginTop;
            target.BBoxMode = source.BBoxMode;
            target.SepMasks = source.SepMasks;
            target.OriginX = source.OriginX;
            target.OriginY = source.OriginY;
            target.Textures = SpriteManager.CopySpriteTextures(source.Textures);
            target.CollisionMasks = source.CollisionMasks;
            target.PlaybackSpeed = source.PlaybackSpeed;
            target.PlaybackSpeedType = source.PlaybackSpeedType;

            return null;
        }

        // sprite_merge
        // sprite_save

        [GMLFunction("sprite_prefetch", GMLFunctionFlags.Stub)]
        public static object? sprite_prefetch(object?[] args)
        {
            // TODO : implement?
            return 0;
        }

        // sprite_prefetch_multi
        // sprite_flush
        // sprite_flush_multi
        // sprite_set_speed

        [GMLFunction("sprite_get_speed_type")]
        public static object? sprite_get_speed_type(object?[] args)
        {
            var index = args[0].Conv<int>();
            var asset = SpriteManager.GetSpriteAsset(index);

            if (asset == null)
            {
                throw new NotImplementedException();
            }

            return asset.PlaybackSpeedType;
        }

        // sprite_get_speed
        // sprite_get_nineslice
        // sprite_set_nineslice
        // sprite_nineslice_create

        [GMLFunction("texture_is_ready", GMLFunctionFlags.Stub)]
        public static object? texture_is_ready(object?[] args)
        {
            // todo : implement?
            return true;
        }

        [GMLFunction("texture_prefetch", GMLFunctionFlags.Stub)]
        public static object? texture_prefetch(object?[] args)
        {
            var tex_id = args[0].Conv<string>();
            // TODO : Implement? Or not?
            return null;
        }

        [GMLFunction("texture_flush", GMLFunctionFlags.Stub)]
        public static object? texture_flush(object?[] args)
        {
            var tex_id = args[0].Conv<string>();
            // TODO : Implement? Or not?
            return null;
        }

        [GMLFunction("texturegroup_get_textures")]
        public static object texturegroup_get_textures(object?[] args)
        {
            var tex_id = args[0].Conv<string>();

            if (!GameLoader.TexGroups.TryGetValue(tex_id, out var texGroup))
            {
                return Array.Empty<string>();
            }

            return texGroup.TexturePages;
        }

        // texturegroup_get_sprites
        // texturegroup_get_fonts
        // texturegroup_get_tilesets
        // texture_debug_messages
        // font_exists
        // font_get_name
        // font_get_fontname
        // font_get_size
        // font_get_bold
        // font_get_italic
        // font_get_first
        // font_get_last

        [GMLFunction("font_add_enable_aa", GMLFunctionFlags.Stub)]
        public static object? font_add_enable_aa(object?[] args)
        {
            // html runner also stubs these out so lol lmao
            return null;
        }

        [GMLFunction("font_add_get_enable_aa", GMLFunctionFlags.Stub)]
        public static object? font_add_get_enable_aa(object?[] args)
        {
            // html runner also stubs these out so lol lmao
            return false;
        }

        // font_add
        // font_add_sprite

        [GMLFunction("font_add_sprite")]
        public static object? font_add_sprite(object?[] args)
        {
            var spr = args[0].Conv<int>();
            var first = args[1].Conv<int>();
            var prop = args[2].Conv<bool>();
            var sep = args[3].Conv<int>();

            var spriteAsset = SpriteManager.GetSpriteAsset(spr)!;

            var index = AssetIndexManager.Register(AssetType.fonts, $"fnt_{spriteAsset.Name}");

            var newFont = new FontAsset
            {
                AssetIndex = index,
                name = $"fnt_{spriteAsset.Name}",
                spriteIndex = spr,
                sep = sep,
                Size = spriteAsset.Width,
                ScaleX = 1,
                ScaleY = 1
            };

            for (var i = 0; i < spriteAsset.Textures.Count; i++)
            {
                var page = SpriteManager.GetSpritePageItem(spr, i);

                var fontAssetEntry = new Glyph
                {
                    characterIndex = first + i,
                    frameIndex = i,
                    x = page.SourceX,
                    y = page.SourceY,
                    w = page.SourceWidth,
                    h = page.SourceHeight,
                    shift = page.SourceWidth,
                    // this looks wrong for some reason so commenting it out for now
                    // xOffset = page.TargetX,
                    yOffset = page.TargetY
                };

                newFont.entries.Add(fontAssetEntry);
                newFont.entriesDict[fontAssetEntry.characterIndex] = fontAssetEntry;
            }

            TextManager.FontAssets.Add(newFont);

            return newFont.AssetIndex;
        }

        [GMLFunction("font_add_sprite_ext")]
        public static object font_add_sprite_ext(object?[] args)
        {
            var spriteAssetIndex = args[0].Conv<int>();
            var string_map = args[1].Conv<string>();
            var prop = args[2].Conv<bool>();
            var sep = args[3].Conv<int>();

            var spriteAsset = SpriteManager.GetSpriteAsset(spriteAssetIndex)!;

            var index = AssetIndexManager.Register(AssetType.fonts, $"fnt_{spriteAsset.Name}");

            var newFont = new FontAsset
            {
                AssetIndex = index,
                name = $"fnt_{spriteAsset.Name}",
                spriteIndex = spriteAssetIndex,
                sep = sep,
                Size = spriteAsset.Width,
                ScaleX = 1,
                ScaleY = 1
            };

            for (var i = 0; i < string_map.Length; i++)
            {
                var page = SpriteManager.GetSpritePageItem(spriteAssetIndex, i);

                var fontAssetEntry = new Glyph
                {
                    characterIndex = string_map[i],
                    frameIndex = i,
                    x = page.SourceX,
                    y = page.SourceY,
                    w = page.SourceWidth,
                    h = page.SourceHeight,
                    shift = page.SourceWidth,
                    // this looks wrong for some reason so commenting it out for now
                    // xOffset = page.TargetX,
                    yOffset = page.TargetY
                };

                newFont.entries.Add(fontAssetEntry);
                newFont.entriesDict[fontAssetEntry.characterIndex] = fontAssetEntry;
            }

            TextManager.FontAssets.Add(newFont);

            return newFont.AssetIndex;
        }

        // font_replace_sprite
        // font_replace_sprite_ext
        // font_delete

        [GMLFunction("script_exists")]
        public static object? script_exists(object?[] args)
        {
            var scr = args[0].Conv<int>();

            if (CompatFlags.ScriptAssetIds)
            {
                return ScriptResolver.ScriptsByIndex.ContainsKey(scr);
            }

            if (scr < GMConstants.FIRST_INSTANCE_ID)
            {
                return ScriptResolver.BuiltInFunctions.Values.Count > scr;
            }

            return ScriptResolver.ScriptsByIndex.ContainsKey(scr - GMConstants.FIRST_INSTANCE_ID);
        }

        // script_get_name

        [GMLFunction("script_execute")]
        public static object? script_execute(object?[] args)
        {
            var scriptAssetId = args[0].Conv<int>();
            var scriptArgs = args[1..];

            if (CompatFlags.ScriptAssetIds)
            {
                return VMExecutor.ExecuteCode(ScriptResolver.ScriptsByIndex[scriptAssetId].GetCode(), VMExecutor.Self.Self, VMExecutor.Self.ObjectDefinition, args: scriptArgs);
            }

            if (scriptAssetId < GMConstants.FIRST_INSTANCE_ID)
            {
                var builtInMethod = ScriptResolver.BuiltInFunctions.Values.ElementAt(scriptAssetId);
                return builtInMethod(scriptArgs);
            }

            return VMExecutor.ExecuteCode(ScriptResolver.ScriptsByIndex[scriptAssetId - GMConstants.FIRST_INSTANCE_ID].GetCode(), VMExecutor.Self.Self, VMExecutor.Self.ObjectDefinition, args: scriptArgs);
        }

        // script_execute_ext

        [GMLFunction("path_exists")]
        public static object? path_exists(object?[] args)
        {
            var index = args[0].Conv<int>();

            return PathManager.Paths.ContainsKey(index);
        }

        [GMLFunction("path_get_name")]
        [GMLFunction("path_name")]
        public static object? path_get_name(object?[] args)
        {
            var index = args[0].Conv<int>();
            // "<undefined>" for a missing asset, the same string every other *_get_name answers
            // with rather than throwing.
            return PathManager.Paths.TryGetValue(index, out var path) ? path.name : "<undefined>";
        }

        [GMLFunction("path_get_length")]
        public static object? path_get_length(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1;
            }

            return (double)path.length;
        }

        [GMLFunction("path_get_kind")]
        public static object? path_get_kind(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1;
            }

            return path.kind;
        }

        [GMLFunction("path_get_closed")]
        public static object? path_get_closed(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1;
            }

            return path.closed;
        }

        [GMLFunction("path_get_precision")]
        public static object? path_get_precision(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1;
            }

            return path.precision;
        }

        [GMLFunction("path_get_number")]
        public static object? path_get_number(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1;
            }

            return path.points.Count;
        }

        [GMLFunction("path_get_point_x")]
        public static object? path_get_point_x(object?[] args)
        {
            var index = args[0].Conv<int>();
            // args[1], not args[0]: this read the path index as the point number, so every one of
            // these answered for point `index` -- for the usual path 0, always point 0.
            var n = args[1].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1; // this isn't documented anywhere, smh gamemaker
            }

            // Guard both ends: a negative point number would index the list backwards and throw.
            if (n < 0 || n >= path.points.Count)
            {
                return 0;
            }

            return path.points[n].x;
        }

        [GMLFunction("path_get_point_y")]
        public static object? path_get_point_y(object?[] args)
        {
            var index = args[0].Conv<int>();
            // args[1], not args[0]: this read the path index as the point number, so every one of
            // these answered for point `index` -- for the usual path 0, always point 0.
            var n = args[1].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1; // this isn't documented anywhere, smh gamemaker
            }

            // Guard both ends: a negative point number would index the list backwards and throw.
            if (n < 0 || n >= path.points.Count)
            {
                return 0;
            }

            return path.points[n].y;
        }

        [GMLFunction("path_get_point_speed")]
        public static object? path_get_point_speed(object?[] args)
        {
            var index = args[0].Conv<int>();
            // args[1], not args[0]: this read the path index as the point number, so every one of
            // these answered for point `index` -- for the usual path 0, always point 0.
            var n = args[1].Conv<int>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1; // this isn't documented anywhere, smh gamemaker
            }

            // Guard both ends: a negative point number would index the list backwards and throw.
            if (n < 0 || n >= path.points.Count)
            {
                return 0;
            }

            return path.points[n].speed;
        }

        [GMLFunction("path_get_x")]
        public static object? path_get_x(object?[] args)
        {
            var index = args[0].Conv<int>();
            // args[1], not args[0] -- this read the path index as the position, so every query
            // answered for offset `index` instead of the requested one (offset 0 for path 0).
            var pos = args[1].Conv<double>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1; // this isn't documented anywhere, smh gamemaker
            }

            return path.XPosition(pos);
        }

        [GMLFunction("path_get_y")]
        public static object? path_get_y(object?[] args)
        {
            var index = args[0].Conv<int>();
            // args[1], not args[0] -- this read the path index as the position, so every query
            // answered for offset `index` instead of the requested one (offset 0 for path 0).
            var pos = args[1].Conv<double>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1; // this isn't documented anywhere, smh gamemaker
            }

            return path.YPosition(pos);
        }

        [GMLFunction("path_get_speed")]
        public static object? path_get_speed(object?[] args)
        {
            var index = args[0].Conv<int>();
            var pos = args[1].Conv<double>();

            if (!PathManager.Paths.TryGetValue(index, out var path))
            {
                return -1;
            }

            return path.SpeedPosition(pos);
        }

        [GMLFunction("path_set_kind")]
        public static object? path_set_kind(object?[] args)
        {
            var index = args[0].Conv<int>();
            var val = args[1].Conv<int>();

            var path = PathManager.Paths[index];
            path.kind = val;
            return null;
        }

        [GMLFunction("path_set_closed")]
        public static object? path_set_closed(object?[] args)
        {
            var id = args[0].Conv<int>();
            var value = args[1].Conv<bool>();

            PathManager.Paths[id].closed = value;
            PathManager.ComputeInternal(PathManager.Paths[id]);
            return null;
        }

        [GMLFunction("path_set_precision")]
        public static object? path_set_precision(object?[] args)
        {
            var id = args[0].Conv<int>();
            var value = args[1].Conv<int>();

            PathManager.Paths[id].precision = value;
            PathManager.ComputeInternal(PathManager.Paths[id]);
            return null;
        }

        [GMLFunction("path_add")]
        public static object path_add(object?[] args)
        {
            return PathManager.PathAdd();
        }

        [GMLFunction("path_duplicate")]
        public static object path_duplicate(object?[] args)
        {
            var source = args[0].Conv<int>();

            var index = PathManager.PathAdd();
            if (PathManager.Paths.TryGetValue(source, out var from))
            {
                // Keep the fresh path's own generated name -- only the shape is duplicated.
                PathManager.CopyInto(PathManager.Paths[index], from);
            }

            return index;
        }

        [GMLFunction("path_assign")]
        public static object? path_assign(object?[] args)
        {
            var dest = args[0].Conv<int>();
            var source = args[1].Conv<int>();

            if (PathManager.Paths.TryGetValue(dest, out var to) && PathManager.Paths.TryGetValue(source, out var from))
            {
                PathManager.CopyInto(to, from);
            }

            return null;
        }

        [GMLFunction("path_append")]
        public static object? path_append(object?[] args)
        {
            var dest = args[0].Conv<int>();
            var source = args[1].Conv<int>();

            if (!PathManager.Paths.TryGetValue(dest, out var to) || !PathManager.Paths.TryGetValue(source, out var from))
            {
                return null;
            }

            // Snapshot the source points first: appending a path to itself would otherwise walk the
            // list it is growing and never terminate.
            foreach (var point in from.points.ToList())
            {
                PathManager.AddPoint(to, point.x, point.y, point.speed);
            }

            return null;
        }

        [GMLFunction("path_delete")]
        public static object? path_delete(object?[] args)
        {
            var index = args[0].Conv<int>();
            PathManager.PathDelete(index);
            return null;
        }

        [GMLFunction("path_add_point")]
        public static object? path_add_point(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<float>();
            var y = args[2].Conv<float>();
            var speed = args[3].Conv<float>();

            var path = PathManager.Paths[id];
            PathManager.AddPoint(path, x, y, speed);

            return null;
        }

        [GMLFunction("path_insert_point")]
        public static object? path_insert_point(object?[] args)
        {
            var id = args[0].Conv<int>();
            var n = args[1].Conv<int>();

            if (!PathManager.Paths.TryGetValue(id, out var path))
            {
                return null;
            }

            var point = new PathPoint
            {
                x = args[2].Conv<double>(),
                y = args[3].Conv<double>(),
                speed = args[4].Conv<double>(),
            };

            path.points.Insert(Math.Clamp(n, 0, path.points.Count), point);
            path.count = path.points.Count;
            PathManager.ComputeInternal(path);
            return null;
        }

        [GMLFunction("path_change_point")]
        public static object? path_change_point(object?[] args)
        {
            var id = args[0].Conv<int>();
            var n = args[1].Conv<int>();

            if (!PathManager.Paths.TryGetValue(id, out var path) || n < 0 || n >= path.points.Count)
            {
                return null;
            }

            path.points[n].x = args[2].Conv<double>();
            path.points[n].y = args[3].Conv<double>();
            path.points[n].speed = args[4].Conv<double>();
            PathManager.ComputeInternal(path);
            return null;
        }

        [GMLFunction("path_delete_point")]
        public static object? path_delete_point(object?[] args)
        {
            var id = args[0].Conv<int>();
            var n = args[1].Conv<int>();

            if (!PathManager.Paths.TryGetValue(id, out var path) || n < 0 || n >= path.points.Count)
            {
                return null;
            }

            path.points.RemoveAt(n);
            path.count = path.points.Count;
            PathManager.ComputeInternal(path);
            return null;
        }

        [GMLFunction("path_clear_points")]
        public static object? path_clear_points(object?[] args)
        {
            var id = args[0].Conv<int>();

            var path = PathManager.Paths[id];
            PathManager.Clear(path);
            return null;
        }

        /*
         * The geometric transforms. All of them except path_shift pivot around the centre of the
         * path's *bounding box* (PathManager.Center), not the average of its points and not the
         * room origin -- so scaling a path in place leaves it where it was.
         * Ground truth: OpenGMK's gml/kernel.rs.
         */

        [GMLFunction("path_reverse")]
        public static object? path_reverse(object?[] args)
        {
            if (PathManager.Paths.TryGetValue(args[0].Conv<int>(), out var path))
            {
                PathManager.Reverse(path);
            }

            return null;
        }

        [GMLFunction("path_mirror")]
        public static object? path_mirror(object?[] args)
        {
            if (PathManager.Paths.TryGetValue(args[0].Conv<int>(), out var path))
            {
                var (cx, _) = PathManager.Center(path);
                PathManager.TransformPoints(path, (x, y) => (cx - (x - cx), y));
            }

            return null;
        }

        [GMLFunction("path_flip")]
        public static object? path_flip(object?[] args)
        {
            if (PathManager.Paths.TryGetValue(args[0].Conv<int>(), out var path))
            {
                var (_, cy) = PathManager.Center(path);
                PathManager.TransformPoints(path, (x, y) => (x, cy - (y - cy)));
            }

            return null;
        }

        [GMLFunction("path_rotate")]
        public static object? path_rotate(object?[] args)
        {
            if (!PathManager.Paths.TryGetValue(args[0].Conv<int>(), out var path))
            {
                return null;
            }

            var angle = args[1].Conv<double>() * Math.PI / 180.0;
            // Negated sine: GML angles run counter-clockwise while y grows downwards.
            var sin = -Math.Sin(angle);
            var cos = Math.Cos(angle);

            var (cx, cy) = PathManager.Center(path);
            PathManager.TransformPoints(path, (x, y) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                return (cx + (dx * cos) - (dy * sin), cy + (dx * sin) + (dy * cos));
            });

            return null;
        }

        // path_scale is the pre-Studio spelling of the same function.
        [GMLFunction("path_rescale")]
        [GMLFunction("path_scale")]
        public static object? path_rescale(object?[] args)
        {
            if (!PathManager.Paths.TryGetValue(args[0].Conv<int>(), out var path))
            {
                return null;
            }

            var xscale = args[1].Conv<double>();
            var yscale = args[2].Conv<double>();

            var (cx, cy) = PathManager.Center(path);
            PathManager.TransformPoints(path, (x, y) => (cx + (xscale * (x - cx)), cy + (yscale * (y - cy))));

            return null;
        }

        [GMLFunction("path_shift")]
        public static object? path_shift(object?[] args)
        {
            if (!PathManager.Paths.TryGetValue(args[0].Conv<int>(), out var path))
            {
                return null;
            }

            var xshift = args[1].Conv<double>();
            var yshift = args[2].Conv<double>();
            PathManager.TransformPoints(path, (x, y) => (x + xshift, y + yshift));

            return null;
        }
        // object_exists

        #region Timelines

        [GMLFunction("timeline_exists")]
        public static object timeline_exists(object?[] args)
        {
            var index = args[0].Conv<int>();
            return GameLoader.Timelines.ContainsKey(index);
        }

        [GMLFunction("timeline_get_name")]
        [GMLFunction("timeline_name")]
        public static object timeline_get_name(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!GameLoader.Timelines.TryGetValue(index, out var timeline))
            {
                DebugLog.LogWarning($"timeline_get_name - timeline {index} doesn't exist!");
                return "<undefined>";
            }

            return timeline.Name;
        }

        [GMLFunction("timeline_size")]
        public static object timeline_size(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!GameLoader.Timelines.TryGetValue(index, out var timeline))
            {
                DebugLog.LogWarning($"timeline_size - timeline {index} doesn't exist!");
                return 0;
            }

            // The number of moments that have anything in them, not the timeline's length in steps.
            return timeline.Moments.Count;
        }

        [GMLFunction("timeline_max_moment")]
        public static object timeline_max_moment(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!GameLoader.Timelines.TryGetValue(index, out var timeline) || timeline.Moments.Count == 0)
            {
                return 0;
            }

            // Matches the length GamemakerObject.UpdateTimeline steps against.
            return timeline.Moments.Keys.Max();
        }

        [GMLFunction("timeline_add")]
        public static object timeline_add(object?[] args)
        {
            // Register needs the name up front but the index is what it hands back, so settle on a
            // free name first and let it assign whatever index follows the existing timelines.
            var name = $"__newtimeline{GameLoader.Timelines.Count}";
            for (var suffix = 0; AssetIndexManager.GetIndex(AssetType.timelines, name) != -1; suffix++)
            {
                name = $"__newtimeline{GameLoader.Timelines.Count}_{suffix}";
            }

            var index = AssetIndexManager.Register(AssetType.timelines, name);
            GameLoader.Timelines[index] = new Timeline { AssetId = index, Name = name };
            return index;
        }

        [GMLFunction("timeline_delete")]
        public static object? timeline_delete(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!GameLoader.Timelines.Remove(index, out var timeline))
            {
                DebugLog.LogWarning($"timeline_delete - timeline {index} doesn't exist!");
                return null;
            }

            AssetIndexManager.Unregister(AssetType.timelines, timeline.Name);

            // Instances still pointing at it just stop: UpdateTimeline's lookup misses and returns.
            return null;
        }

        [GMLFunction("timeline_clear")]
        public static object? timeline_clear(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (!GameLoader.Timelines.TryGetValue(index, out var timeline))
            {
                DebugLog.LogWarning($"timeline_clear - timeline {index} doesn't exist!");
                return null;
            }

            timeline.Moments.Clear();
            return null;
        }

        [GMLFunction("timeline_moment_clear")]
        public static object? timeline_moment_clear(object?[] args)
        {
            var index = args[0].Conv<int>();
            var step = args[1].Conv<int>();

            if (!GameLoader.Timelines.TryGetValue(index, out var timeline))
            {
                DebugLog.LogWarning($"timeline_moment_clear - timeline {index} doesn't exist!");
                return null;
            }

            // Clears every action at that step, not just the first.
            timeline.Moments.Remove(step);
            return null;
        }

        [GMLFunction("timeline_moment_add_script")]
        public static object? timeline_moment_add_script(object?[] args)
        {
            var index = args[0].Conv<int>();
            var step = args[1].Conv<int>();
            var scriptIndex = args[2].Conv<int>();

            if (!GameLoader.Timelines.TryGetValue(index, out var timeline))
            {
                DebugLog.LogWarning($"timeline_moment_add_script - timeline {index} doesn't exist!");
                return null;
            }

            if (!ScriptResolver.ScriptsByIndex.TryGetValue(scriptIndex, out var script))
            {
                DebugLog.LogWarning($"timeline_moment_add_script - script {scriptIndex} doesn't exist!");
                return null;
            }

            var code = script.GetCode();
            if (code == null)
            {
                DebugLog.LogWarning($"timeline_moment_add_script - script {script.Name} has no code!");
                return null;
            }

            // A negative step would never satisfy UpdateTimeline's window, so it'd sit there
            // silently never running -- say so rather than pretend it was added.
            if (step < 0)
            {
                DebugLog.LogWarning($"timeline_moment_add_script - step {step} is negative, ignoring!");
                return null;
            }

            // Adds to the moment rather than replacing it: several scripts can share one step, and
            // they run in the order they were added.
            if (!timeline.Moments.TryGetValue(step, out var actions))
            {
                actions = new List<VMCode>();
                timeline.Moments[step] = actions;
            }

            actions.Add(code);
            return null;
        }

        #endregion

        [GMLFunction("object_get_name")]
        public static object object_get_name(object?[] args)
        {
            var obj = args[0].Conv<int>();
            return InstanceManager.ObjectDefinitions[obj].Name;
        }

        [GMLFunction("object_get_sprite")]
        public static object object_get_sprite(object?[] args)
        {
            var obj = args[0].Conv<int>();
            return InstanceManager.ObjectDefinitions[obj].sprite;
        }

        // object_get_solid
        // object_get_visible
        // object_get_persistent
        // object_get_mask

        [GMLFunction("object_get_parent")]
        public static object? object_get_parent(object?[] args)
        {
            var obj = args[0].Conv<int>();

            if (!InstanceManager.ObjectDefinitions.TryGetValue(obj, out var objectDef))
            {
                return -1;
            }

            if (objectDef.parent == null)
            {
                return -100; // lmao
            }

            return objectDef.parent.AssetId;
        }

        // object_get_physics

        [GMLFunction("object_is_ancestor")]
        public static object object_is_ancestor(object?[] args)
        {
            var obj = args[0].Conv<int>();
            var par = args[1].Conv<int>();

            // Real GM fails soft (false) on a bad index rather than crashing -- games sometimes
            // pass through a value that isn't actually a valid object index here (e.g. jtool's
            // bundled TGMS Tweener library short-circuit-checking `_target.object_index == X ||
            // object_is_ancestor(_target.object_index, X)` where X can legitimately be an instance
            // id rather than an object index in some call modes).
            if (!InstanceManager.ObjectDefinitions.TryGetValue(obj, out var objDef) ||
                !InstanceManager.ObjectDefinitions.TryGetValue(par, out var parDef))
            {
                return false;
            }

            var currentParent = objDef;
            while (currentParent != null)
            {
                if (currentParent.AssetId == parDef.AssetId)
                {
                    return true;
                }

                currentParent = currentParent.parent;
            }

            return false;
        }

        // object_set_sprite
        // object_set_solid
        // object_set_visible

        [GMLFunction("object_set_persistent")]
        public static object? object_set_persistent(object?[] args)
        {
            var index = args[0].Conv<int>();
            var pers = args[1].Conv<bool>();

            if (InstanceManager.ObjectDefinitions.TryGetValue(index, out var definition))
            {
                definition.persistent = pers;
            }

            return null;
        }

        [GMLFunction("object_set_depth")]
        public static object? object_set_depth(object?[] args)
        {
            var index = args[0].Conv<int>();
            var depth = args[1].Conv<int>();

            if (InstanceManager.ObjectDefinitions.TryGetValue(index, out var definition))
            {
                definition.depth = depth;
            }

            return null;
        }

        [GMLFunction("object_get_depth")]
        public static object object_get_depth(object?[] args)
        {
            var index = args[0].Conv<int>();
            return InstanceManager.ObjectDefinitions.TryGetValue(index, out var definition) ? definition.depth : 0;
        }

        // object_set_mask
        // object_set_parent
        // object_add
        // object_delete
        // object_event_clear
        // object_event_add

        [GMLFunction("room_exists")]
        public static object? room_exists(object?[] args)
        {
            var index = args[0].Conv<int>();
            return RoomManager.RoomList.ContainsKey(index);
        }

        [GMLFunction("room_get_name")]
        public static object room_get_name(object?[] args)
        {
            var index = args[0].Conv<int>();

            return RoomManager.RoomList[index].Name;
        }

        [GMLFunction("room_set_width")]
        public static object? room_set_width(object?[] args)
        {
            var roomIndex = args[0].Conv<int>();
            var w = args[1].Conv<int>();

            RoomManager.RoomList[roomIndex].SizeX = w;
            return null;
        }

        [GMLFunction("room_set_height")]
        public static object? room_set_height(object?[] args)
        {
            var roomIndex = args[0].Conv<int>();
            var h = args[1].Conv<int>();

            RoomManager.RoomList[roomIndex].SizeY = h;
            return null;
        }

        // room_set_caption

        [GMLFunction("room_set_persistent")]
        public static object? room_set_persistent(object?[] args)
        {
            var roomIndex = args[0].Conv<int>();
            var persistent = args[1].Conv<bool>();

            var room = RoomManager.RoomList[roomIndex];
            room.Persistent = persistent;

            return null;
        }

        [GMLFunction("room_set_background")]
        public static object? room_set_background(object?[] args)
        {
            var roomIndex = args[0].Conv<int>();
            var ind = args[1].Conv<int>();
            var visible = args[2].Conv<bool>();
            var foreground = args[3].Conv<bool>();
            var background = args[4].Conv<int>();
            var x = args[5].Conv<int>();
            var y = args[6].Conv<int>();
            var tileHoriz = args[7].Conv<bool>();
            var tileVert = args[8].Conv<bool>();
            var hspeed = args[9].Conv<int>();
            var vspeed = args[10].Conv<int>();
            var stretch = args[11].Conv<bool>();

            var room = RoomManager.RoomList[roomIndex];
            var templateBg = room.OldBackgrounds[ind];
            templateBg.Enabled = visible;
            templateBg.Foreground = foreground;
            templateBg.Definition = background;
            templateBg.Position = new Vector2i(x, y);
            templateBg.TilingX = tileHoriz;
            templateBg.TilingY = tileVert;
            templateBg.Speed = new Vector2i(hspeed, vspeed);
            templateBg.Stretch = stretch;

            if (RoomManager.CurrentRoom.AssetId == roomIndex)
            {
                var liveBg = RoomManager.CurrentRoom.OldBackgrounds[ind];
                liveBg.Enabled = visible;
                liveBg.Foreground = foreground;
                liveBg.Definition = background;
                liveBg.Position = new Vector2i(x, y);
                liveBg.TilingX = tileHoriz;
                liveBg.TilingY = tileVert;
                liveBg.Speed = new Vector2i(hspeed, vspeed);
                liveBg.Stretch = stretch;
            }

            return null;
        }

        // room_set_background_color
        // room_set_background_colour
        // room_set_viewport
        // room_get_viewport
        // room_set_view_enabled
        // room_add
        // room_duplicate
        // room_assign
        // room_instance_add
        // room_instance_clear
        // room_get_camera
        // room_set_camera

        [GMLFunction("asset_get_index")]
        public static object asset_get_index(object?[] args)
        {
            var name = args[0].Conv<string>();
            return AssetIndexManager.GetIndex(name);
        }

        /// <summary>
        /// GML's asset_* constants, which are their own numbering -- not <see cref="AssetType"/>'s
        /// declaration order, which follows the data.win chunk layout instead.
        /// </summary>
        private static readonly Dictionary<AssetType, int> _assetTypeConstants = new()
        {
            [AssetType.objects] = 0,
            [AssetType.sprites] = 1,
            [AssetType.sounds] = 2,
            [AssetType.rooms] = 3,
            [AssetType.paths] = 4,
            [AssetType.scripts] = 5,
            [AssetType.fonts] = 6,
            [AssetType.timelines] = 7,
            [AssetType.backgrounds] = 8, // asset_tiles
            [AssetType.shaders] = 9,
            [AssetType.sequences] = 10,
            [AssetType.animcurves] = 11
        };

        [GMLFunction("asset_get_type")]
        public static object asset_get_type(object?[] args)
        {
            var name = args[0].Conv<string>();

            foreach (var (type, constant) in _assetTypeConstants)
            {
                if (AssetIndexManager.GetIndex(type, name) != -1)
                {
                    return constant;
                }
            }

            return -1; // asset_unknown
        }

        // ... sequence stuff

        // animcurve_get

        [GMLFunction("animcurve_get_channel")]
        public static object? animcurve_get_channel(object?[] args)
        {
            var curve_struct_or_id = args[0];
            var channel_name_or_index = args[1];

            RuntimeAnimCurve curveStruct;
            if (curve_struct_or_id is RuntimeAnimCurve _struct)
            {
                curveStruct = _struct;
            }
            else
            {
                curveStruct = GameLoader.AnimCurves[curve_struct_or_id.Conv<int>()];
            }

            if (channel_name_or_index is string str)
            {
                foreach (var channel in curveStruct.channels)
                {
                    if (channel.name == str)
                    {
                        return channel;
                    }
                }

                throw new($"Channel with name {str} not found.");
            }
            else
            {
                return curveStruct.channels[channel_name_or_index.Conv<int>()];
            }
        }

        // animcurve_get_channel_index

        [GMLFunction("animcurve_channel_evaluate")]
        public static object? animcurve_channel_evaluate(object?[] args)
        {
            var channel_struct = args[0].Conv<RuntimeAnimCurveChannel>();
            var posx = args[1].Conv<float>();
            return channel_struct.Evaluate(posx);
        }

        // ...
    }
}
