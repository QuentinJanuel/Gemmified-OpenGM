using OpenGM.VirtualMachine;

namespace OpenGM.IO;

/// <summary>
/// Real implementation of the "GMIni" extension's (GMIni.dll's) INI-file read/write functions --
/// same reasoning and persistence design as GMFileSystemExtension.cs (basename-keyed, real files
/// under Entry.DataWinFolder on native, localStorage on browser-wasm via BrowserFileSystem.cs).
/// GMIni.dll exports both a "current open file" API (ini_open/ini_read_string/.../ini_close,
/// operating on whichever file the last ini_open call opened) and an explicit "_ext" API taking the
/// filename on every call -- both are implemented here, sharing the same per-filename parsed
/// section/key cache, since a real INI file on disk only has one true state regardless of which API
/// touched it last.
/// </summary>
public static class GMIniExtension
{
    private class IniFile
    {
        public required string Name;
        public Dictionary<string, Dictionary<string, string>> Sections = new(StringComparer.OrdinalIgnoreCase);
        public bool Dirty;
    }

    private static readonly Dictionary<string, IniFile> _openFiles = new(StringComparer.OrdinalIgnoreCase);
    private static string? _currentFile;

    public static void Register(ExtensionFile file)
    {
        var implemented = new Dictionary<string, ScriptResolver.GMLFunctionType>
        {
            ["FS_ini_open"] = FS_ini_open,
            ["FS_ini_close"] = FS_ini_close,
            ["FS_ini_read_string"] = FS_ini_read_string,
            ["FS_ini_read_real"] = FS_ini_read_real,
            ["FS_ini_write_string"] = FS_ini_write_string,
            ["FS_ini_write_real"] = FS_ini_write_real,
            ["FS_ini_key_exists"] = FS_ini_key_exists,
            ["FS_ini_key_delete"] = FS_ini_key_delete,
            ["FS_ini_section_exists"] = FS_ini_section_exists,
            ["FS_ini_section_delete"] = FS_ini_section_delete,
            ["FS_ini_open_ext"] = FS_ini_open_ext,
            ["FS_ini_close_ext"] = FS_ini_close_ext,
            ["FS_ini_read_string_ext"] = FS_ini_read_string_ext,
            ["FS_ini_read_real_ext"] = FS_ini_read_real_ext,
            ["FS_ini_write_string_ext"] = FS_ini_write_string_ext,
            ["FS_ini_write_real_ext"] = FS_ini_write_real_ext,
            ["FS_ini_key_exists_ext"] = FS_ini_key_exists_ext,
            ["FS_ini_key_delete_ext"] = FS_ini_key_delete_ext,
            ["FS_ini_section_exists_ext"] = FS_ini_section_exists_ext,
            ["FS_ini_section_delete_ext"] = FS_ini_section_delete_ext,
        };

        foreach (var func in file.Functions)
        {
            var impl = implemented.GetValueOrDefault(func.Name) ?? ExtensionManager.MakeStubFunction(func.Name);
            if (!ScriptResolver.BuiltInFunctions.TryAdd(func.Name, impl))
            {
                DebugLog.LogError($"Could not add extension function {func.Name} to functions list");
            }
        }
    }

    private static string Basename(string path) => Path.GetFileName(path.Replace('\\', '/'));

    private static string? LoadPersistedText(string name)
    {
#if BROWSER
        var bytes = BrowserFileSystem.Load(name);
#else
        var path = Path.Combine(Entry.DataWinFolder, name);
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
#endif
        return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static void SavePersistedText(string name, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
#if BROWSER
        BrowserFileSystem.Save(name, bytes);
#else
        File.WriteAllBytes(Path.Combine(Entry.DataWinFolder, name), bytes);
#endif
    }

    /// <summary>
    /// Reads an INI file's text, preferring a real, staged bundled file at its full resolved path
    /// (e.g. a skin's skins/&lt;name&gt;/skin_config.ini shipped alongside data.win) over the persisted
    /// (localStorage on wasm) store -- exactly like GMFileSystemExtension.FS_file_text_open_read.
    /// The previous basename-only persisted lookup never found bundled config files at all (they're
    /// staged at a full subfolder path, not written to the persisted store), so every read of one
    /// silently returned the caller's default -- which, for a skin's colours, meant a game got
    /// GameMaker's own fallback strings (e.g. "0,0,255" read as HSV = white) instead of the skin's
    /// real values. The persisted store is still the fallback, for files the game itself wrote via
    /// FS_ini_write_* (e.g. its own config.ini), which never go to the read-only staged area.
    /// </summary>
    private static string? ReadIniText(string path)
    {
        var staged = Entry.ResolveDataWinPath(path);
        if (File.Exists(staged))
        {
            return File.ReadAllText(staged);
        }
        return LoadPersistedText(Basename(path));
    }

    private static IniFile GetOrLoad(string path)
    {
        // Keyed by the full resolved path, not basename -- otherwise two different skins'
        // skins/<name>/skin_config.ini (same basename, different folder) would collide in the cache,
        // and switching skins would keep serving the first one's contents.
        var key = Entry.ResolveDataWinPath(path);
        if (_openFiles.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var file = new IniFile { Name = Basename(path) };
        var text = ReadIniText(path);
        if (text != null)
        {
            ParseInto(file, text);
        }

        _openFiles[key] = file;
        return file;
    }

    private static void ParseInto(IniFile file, string text)
    {
        Dictionary<string, string>? currentSection = null;
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                var sectionName = trimmed[1..^1];
                if (!file.Sections.TryGetValue(sectionName, out currentSection))
                {
                    currentSection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    file.Sections[sectionName] = currentSection;
                }
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq < 0 || currentSection == null)
            {
                continue;
            }

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            currentSection[key] = value;
        }
    }

    private static string Serialize(IniFile file)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (sectionName, keys) in file.Sections)
        {
            sb.Append('[').Append(sectionName).Append(']').Append('\n');
            foreach (var (key, value) in keys)
            {
                sb.Append(key).Append('=').Append(value).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static void Flush(IniFile file)
    {
        if (file.Dirty)
        {
            SavePersistedText(file.Name, Serialize(file));
            file.Dirty = false;
        }
    }

    // -- "current open file" API ---------------------------------------------------------------

    private static object? FS_ini_open(object?[] args)
    {
        var path = args[0].Conv<string>();
        GetOrLoad(path); // populate the cache
        // Must match GetOrLoad's own cache key (the full resolved path), not the basename, or
        // FS_ini_close/Current() would look the just-opened file up under the wrong key.
        _currentFile = Entry.ResolveDataWinPath(path);
        return null;
    }

    private static object? FS_ini_close(object?[] args)
    {
        if (_currentFile != null && _openFiles.TryGetValue(_currentFile, out var file))
        {
            Flush(file);
        }
        _currentFile = null;
        return null;
    }

    private static IniFile? Current() => _currentFile != null ? _openFiles.GetValueOrDefault(_currentFile) : null;

    private static object? FS_ini_read_string(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        var def = args[2].Conv<string>();

        var file = Current();
        if (file != null && file.Sections.TryGetValue(section, out var keys) && keys.TryGetValue(key, out var value))
        {
            return value;
        }
        return def;
    }

    private static object? FS_ini_read_real(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        var def = args[2].Conv<double>();

        var file = Current();
        if (file != null && file.Sections.TryGetValue(section, out var keys) && keys.TryGetValue(key, out var value)
            && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return def;
    }

    private static object? FS_ini_write_string(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        var value = args[2].Conv<string>();

        var file = Current();
        if (file == null)
        {
            return null;
        }

        if (!file.Sections.TryGetValue(section, out var keys))
        {
            keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            file.Sections[section] = keys;
        }
        keys[key] = value;
        file.Dirty = true;
        return null;
    }

    private static object? FS_ini_write_real(object?[] args)
    {
        var value = args[2].Conv<double>();
        return FS_ini_write_string([args[0], args[1], value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    private static object? FS_ini_key_exists(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        var file = Current();
        return file != null && file.Sections.TryGetValue(section, out var keys) && keys.ContainsKey(key);
    }

    private static object? FS_ini_key_delete(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        var file = Current();
        if (file != null && file.Sections.TryGetValue(section, out var keys))
        {
            keys.Remove(key);
            file.Dirty = true;
        }
        return null;
    }

    private static object? FS_ini_section_exists(object?[] args)
    {
        var section = args[0].Conv<string>();
        var file = Current();
        return file != null && file.Sections.ContainsKey(section);
    }

    private static object? FS_ini_section_delete(object?[] args)
    {
        var section = args[0].Conv<string>();
        var file = Current();
        if (file != null)
        {
            file.Sections.Remove(section);
            file.Dirty = true;
        }
        return null;
    }

    // -- explicit-filename ("_ext") API ----------------------------------------------------------

    private static object? FS_ini_open_ext(object?[] args)
    {
        GetOrLoad(args[0].Conv<string>());
        return null;
    }

    private static object? FS_ini_close_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        Flush(file);
        return null;
    }

    private static object? FS_ini_read_string_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        var section = args[1].Conv<string>();
        var key = args[2].Conv<string>();
        var def = args[3].Conv<string>();
        if (file.Sections.TryGetValue(section, out var keys) && keys.TryGetValue(key, out var value))
        {
            return value;
        }
        return def;
    }

    private static object? FS_ini_read_real_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        var section = args[1].Conv<string>();
        var key = args[2].Conv<string>();
        var def = args[3].Conv<double>();
        if (file.Sections.TryGetValue(section, out var keys) && keys.TryGetValue(key, out var value)
            && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return def;
    }

    private static object? FS_ini_write_string_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        var section = args[1].Conv<string>();
        var key = args[2].Conv<string>();
        var value = args[3].Conv<string>();
        if (!file.Sections.TryGetValue(section, out var keys))
        {
            keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            file.Sections[section] = keys;
        }
        keys[key] = value;
        file.Dirty = true;
        return null;
    }

    private static object? FS_ini_write_real_ext(object?[] args)
    {
        var value = args[3].Conv<double>();
        return FS_ini_write_string_ext([args[0], args[1], args[2], value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    private static object? FS_ini_key_exists_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        var section = args[1].Conv<string>();
        var key = args[2].Conv<string>();
        return file.Sections.TryGetValue(section, out var keys) && keys.ContainsKey(key);
    }

    private static object? FS_ini_key_delete_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        var section = args[1].Conv<string>();
        var key = args[2].Conv<string>();
        if (file.Sections.TryGetValue(section, out var keys))
        {
            keys.Remove(key);
            file.Dirty = true;
        }
        return null;
    }

    private static object? FS_ini_section_exists_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        var section = args[1].Conv<string>();
        return file.Sections.ContainsKey(section);
    }

    private static object? FS_ini_section_delete_ext(object?[] args)
    {
        var file = GetOrLoad(args[0].Conv<string>());
        var section = args[1].Conv<string>();
        file.Sections.Remove(section);
        file.Dirty = true;
        return null;
    }
}
