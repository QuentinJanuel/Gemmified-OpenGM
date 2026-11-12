using OpenGM.VirtualMachine;

namespace OpenGM.IO;

/// <summary>
/// Real implementations of the "GMFileSystem" extension's (GMFile.dll's) binary-file and
/// existence/delete/rename/copy functions, for platforms that can't load the real Windows DLL
/// (ExtensionManager.Init() otherwise just stubs every one of its functions out entirely there,
/// which is what a game's save/load system hitting real "undefined" values downstream looks like).
///
/// Storage differs by platform. On native it is real files under Entry.DataWinFolder (GMFile.dll
/// itself would just do real file I/O too). On browser-wasm it is the shared virtual filesystem --
/// the GEMS model OpenGMK's browser build also runs, in OpenGM.Wasm/Vfs.cs via BrowserFileSystem.cs
/// -- where a read spans the read-only `bundle` (the uploaded game folder)
/// and the persisted `store` (whatever the game has written), and a write always lands in the store.
///
/// Note for anyone tracing an old bug: this used to be basename-keyed, with a *separate* full-path
/// resolution for staged read-only assets, and picking between the two at each call site. That is
/// why `Save\game1` and `Backup\game1` could be one saved file, and why file_exists and
/// file_bin_open could disagree. Both layers are now one overlay behind one Resolve().
/// </summary>
public static class GMFileSystemExtension
{
    private class Handle
    {
        public required string? Name;
        public required List<byte> Buffer;
        public int Position;
        public bool Writable;
    }

    /// <summary>
    /// Unlike Handle (file_bin_*), the whole file is held as one string with a plain
    /// character-index Position, because every read here is a substring of it.
    ///
    /// Position is a *read cursor within* that string, not proof that a single read returns all of
    /// it: FS_file_text_read_string is GM's file_text_read_string, which stops at the end of the
    /// line. See that method for the bug reading the whole file caused.
    /// </summary>
    private class TextHandle
    {
        public required string? Name;
        public required string Content;
        public int Position;
        public bool Writable;
    }

    private static readonly Dictionary<int, Handle> _handles = new();
    private static readonly Dictionary<int, TextHandle> _textHandles = new();
    private static int _nextHandle = 1;

    public static void Register(ExtensionFile file)
    {
        var implemented = new Dictionary<string, ScriptResolver.GMLFunctionType>
        {
            ["FS_file_bin_open"] = FS_file_bin_open,
            ["FS_file_bin_read_byte"] = FS_file_bin_read_byte,
            ["FS_file_bin_write_byte"] = FS_file_bin_write_byte,
            ["FS_file_bin_close"] = FS_file_bin_close,
            ["FS_file_bin_seek"] = FS_file_bin_seek,
            ["FS_file_bin_seek_relative"] = FS_file_bin_seek_relative,
            ["FS_file_bin_position"] = FS_file_bin_position,
            ["FS_file_bin_size"] = FS_file_bin_size,
            ["FS_file_bin_eof"] = FS_file_bin_eof,
            ["FS_file_bin_good"] = FS_file_bin_good,
            ["FS_file_bin_bad"] = FS_file_bin_bad,
            ["FS_file_bin_fail"] = FS_file_bin_fail,
            ["FS_file_bin_clear_bad"] = FS_file_bin_clear_bad,
            ["FS_file_bin_clear_fail"] = FS_file_bin_clear_fail,
            ["FS_file_bin_read_word"] = FS_file_bin_read_word,
            ["FS_file_bin_read_dword"] = FS_file_bin_read_dword,
            ["FS_file_bin_write_word"] = FS_file_bin_write_word,
            ["FS_file_bin_write_dword"] = FS_file_bin_write_dword,
            ["FS_file_exists"] = FS_file_exists,
            ["FS_file_delete"] = FS_file_delete,
            ["FS_file_rename"] = FS_file_rename,
            ["FS_file_copy"] = FS_file_copy,
            ["FS_max_open"] = FS_max_open,
            ["FS_directory_exists"] = FS_directory_exists,
            ["FS_directory_create"] = FS_directory_create,
            ["FS_directory_delete"] = FS_directory_delete,
            ["FS_file_text_open_read"] = FS_file_text_open_read,
            ["FS_file_text_open_write"] = FS_file_text_open_write,
            ["FS_file_text_open_append"] = FS_file_text_open_append,
            ["FS_file_text_close"] = FS_file_text_close,
            ["FS_file_text_read_string"] = FS_file_text_read_string,
            ["FS_file_text_read_real"] = FS_file_text_read_real,
            ["FS_file_text_read_char"] = FS_file_text_read_char,
            ["FS_file_text_readln"] = FS_file_text_readln,
            ["FS_file_text_unread"] = FS_file_text_unread,
            ["FS_file_text_write_string"] = FS_file_text_write_string,
            ["FS_file_text_write_real"] = FS_file_text_write_real,
            ["FS_file_text_writeln"] = FS_file_text_writeln,
            ["FS_file_text_eof"] = FS_file_text_eof,
            ["FS_file_text_eoln"] = FS_file_text_eoln,
            ["FS_file_text_good"] = FS_file_bin_good,
            ["FS_file_text_bad"] = FS_file_bin_bad,
            ["FS_file_text_fail"] = FS_file_bin_fail,
            ["FS_file_text_clear_bad"] = FS_file_bin_clear_bad,
            ["FS_file_text_clear_fail"] = FS_file_bin_clear_fail,
            ["FS_file_text_set_endl"] = FS_noop,
            ["FS_file_text_set_endl_posix"] = FS_noop,
            ["FS_file_text_set_endl_windows"] = FS_noop,
            ["FS_file_text_write_bom"] = FS_noop,
            ["FS_file_text_write_flush"] = FS_noop,
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

    /// <summary>
    /// Resolves a GML-supplied path (games author these with Windows backslashes) to the full path
    /// the storage layer takes.
    ///
    /// There used to be TWO resolutions here and choosing between them was a per-call-site
    /// judgement: a `Basename` that discarded directories entirely (used for anything persisted)
    /// and this full-path one (used for read-only staged assets). That split is what made
    /// `Save\game1` and `Backup\game1` the same saved file, and what let `file_exists` and
    /// `file_bin_open` disagree about the same path. On the browser build both layers are now the
    /// shared VFS's `bundle` and `store`, resolved by one overlay, so there is one resolution.
    /// </summary>
    private static string Resolve(string path) => Entry.ResolveDataWinPath(path);

    /// <summary>
    /// Where a WRITE goes, as opposed to where a bundled asset is read from. Under GameMaker
    /// Studio's sandbox those are different places: reads may come from the game folder, but every
    /// write lands in the save area (%LOCALAPPDATA%\&lt;game name&gt;\) with its relative position
    /// preserved. Keeping them distinct is what lets the page's file browser report the location a
    /// file would actually occupy on a real Windows install.
    /// </summary>
    private static string? SaveResolve(string path)
    {
#if BROWSER
        return LoadSave.ResolveSavePath(path);
#else
        return Entry.ResolveDataWinPath(path);
#endif
    }

    // internal (not private): also used directly by FileFunctions.cs's plain file_text_* builtins
    // (as opposed to this extension's own FS_file_text_* functions), so both file APIs share one
    // store instead of file_text_* silently writing through raw System.IO -- which under
    // browser-wasm lands in the ephemeral in-memory MEMFS, so saves neither show in the page's file
    // browser nor survive a reload.
    //
    // On BROWSER these go through the shared VFS, whose read already spans both layers: a game's
    // own write if it has one, the uploaded/bundled original otherwise. Callers therefore no longer
    // check a staged path and a persisted name separately.
    // A null path is one SaveResolve refused: nothing is there and nothing can be put there.
    internal static byte[]? LoadPersisted(string? path)
    {
        if (path == null)
        {
            return null;
        }

#if BROWSER
        return BrowserFileSystem.Load(path);
#else
        var full = Path.Combine(Entry.DataWinFolder, path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
#endif
    }

    internal static void SavePersisted(string? path, byte[] data)
    {
        if (path == null)
        {
            return;
        }

#if BROWSER
        BrowserFileSystem.Save(path, data);
#else
        var full = Path.Combine(Entry.DataWinFolder, path);

        // Games write saves into a subfolder of the save area ("Data\save1") without ever calling
        // directory_create -- GameMaker's sandbox materialises the folder as part of the write, and
        // so does the BROWSER branch above, whose VFS resolves a path into a tree. Native
        // File.WriteAllBytes is the only one of the three that doesn't, so the exact write that
        // works in the shipping build threw DirectoryNotFoundException here.
        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllBytes(full, data);
#endif
    }

    internal static bool ExistsPersisted(string? path)
    {
        if (path == null)
        {
            return false;
        }

#if BROWSER
        return BrowserFileSystem.Exists(path);
#else
        return File.Exists(Path.Combine(Entry.DataWinFolder, path));
#endif
    }

    internal static void RemovePersisted(string? path)
    {
        if (path == null)
        {
            return;
        }

#if BROWSER
        BrowserFileSystem.Remove(path);
#else
        var full = Path.Combine(Entry.DataWinFolder, path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
#endif
    }

    /// <summary>
    /// Whether a file exists, in either layer. On BROWSER one VFS call covers both; on native the
    /// staged file and the written file are genuinely two places on disk.
    /// </summary>
    private static bool AnyExists(string path)
    {
#if BROWSER
        // Two lookups, not one. Under GMS's sandbox a read checks the save area first and then the
        // game's own shipped files, and since this refactor those are *different mounts* in the VFS
        // (/appdata/<game>/... vs /game/...), so the overlay cannot merge them for us -- it resolves
        // store-over-bundle within a single path, which these no longer share.
        return ExistsPersisted(SaveResolve(path)) || ExistsPersisted(Resolve(path));
#else
        return File.Exists(Resolve(path)) || ExistsPersisted(Resolve(path));
#endif
    }

    /// <summary>Reads a file from either layer, or null if it is in neither.</summary>
    private static byte[]? ReadAny(string path)
    {
#if BROWSER
        // Save area wins over the shipped copy -- a game's own write shadows what came with it --
        // then fall back to the game folder. See AnyExists for why this is two lookups.
        return LoadPersisted(SaveResolve(path)) ?? LoadPersisted(Resolve(path));
#else
        var staged = Resolve(path);
        return File.Exists(staged) ? File.ReadAllBytes(staged) : LoadPersisted(staged);
#endif
    }

    /// <summary>
    /// Everywhere a file a game asked for can actually be, in the order it should be looked for:
    /// the game's own writes, then the files it shipped with, then the real filesystem.
    ///
    /// Null when it is in none of them.
    ///
    /// The third is not redundant. Anything written before <see cref="WriteGameFile"/> existed --
    /// or by a build that still writes straight through System.IO -- sits in the wasm's in-memory
    /// filesystem and in neither store, so dropping it would lose a file the same session had just
    /// created. A game cannot tell these apart, and should not have to: from inside, it is one
    /// filesystem.
    /// </summary>
    internal static byte[]? ReadGameFile(string path)
    {
        var overlay = ReadAny(path);
        if (overlay != null)
        {
            return overlay;
        }

        var real = Resolve(path);
        return File.Exists(real) ? File.ReadAllBytes(real) : null;
    }

    /// <summary>
    /// Where a game's own write goes: the save area, through the persisted store.
    ///
    /// The same place every other write in this engine lands, which is the point. A write that goes
    /// around this -- straight through System.IO -- lands in the wasm's in-memory filesystem, where
    /// it does not show in the page's file browser, does not survive a reload, and is not the place
    /// a read looks first. That is a save the player believes they made and does not have.
    /// </summary>
    internal static void WriteGameFile(string path, byte[] data) =>
        SavePersisted(SaveResolve(path), data);

    private static object? FS_file_bin_open(object?[] args)
    {
        var path = args[0].Conv<string>();
        var mode = args[1].Conv<int>(); // 0 = read-only, non-zero = writable (matches GM's own file_bin_open)
        var name = SaveResolve(path);
        if (name == null && mode != 0)
        {
            return -1d; // refused by the save-path rules: fails as an uncreatable file does
        }

        // Real GM8 binary write mode never truncates on open -- it preserves any existing content
        // beyond wherever this write actually reaches, so a game can open an already-larger save
        // file, seek partway in, and overwrite just one field without disturbing the rest. Loading
        // any existing persisted bytes here (for every mode, not just read) matches that.
        var buffer = LoadPersisted(name)?.ToList() ?? new List<byte>();

        var handle = _nextHandle++;
        _handles[handle] = new Handle { Name = name, Buffer = buffer, Position = 0, Writable = mode != 0 };
        return (double)handle;
    }

    private static object? FS_file_bin_read_byte(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (!_handles.TryGetValue(handle, out var h) || h.Position >= h.Buffer.Count)
        {
            return -1d;
        }

        return (double)h.Buffer[h.Position++];
    }

    private static object? FS_file_bin_write_byte(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var value = (byte)(args[1].Conv<int>() & 0xFF);
        if (!_handles.TryGetValue(handle, out var h))
        {
            return 0d;
        }

        if (h.Position < h.Buffer.Count)
        {
            h.Buffer[h.Position] = value;
        }
        else
        {
            while (h.Buffer.Count < h.Position)
            {
                h.Buffer.Add(0);
            }

            h.Buffer.Add(value);
        }

        h.Position++;
        return 0d;
    }

    private static object? FS_file_bin_close(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (_handles.Remove(handle, out var h) && h.Writable)
        {
            SavePersisted(h.Name, h.Buffer.ToArray());
        }

        return 0d;
    }

    private static object? FS_file_bin_seek(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var pos = args[1].Conv<int>();
        if (_handles.TryGetValue(handle, out var h))
        {
            h.Position = Math.Max(0, pos);
        }

        return 0d;
    }

    private static object? FS_file_bin_seek_relative(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var offset = args[1].Conv<int>();
        var origin = args[2].Conv<int>(); // 0 = start, 1 = current, 2 = end
        if (_handles.TryGetValue(handle, out var h))
        {
            h.Position = Math.Max(0, origin switch
            {
                0 => offset,
                2 => h.Buffer.Count + offset,
                _ => h.Position + offset,
            });
        }

        return 0d;
    }

    private static object? FS_file_bin_position(object?[] args)
    {
        var handle = args[0].Conv<int>();
        return _handles.TryGetValue(handle, out var h) ? (double)h.Position : -1d;
    }

    private static object? FS_file_bin_size(object?[] args)
    {
        var handle = args[0].Conv<int>();
        return _handles.TryGetValue(handle, out var h) ? (double)h.Buffer.Count : -1d;
    }

    private static object? FS_file_bin_eof(object?[] args)
    {
        var handle = args[0].Conv<int>();
        return _handles.TryGetValue(handle, out var h) && h.Position >= h.Buffer.Count ? 1d : 0d;
    }

    private static object? FS_file_bin_good(object?[] args) => 1d;
    private static object? FS_file_bin_bad(object?[] args) => 0d;
    private static object? FS_file_bin_fail(object?[] args) => 0d;
    private static object? FS_file_bin_clear_bad(object?[] args) => 0d;
    private static object? FS_file_bin_clear_fail(object?[] args) => 0d;

    private static object? FS_file_bin_read_word(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var lo = FS_file_bin_read_byte([(double)handle]).Conv<int>();
        var hi = FS_file_bin_read_byte([(double)handle]).Conv<int>();
        return (double)(lo | (hi << 8));
    }

    private static object? FS_file_bin_write_word(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var value = args[1].Conv<int>();
        FS_file_bin_write_byte([(double)handle, (double)(value & 0xFF)]);
        FS_file_bin_write_byte([(double)handle, (double)((value >> 8) & 0xFF)]);
        return 0d;
    }

    private static object? FS_file_bin_read_dword(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var b0 = FS_file_bin_read_byte([(double)handle]).Conv<int>();
        var b1 = FS_file_bin_read_byte([(double)handle]).Conv<int>();
        var b2 = FS_file_bin_read_byte([(double)handle]).Conv<int>();
        var b3 = FS_file_bin_read_byte([(double)handle]).Conv<int>();
        return (double)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
    }

    private static object? FS_file_bin_write_dword(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var value = args[1].Conv<int>();
        FS_file_bin_write_byte([(double)handle, (double)(value & 0xFF)]);
        FS_file_bin_write_byte([(double)handle, (double)((value >> 8) & 0xFF)]);
        FS_file_bin_write_byte([(double)handle, (double)((value >> 16) & 0xFF)]);
        FS_file_bin_write_byte([(double)handle, (double)((value >> 24) & 0xFF)]);
        return 0d;
    }

    private static object? FS_file_exists(object?[] args)
    {
        var path = args[0].Conv<string>();
        return AnyExists(path) ? 1d : 0d;
    }

    private static object? FS_file_delete(object?[] args)
    {
        var path = args[0].Conv<string>();
        var stagedPath = Resolve(path);
        if (File.Exists(stagedPath))
        {
            File.Delete(stagedPath);
        }
        RemovePersisted(SaveResolve(path));
        return 1d;
    }

    /// <summary>
    /// Reads a file from either layer -- the game's own writes or what it shipped with -- in
    /// <see cref="ReadAny"/>'s order, the same order FS_file_exists answers and
    /// FS_file_text_open_read reads in.
    /// </summary>
    private static byte[]? ReadFromEither(string path)
    {
        return ReadAny(path);
    }

    /// <summary>
    /// Writes a *real* file at the destination's resolved path, not just the persisted (localStorage
    /// on wasm) store -- functions like sprite_add read a copied/renamed file straight off the real
    /// filesystem (System.IO.File), bypassing this extension's own persistence abstraction entirely,
    /// so the destination has to actually exist there for a script chaining copy -> sprite_add (or
    /// similar) to work.
    /// </summary>
    private static void WriteToStaged(string path, byte[] data)
    {
        var stagedPath = Resolve(path);
        var dir = Path.GetDirectoryName(stagedPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(stagedPath, data);
    }

    private static object? FS_file_rename(object?[] args)
    {
        var from = args[0].Conv<string>();
        var to = args[1].Conv<string>();
        var data = ReadFromEither(from);
        if (data == null)
        {
            return 0d;
        }

        WriteToStaged(to, data);
        FS_file_delete([from]);
        return 1d;
    }

    private static object? FS_file_copy(object?[] args)
    {
        var from = args[0].Conv<string>();
        var to = args[1].Conv<string>();
        var data = ReadFromEither(from);
        if (data == null)
        {
            return 0d;
        }

        WriteToStaged(to, data);
        return 1d;
    }

    private static object? FS_max_open(object?[] args) => 32d;

    private static object? FS_noop(object?[] args) => 0d;

    // -- directory_* --------------------------------------------------------------------------
    //
    // Only real, staged-folder checks -- there's no directory concept in the persisted (localStorage
    // on wasm) basename-keyed store, only flat file names, matching real bundled game folders
    // (read-only assets shipped next to data.win), not the app's own save data.

    /// <summary>
    /// On BROWSER this asks the VFS, which answers "does anything live under here" across both
    /// layers. It used to call Directory.Exists against the in-memory MEMFS, which knows nothing
    /// about uploaded files -- so a game guarding an asset folder with directory_exists (the
    /// near-universal idiom) was told the folder was missing and bailed.
    /// </summary>
    private static object? FS_directory_exists(object?[] args)
    {
        var path = args[0].Conv<string>();
#if BROWSER
        return BrowserFileSystem.DirectoryExists(Resolve(path)) ? 1d : 0d;
#else
        return Directory.Exists(Resolve(path)) ? 1d : 0d;
#endif
    }

    private static object? FS_directory_create(object?[] args)
    {
        var path = Resolve(args[0].Conv<string>());
#if BROWSER
        BrowserFileSystem.CreateDirectory(path);
#else
        Directory.CreateDirectory(path);
#endif
        return 1d;
    }

    private static object? FS_directory_delete(object?[] args)
    {
        var path = Resolve(args[0].Conv<string>());
#if BROWSER
        BrowserFileSystem.RemoveDirectory(path);
#else
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
#endif
        return 1d;
    }

    // -- file_text_* ----------------------------------------------------------------------------

    internal static object? FS_file_text_open_read(object?[] args)
    {
        var path = args[0].Conv<string>();
        var name = SaveResolve(path);

        // Every place the file can be, in ReadGameFile's order (the game's own writes, then what it
        // shipped with) -- falls back to empty content, not a failure, if it is in none of them,
        // same as real GM8's own "open nonexistent file" behaviour: a valid handle that reads as
        // immediately empty/EOF rather than a thrown error.
        //
        // This used to be File.Exists(Resolve(path)) with a persisted-store fallback. On the
        // browser build the first of those is dead code -- the game's own files live in the JS-side
        // VFS, which System.IO cannot see -- so the fallback was the only branch that ever ran, and
        // it looks only in the save area. A text file the game *shipped with* was therefore
        // unreadable there: jtool's bundled example.jmap opened as an empty string and the game
        // reported "Not a valid jtool map."
        var bytes = ReadGameFile(path);
        var content = bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : "";

        var handle = _nextHandle++;
        _textHandles[handle] = new TextHandle { Name = name, Content = content, Position = 0, Writable = false };
        return (double)handle;
    }

    // Real GM8 text write mode truncates on open (unlike file_bin's own preserve-and-overwrite
    // behavior) -- starts empty, matching that.
    internal static object? FS_file_text_open_write(object?[] args)
    {
        var name = SaveResolve(args[0].Conv<string>());
        if (name == null)
        {
            return -1d;
        }

        var handle = _nextHandle++;
        _textHandles[handle] = new TextHandle { Name = name, Content = "", Position = 0, Writable = true };
        return (double)handle;
    }

    internal static object? FS_file_text_open_append(object?[] args)
    {
        var name = SaveResolve(args[0].Conv<string>());
        if (name == null)
        {
            return -1d;
        }

        var persisted = LoadPersisted(name);
        var content = persisted != null ? System.Text.Encoding.UTF8.GetString(persisted) : "";

        var handle = _nextHandle++;
        _textHandles[handle] = new TextHandle { Name = name, Content = content, Position = content.Length, Writable = true };
        return (double)handle;
    }

    internal static object? FS_file_text_close(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (_textHandles.Remove(handle, out var h) && h.Writable)
        {
            SavePersisted(h.Name, System.Text.Encoding.UTF8.GetBytes(h.Content));
        }
        return 0d;
    }

    /// <summary>
    /// GM's file_text_read_string: everything from the cursor to the end of the *line*, leaving the
    /// cursor on the line break (advancing past it is file_text_readln's job). Identical to the
    /// plain builtin in FileFunctions.cs -- one GM function, one behaviour, whichever API a game
    /// reaches for.
    ///
    /// This used to return the entire rest of the file, on the reasoning that jtool's map loader
    /// opens a map, calls this once, and parses "the whole file". It parses the whole *first line*.
    /// The two are the same thing only for maps written before jtool 1.2.0, which is what made this
    /// look confirmed: 1.2.0 started appending a second, human-readable copy of the map below a
    /// blank line, so from then on every map jtool saved reloaded with the trailing copy glued onto
    /// its "objects:" section and decoded as more objects -- the map came back with junk blocks and
    /// spikes in it that were never placed.
    /// </summary>
    internal static object? FS_file_text_read_string(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (!_textHandles.TryGetValue(handle, out var h))
        {
            return "";
        }

        var end = h.Position;
        while (end < h.Content.Length && h.Content[end] is not ('\n' or '\r'))
        {
            end++;
        }

        var line = h.Content[h.Position..end];
        h.Position = end;
        return line;
    }

    // Same line-at-a-time read as above, then parsed invariant -- "." is GML's decimal point
    // whatever the host locale is, exactly as the plain file_text_read_real builtin does it.
    internal static object? FS_file_text_read_real(object?[] args)
    {
        var str = FS_file_text_read_string(args).Conv<string>();
        return double.TryParse(
            str,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0d;
    }

    internal static object? FS_file_text_read_char(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (!_textHandles.TryGetValue(handle, out var h) || h.Position >= h.Content.Length)
        {
            return "";
        }

        return h.Content[h.Position++].ToString();
    }

    internal static object? FS_file_text_readln(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (!_textHandles.TryGetValue(handle, out var h) || h.Position >= h.Content.Length)
        {
            return "";
        }

        var newlineIndex = h.Content.IndexOf('\n', h.Position);
        string line;
        if (newlineIndex < 0)
        {
            line = h.Content[h.Position..];
            h.Position = h.Content.Length;
        }
        else
        {
            line = h.Content[h.Position..newlineIndex].TrimEnd('\r');
            h.Position = newlineIndex + 1;
        }
        return line;
    }

    internal static object? FS_file_text_unread(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (_textHandles.TryGetValue(handle, out var h))
        {
            h.Position = Math.Max(0, h.Position - 1);
        }
        return 0d;
    }

    internal static object? FS_file_text_write_string(object?[] args)
    {
        var handle = args[0].Conv<int>();
        var str = args[1].Conv<string>();
        if (_textHandles.TryGetValue(handle, out var h))
        {
            h.Content += str;
            h.Position = h.Content.Length;
        }
        return 0d;
    }

    internal static object? FS_file_text_write_real(object?[] args)
    {
        var value = args[1].Conv<double>();
        return FS_file_text_write_string([args[0], value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    internal static object? FS_file_text_writeln(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (_textHandles.TryGetValue(handle, out var h))
        {
            h.Content += "\n";
            h.Position = h.Content.Length;
        }
        return 0d;
    }

    internal static object? FS_file_text_eof(object?[] args)
    {
        var handle = args[0].Conv<int>();
        return _textHandles.TryGetValue(handle, out var h) && h.Position >= h.Content.Length ? 1d : 0d;
    }

    internal static object? FS_file_text_eoln(object?[] args)
    {
        var handle = args[0].Conv<int>();
        if (!_textHandles.TryGetValue(handle, out var h) || h.Position >= h.Content.Length)
        {
            return 1d;
        }
        return h.Content[h.Position] is '\n' or '\r' ? 1d : 0d;
    }
}
