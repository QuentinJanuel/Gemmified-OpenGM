using Newtonsoft.Json.Linq;
using OpenGM.IO;
using System.Globalization;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

public static class FileFunctions
{
    private static readonly Dictionary<int, FileHandle> _fileHandles = new(32);
    private static IniFile? _iniFile;
    private static FileInfo[]? _findFile;
    private static int _findFileIdx;

    // file_text_open_from_string

    /*
     * The binary file family.
     *
     * Held entirely in memory and flushed to the same persisted store the text-file builtins use
     * (GMFileSystemExtension), rather than to System.IO. That's deliberate and it's the single most
     * repeated lesson in this engine's history: the plain builtins and the GMFileSystem extension
     * are two independent persistence paths, and every time one of them has reached for raw
     * System.IO it has written to storage that is ephemeral under the browser build -- so the save
     * appeared to work and was gone on reload. file_text_* was fixed for this once, ini_* a second
     * time; this is the third family and it starts out on the right store.
     *
     * Whole-file buffering also makes seek/size/position trivially correct, and these files are
     * save data -- kilobytes, not megabytes.
     */

    private class BinaryFileHandle
    {
        public string? PersistedName;
        public List<byte> Data = new();
        public int Position;
        public bool CanWrite;
        public bool Dirty;
    }

    private static readonly Dictionary<int, BinaryFileHandle> _binHandles = new();

    private const int BinModeRead = 0;
    private const int BinModeWrite = 1;
    private const int BinModeReadWrite = 2;

    [GMLFunction("file_bin_open")]
    public static object file_bin_open(object?[] args)
    {
        var fname = args[0].Conv<string>();
        var mode = args[1].Conv<int>();

        var existing = ReadPersistedOrBundled(fname);
        var persistedName = PersistedName(fname);
        if (persistedName == null && mode != BinModeRead)
        {
            return -1; // refused by the save-path rules: fails as an uncreatable file does
        }

        // Mode 1 creates or truncates; 0 and 2 keep whatever is there. A read of a file that isn't
        // there fails (-1); a read-write of one that isn't there creates it, matching how GM lets
        // mode 2 be used to build a new file.
        if (mode == BinModeRead && existing == null)
        {
            return -1;
        }

        var handle = new BinaryFileHandle
        {
            PersistedName = persistedName,
            Data = mode == BinModeWrite || existing == null ? new List<byte>() : new List<byte>(existing),
            Position = 0,
            CanWrite = mode != BinModeRead,
            // A fresh write handle must persist even if nothing is ever written to it, or
            // file_bin_open(f, 1) followed by close would leave the old contents in place.
            Dirty = mode == BinModeWrite,
        };

        var id = 0;
        while (_binHandles.ContainsKey(id))
        {
            id++;
        }

        _binHandles.Add(id, handle);
        return id;
    }

    [GMLFunction("file_bin_close")]
    public static object? file_bin_close(object?[] args)
    {
        var id = args[0].Conv<int>();

        if (_binHandles.Remove(id, out var handle) && handle.CanWrite && handle.Dirty)
        {
            GMFileSystemExtension.SavePersisted(handle.PersistedName, handle.Data.ToArray());
        }

        return null;
    }

    [GMLFunction("file_bin_size")]
    public static object file_bin_size(object?[] args)
        => _binHandles.TryGetValue(args[0].Conv<int>(), out var handle) ? handle.Data.Count : 0;

    [GMLFunction("file_bin_position")]
    public static object file_bin_position(object?[] args)
        => _binHandles.TryGetValue(args[0].Conv<int>(), out var handle) ? handle.Position : 0;

    [GMLFunction("file_bin_seek")]
    public static object? file_bin_seek(object?[] args)
    {
        if (_binHandles.TryGetValue(args[0].Conv<int>(), out var handle))
        {
            // Seeking past the end is allowed and does not itself grow the file -- the next write
            // is what fills the gap (with zeroes, below).
            handle.Position = Math.Max(0, args[1].Conv<int>());
        }

        return null;
    }

    [GMLFunction("file_bin_rewrite")]
    public static object? file_bin_rewrite(object?[] args)
    {
        if (_binHandles.TryGetValue(args[0].Conv<int>(), out var handle))
        {
            handle.Data.Clear();
            handle.Position = 0;
            handle.Dirty = true;
        }

        return null;
    }

    [GMLFunction("file_bin_read_byte")]
    public static object file_bin_read_byte(object?[] args)
    {
        if (!_binHandles.TryGetValue(args[0].Conv<int>(), out var handle))
        {
            return 0;
        }

        // Reading past the end returns 0 and still advances. This is a DELIBERATE DIVERGENCE:
        // GMS 1.4.9999 raises a fatal `Error reading byte.` and stops the game (measured -- it took
        // down a probe run). Reproducing that would only ever matter for a game that already
        // crashes on Windows, while getting our own EOF accounting off by one would turn a working
        // game into a crashing one. Same trade as the pre-1970 dates in DateFunctions: match GM
        // where a working game can tell, be forgiving where GM simply fails.
        //
        // It still says so at warning level, because a game hitting this on Windows is broken and
        // the log is the only place that would show up.
        if (handle.Position < 0 || handle.Position >= handle.Data.Count)
        {
            DebugLog.LogWarning($"file_bin_read_byte past the end of the file " +
                                $"(position {handle.Position}, size {handle.Data.Count}) -- real GameMaker aborts here.");
            handle.Position++;
            return 0;
        }

        var value = handle.Data[handle.Position];
        handle.Position++;
        return (int)value;
    }

    [GMLFunction("file_bin_write_byte")]
    public static object? file_bin_write_byte(object?[] args)
    {
        if (!_binHandles.TryGetValue(args[0].Conv<int>(), out var handle) || !handle.CanWrite)
        {
            return null;
        }

        var value = (byte)(args[1].Conv<int>() & 0xFF);

        // Writing past the end zero-fills the gap, so seek-then-write behaves like a sparse file
        // rather than silently dropping the write.
        while (handle.Data.Count < handle.Position)
        {
            handle.Data.Add(0);
        }

        if (handle.Position < handle.Data.Count)
        {
            handle.Data[handle.Position] = value;
        }
        else
        {
            handle.Data.Add(value);
        }

        handle.Position++;
        handle.Dirty = true;
        return null;
    }

    // Basename used to key persisted saves (both here and in GMFileSystemExtension) -- GML paths
    // like "Data\save0" have no real directory concept in the persisted store, only a flat
    // basename, matching GMFileSystemExtension.Basename's own handling of the same kind of path.
    /// <summary>
    /// Resolves a GML-supplied path to the full path the storage layer takes.
    ///
    /// Under GameMaker Studio's sandbox every game write lands in the save area
    /// (%LOCALAPPDATA%\&lt;game name&gt;\), keeping its position relative to the game folder -- so a
    /// game writing "Data\save1" produces %LOCALAPPDATA%\&lt;game&gt;\Data\save1, not a file beside
    /// its own .exe. Confirmed against a real game (I Wanna Spook Jam writes exactly "Data\save"
    /// and "config.ini" as relative paths).
    ///
    /// This was Path.GetFileName -- a bare basename -- which collapsed `Save\game1` and
    /// `Backup\game1` into one stored file AND filed everything as though it sat next to the game.
    /// </summary>
    internal static string? PersistedName(string path)
    {
#if BROWSER
        return LoadSave.ResolveSavePath(path);
#else
        return Entry.ResolveDataWinPath(path);
#endif
    }

    /// <summary>
    /// Reads a file the way every read-side file builtin should: a previously-saved file (persisted
    /// store) takes priority over a bundled asset shipped alongside data.win, the same priority
    /// GMFileSystemExtension's own file_text_open_read uses. Returns null if it's in neither.
    ///
    /// The bundled fallback is what lets a save file dropped in with the game -- e.g. a "Data"
    /// folder carried over from a Windows install -- be read on the game's first run, before
    /// anything has been written to the persisted store.
    /// </summary>
    internal static byte[]? ReadPersistedOrBundled(string fname)
    {
        var bytes = GMFileSystemExtension.LoadPersisted(PersistedName(fname));
        if (bytes != null)
        {
            return bytes;
        }

        if (!LoadSave.BundleFileExist(fname))
        {
            return null;
        }

        var bundlePath = "";
        LoadSave.GetBundleFileName(ref bundlePath, 1024, fname);
        return File.ReadAllBytes(bundlePath);
    }

    [GMLFunction("file_text_open_read")]
    public static object? file_text_open_read(object?[] args)
    {
        var fname = args[0].Conv<string>();

        if (_fileHandles.Count == 32)
        {
            DebugLog.LogError("Cannot open any more files!");
            return -1;
        }

        var bytes = ReadPersistedOrBundled(fname);
        if (bytes == null)
        {
            DebugLog.LogError($"Failed to open file: {fname}");
            return -1;
        }

        var highestIndex = -1;
        if (_fileHandles.Count > 0)
        {
            highestIndex = _fileHandles.Keys.Max();
        }

        var handle = new FileHandle
        {
            Reader = new StreamReader(new MemoryStream(bytes))
        };

        _fileHandles.Add(highestIndex + 1, handle);
        return highestIndex + 1;
    }

    [GMLFunction("file_text_open_write")]
    public static object? file_text_open_write(object?[] args)
    {
        if (_fileHandles.Count == 32)
        {
            DebugLog.LogError("Cannot open any more files!");
            return -1;
        }

        var fname = args[0].Conv<string>();

        var highestIndex = -1;
        if (_fileHandles.Count > 0)
        {
            highestIndex = _fileHandles.Keys.Max();
        }

        if (PersistedName(fname) is not { } persistedName)
        {
            return -1;
        }

        var buffer = new MemoryStream();
        var handle = new FileHandle
        {
            PersistedName = persistedName,
            WriteBuffer = buffer,
            Writer = new StreamWriter(buffer),
        };

        _fileHandles.Add(highestIndex + 1, handle);
        return highestIndex + 1;
    }

    [GMLFunction("file_text_open_append")]
    public static object? file_text_open_append(object?[] args)
    {
        if (_fileHandles.Count == 32)
        {
            DebugLog.LogError("Cannot open any more files!");
            return -1;
        }

        var fname = args[0].Conv<string>();
        if (PersistedName(fname) is not { } name)
        {
            return -1;
        }

        var buffer = new MemoryStream();
        var existing = GMFileSystemExtension.LoadPersisted(name);
        if (existing != null)
        {
            buffer.Write(existing); // leaves buffer.Position at the end, ready to append
        }

        var highestIndex = -1;
        if (_fileHandles.Count > 0)
        {
            highestIndex = _fileHandles.Keys.Max();
        }

        var handle = new FileHandle
        {
            PersistedName = name,
            WriteBuffer = buffer,
            Writer = new StreamWriter(buffer),
        };

        _fileHandles.Add(highestIndex + 1, handle);
        return highestIndex + 1;
    }

    [GMLFunction("file_text_close")]
    public static object? file_text_close(object?[] args)
    {
        var index = args[0].Conv<int>();

        if (_fileHandles.TryGetValue(index, out var handle))
        {
            if (handle.Reader != null)
            {
                handle.Reader.Close();
                handle.Reader.Dispose();
            }

            if (handle.Writer != null)
            {
                handle.Writer.Flush();
                GMFileSystemExtension.SavePersisted(handle.PersistedName!, handle.WriteBuffer!.ToArray());
                handle.Writer.Close();
                handle.Writer.Dispose();
            }

            _fileHandles.Remove(index);
        }

        return null;
    }

    [GMLFunction("file_text_read_string")]
    public static object file_text_read_string(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var reader = _fileHandles[fileid].Reader!;

        var result = "";
        while (reader.Peek() != 0x0D && reader.Peek() != 0x0A && reader.Peek() >= 0)
        {
            result += (char)reader.Read();
        }

        return result;
    }

    [GMLFunction("file_text_read_real")]
    public static object file_text_read_real(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var reader = _fileHandles[fileid].Reader!;

        var result = "";
        while (reader.Peek() != 0x0D && reader.Peek() != 0x0A && reader.Peek() >= 0)
        {
            result += (char)reader.Read();
        }

        // Invariant, like every other string->number path in the engine (see VMExecutor's Conv):
        // GML numbers always use "." for the decimal point whatever the host's locale is. Parsing
        // with the current culture reads "1.000000" -- what a real runner writes -- as one million
        // under a French or German locale.
        if (double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out var ret))
        {
            return ret;
        }
        else
        {
            DebugLog.LogError($"Couldn't read \"{result}\" as a real!");
            return 0;
        }
    }

    [GMLFunction("file_text_readln")]
    public static object? file_text_readln(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var reader = _fileHandles[fileid].Reader!;
        return reader.ReadLine(); // BUG: returns null if eof
    }

    [GMLFunction("file_text_eof")]
    public static object file_text_eof(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var reader = _fileHandles[fileid].Reader!;
        return reader.EndOfStream;
    }

    [GMLFunction("file_text_eoln")]
    public static object file_text_eoln(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var reader = _fileHandles[fileid].Reader!;

        // Peek rather than track a column: the read position is at the end of a line exactly when
        // the next character is a line break (or there isn't one).
        var next = reader.Peek();
        return next == -1 || next == '\n' || next == '\r';
    }

    [GMLFunction("file_text_write_string")]
    public static object? file_text_write_string(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var str = args[1].Conv<string>();
        var writer = _fileHandles[fileid].Writer!;
        writer.Write(str);
        return null;
    }

    [GMLFunction("file_text_write_real")]
    public static object? file_text_write_real(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var val = args[1];
        var writer = _fileHandles[fileid].Writer!;

        if (val is not (int or double or float or long or short))
        {
            if (val is null)
            {
                DebugLog.LogError($"file_text_write_real got null instead of a real!");
            }
            else
            {
                DebugLog.LogError($"file_text_write_real got {val} ({val!.GetType()}) instead of a real!");
            }

            // i have no fucking idea
            writer.Write(0);
            return null;
        }

        writer.Write(val.Conv<double>());
        return null;
    }

    [GMLFunction("file_text_writeln")]
    public static object? file_text_writeln(object?[] args)
    {
        var fileid = args[0].Conv<int>();
        var writer = _fileHandles[fileid].Writer!;
        writer.WriteLine();
        return null;
    }

    [GMLFunction("file_exists")]
    public static object file_exists(object?[] args)
    {
        var fname = args[0].Conv<string>();
        return LoadSave.BundleFileExist(fname) || GMFileSystemExtension.ExistsPersisted(PersistedName(fname));
    }

    [GMLFunction("file_delete")]
    public static object file_delete(object?[] args)
    {
        var fname = args[0].Conv<string>();
        var name = PersistedName(fname);

        if (!GMFileSystemExtension.ExistsPersisted(name))
        {
            return false;
        }

        GMFileSystemExtension.RemovePersisted(name);
        return true;
    }

    // file_rename

    [GMLFunction("file_copy")]
    public static object? file_copy(object?[] args)
    {
        // TODO: use LoadSave here

        var fname = args[0].Conv<string>();
        var newname = args[1].Conv<string>();

        fname = Path.Combine(Entry.DataWinFolder, fname);
        newname = Path.Combine(Entry.DataWinFolder, newname);

        if (File.Exists(newname))
        {
            throw new Exception("File already exists.");
        }

        File.Copy(fname, newname);

        return null;
    }

    [GMLFunction("directory_exists")]
    public static object? directory_exists(object?[] args)
    {
        var dname = args[0].Conv<string>();

        var _name = "";
        LoadSave.GetSaveFileName(ref _name, 0x400, dname);

        if (!Directory.Exists(_name))
        {
            if (LoadSave.GetBundleFileName(ref _name, 0x400, dname) == 0)
            {
                if (Directory.Exists(_name))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    // directory_create
    // directory_destroy

    [GMLFunction("file_find_first", GMLFunctionFlags.Stub)]
    public static object? file_find_first(object?[] args)
    {
        var mask = args[0].Conv<string>();
        var attr = args[1].Conv<int>(); // TODO: do something with attr

        var folder = Directory.GetParent(Path.Combine(Entry.DataWinFolder, mask));
        var filename = Path.GetFileName(mask);
        if (folder == null)
        {
            return "";
        }

        _findFile = folder.GetFiles(filename);
        _findFileIdx = 0;
        return _findFile.ElementAtOrDefault(_findFileIdx)?.Name ?? "";
    }

    [GMLFunction("file_find_next", GMLFunctionFlags.Stub)]
    public static object? file_find_next(object?[] args)
    {
        if (_findFile == null)
        {
            return ""; // idk if this is supposed to error or what
        }

        _findFileIdx++;
        return _findFile.ElementAtOrDefault(_findFileIdx)?.Name ?? "";
    }

    [GMLFunction("file_find_close", GMLFunctionFlags.Stub)]
    public static object? file_find_close(object?[] args)
    {
        _findFile = null;
        _findFileIdx = 0;
        return null;
    }

    // file_attributes
    // filename_name
    // filename_path
    // filename_dir
    // filename_drive
    // filename_ext
    // filename_change_ext

    [GMLFunction("parameter_count")]
    public static object? parameter_count(object?[] args)
    {
        return Entry.LaunchParameters.Length;
    }

    [GMLFunction("parameter_string")]
    public static object? parameter_string(object?[] args)
    {
        var n = args[0].Conv<int>();
        return Entry.LaunchParameters.ElementAtOrDefault(n) ?? "";
    }

    [GMLFunction("environment_get_variable")]
    public static object? environment_get_variable(object?[] args)
    {
        var name = args[0].Conv<string>();
        // TODO : is this right? idk
        return Environment.GetEnvironmentVariable(name);
    }

    [GMLFunction("ini_open_from_string")]
    public static object? ini_open_from_string(object?[] args)
    {
        var content = args[0].Conv<string>();

        if (_iniFile != null)
        {
            ini_close(new object[0]);
        }

        _iniFile = IniFile.FromContent(content);

        return null;
    }

    [GMLFunction("ini_open")]
    public static object? ini_open(object?[] args)
    {
        var name = args[0].Conv<string>();

        if (_iniFile != null)
        {
            // Docs say this throws an error.
            // C++ and HTML runners just save the old ini file and open the new one, with no error.
            // I love Gamemaker.

            ini_close(new object[0]);
        }

        // Through the same path resolution as every other file builtin, and through the bundled
        // fallback too, so an ini shipped with the game is readable on a first run.
        //
        // This used to key the store on Path.GetFileName -- the bare basename -- which is exactly
        // the bug PersistedName's own summary describes as fixed for the file_text family, and the
        // ini family was simply missed. It matters beyond tidiness: a real GMS runner resolves a
        // relative path into the game's save area (%LOCALAPPDATA%\<Name>), so it writes
        // config.ini there, while a bare basename lands next to the game instead. The two then
        // never meet -- which is why a game's settings, volume included, would not follow the
        // player between the emulator and a real install even though its save file did.
        var bytes = ReadPersistedOrBundled(name);

        if (bytes == null)
        {
            _iniFile = new IniFile { Name = name };
            return null;
        }

        var text = System.Text.Encoding.UTF8.GetString(bytes);

        // remove null bytes
        var i = 0;
        while (i < text.Length && text[i] > 0)
        {
            i++;
        }

        text = text[..i];

        _iniFile = IniFile.FromContent(text);
        _iniFile.Name = name;

        return null;
    }

    [GMLFunction("ini_close")]
    public static object? ini_close(object?[] args)
    {
        if (_iniFile == null)
        {
            return null;
        }

        var text = _iniFile.ToString();

        if (_iniFile.Name is not null)
        {
            // Same resolution as ini_open, or a file would be read from one place and written to
            // another.
            GMFileSystemExtension.SavePersisted(
                PersistedName(_iniFile.Name), System.Text.Encoding.UTF8.GetBytes(text));
        }

        _iniFile = null;

        return text;
    }

    [GMLFunction("ini_read_string")]
    public static object ini_read_string(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        var value = args[2].Conv<string>();

        var sectionClass = _iniFile!.Sections.FirstOrDefault(x => x.Name == section);

        if (sectionClass == null)
        {
            return value;
        }

        if (!sectionClass.Dict.ContainsKey(key))
        {
            return value;
        }

        return sectionClass.Dict[key];
    }

    [GMLFunction("ini_read_real")]
    public static object? ini_read_real(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        // The default is handed back untouched rather than converted up front. GML is loosely
        // typed and a runner does not type-check this argument, so a game is free to pass a string
        // and get it back -- I Wanna Be The Ocean Princess reads its music folder with
        // `ini_read_real("Settings", "MusicPath", working_directory + "music/")`, meaning to call
        // ini_read_string. Converting eagerly threw on the path before the key was even looked up,
        // which aborted the whole of its scrLoadConfig.
        var value = args[2];

        var sectionClass = _iniFile!.Sections.FirstOrDefault(x => x.Name == section);

        if (sectionClass == null)
        {
            return value;
        }

        if (!sectionClass.Dict.ContainsKey(key))
        {
            return value;
        }

        // Invariant, and this one had teeth: a real GameMaker runner writes every ini real as
        // `<digits>.000000`, so under a locale where "." groups digits rather than separating the
        // fraction, `Volume_level="1.000000"` came back as 1000000 -- which a game clamps to its
        // maximum. That is a game's settings silently resetting every time they arrive from a real
        // install, while the reverse direction works because what this engine writes has no "." in
        // it at all. See the same fix in VMExecutor's Conv, whose comment records the first time
        // this class of bug surfaced.
        //
        // NumberStyles.Float rather than the default, which also allows group separators: nothing
        // a runner writes contains one, and accepting them is what turns a misread into a number
        // 10^6 too big instead of a clean failure.
        if (!double.TryParse(
                sectionClass.Dict[key], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var _res))
        {
            // TODO : check what it does here. maybe it only parses up to an invalid character?
            return value;
        }

        return _res;
    }

    [GMLFunction("ini_write_string")]
    public static object? ini_write_string(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();
        var value = args[2].Conv<string>();

        var sectionClass = _iniFile!.Sections.FirstOrDefault(x => x.Name == section);

        if (sectionClass == null)
        {
            sectionClass = new IniSection(section);
            _iniFile.Sections.Add(sectionClass);
        }

        sectionClass.Dict[key] = value;

        return null;
    }

    [GMLFunction("ini_write_real")]
    public static object? ini_write_real(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();

        var sectionClass = _iniFile!.Sections.FirstOrDefault(x => x.Name == section);

        if (sectionClass == null)
        {
            sectionClass = new IniSection(section);
            _iniFile.Sections.Add(sectionClass);
        }

        // An ini holds text, and GML does not type-check this argument -- a game may hand a string
        // to ini_write_real, and I Wanna Be The Ocean Princess does: its scrSaveConfig writes the
        // music folder back with `ini_write_real("Settings", "MusicPath", global.musicDirectory)`,
        // the write half of the ini_read_real call in scrLoadConfig that reads it. Converting to a
        // double first threw on the path and aborted the whole of scrSaveConfig, which
        // scrLoadConfig calls at the end of itself.
        //
        // A string is written as it stands, which is what keeps that pair a round trip. A number
        // keeps the "F6" formatting below.
        if (args[2] is string text)
        {
            sectionClass.Dict[key] = text;
            return null;
        }

        // "F6" and invariant, matching a real runner byte for byte: every real in an ini written
        // by one is `<digits>.000000`. Plain ToString() would emit "1,5" under a French locale --
        // unreadable by a real install -- and even in English its "1" for 1.0 would rewrite the
        // whole file on every crossing, so a synced ini would never stop changing.
        sectionClass.Dict[key] = args[2].Conv<double>().ToString("F6", CultureInfo.InvariantCulture);

        return null;
    }

    [GMLFunction("ini_key_exists")]
    public static object? ini_key_exists(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();

        var sec = _iniFile!.Sections.FirstOrDefault(x => x.Name == section);

        return sec != null && sec.Dict.ContainsKey(key);
    }

    [GMLFunction("ini_section_exists")]
    public static object? ini_section_exists(object?[] args)
    {
        var section = args[0].Conv<string>();
        return _iniFile!.Sections.Any(x => x.Name == section);
    }

    [GMLFunction("ini_key_delete")]
    public static object? ini_key_delete(object?[] args)
    {
        var section = args[0].Conv<string>();
        var key = args[1].Conv<string>();

        var sec = _iniFile!.Sections.FirstOrDefault(x => x.Name == section);
        sec?.Dict.Remove(key);

        return null;
    }

    [GMLFunction("ini_section_delete")]
    public static object? ini_section_delete(object?[] args)
    {
        var section = args[0].Conv<string>();

        _iniFile!.Sections.RemoveAll(x => x.Name == section);

        return null;
    }

    // http_get
    // http_get_file
    // http_request
    // http_get_request_crossorigin
    // http_set_request_crossorigin
    // json_encode moved to DataStructuresFunctions.cs (needs direct access to the ds_map/ds_list dicts)

    [GMLFunction("json_decode")]
    public static object? json_decode(object?[] args)
    {
        // is recursive weeeeeeeeeeee
        static object Parse(JToken jToken)
        {
            switch (jToken)
            {
                case JValue jValue:
                    return jValue.Value!;
                case JArray jArray:
                    {
                        var dsList = (int)DataStructuresFunctions.ds_list_create();
                        foreach (var item in jArray)
                        {
                            // TODO: make and call the proper function for maps and lists
                            DataStructuresFunctions.ds_list_add(dsList, Parse(item));
                        }
                        return dsList;
                    }
                case JObject jObject:
                    {
                        var dsMap = (int)DataStructuresFunctions.ds_map_create();
                        foreach (var (name, value) in jObject)
                        {
                            // TODO: make and call the proper function for maps and lists
                            DataStructuresFunctions.ds_map_add(dsMap, name, Parse(value!));
                        }
                        return dsMap;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        var @string = args[0].Conv<string>();
        var jToken = JToken.Parse(@string);

        switch (jToken)
        {
            case JValue jValue:
                {
                    var dsMap = (int)DataStructuresFunctions.ds_map_create();
                    DataStructuresFunctions.ds_map_add(dsMap, "default", Parse(jValue));
                    return dsMap;
                }
            case JArray jArray:
                {
                    var dsMap = (int)DataStructuresFunctions.ds_map_create();
                    DataStructuresFunctions.ds_map_add(dsMap, "default", Parse(jArray));
                    return dsMap;
                }
            case JObject jObject:
                {
                    return Parse(jObject);
                }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>
    /// The other half of <see cref="json_parse"/>: GML values (structs, arrays, numbers, strings,
    /// bools, undefined) to JSON text. This is the modern struct-based pair, not json_encode's
    /// ds_map one -- a GMS 2.3+ game's whole save and settings system is usually
    /// json_stringify/json_parse over one struct, so without this a game cannot write a settings
    /// file, and the failed call aborts everything after it in the calling event.
    /// </summary>
    [GMLFunction("json_stringify")]
    public static object? json_stringify(object?[] args)
    {
        static JToken Write(object? value)
        {
            switch (value)
            {
                case null:
                    // GML's undefined. Real GM writes it as null, and json_parse reads null back as
                    // undefined, so a struct survives the round trip.
                    return JValue.CreateNull();
                case GMLObject gmlObject:
                    {
                        var jObject = new JObject();
                        foreach (var (name, member) in gmlObject.SelfVariables)
                        {
                            jObject[name] = Write(member);
                        }
                        return jObject;
                    }
                case IList<object?> list:
                    {
                        var jArray = new JArray();
                        foreach (var item in list)
                        {
                            jArray.Add(Write(item));
                        }
                        return jArray;
                    }
                case bool b:
                    // GML has no bool type of its own; true/false are 1/0, and that is what real GM
                    // writes here rather than JSON's true/false.
                    return new JValue(b ? 1 : 0);
                case string s:
                    return new JValue(s);
                default:
                    return new JValue(value.Conv<double>());
            }
        }

        // Real GM emits this compact, no line breaks -- and games hash or compare the text (this one
        // base64s it into a settings file), so the formatting is part of the contract.
        return Write(args[0]).ToString(Newtonsoft.Json.Formatting.None);
    }

    [GMLFunction("json_parse")]
    public static object? json_parse(object?[] args)
    {
        static object Parse(JToken jToken)
        {
            switch (jToken)
            {
                case JValue jValue:
                    return jValue.Value!;
                case JArray jArray:
                    {
                        return jArray.Select(Parse).ToList();
                    }
                case JObject jObject:
                    {
                        var retStruct = new GMLObject();
                        foreach (var (name, value) in jObject)
                        {
                            retStruct[name] = Parse(value!);
                        }
                        return retStruct;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        var json = args[0].Conv<string>();
        var jToken = JToken.Parse(json);

        return Parse(jToken);
    }

    // zip_unzip
    // load_csv
}

public class FileHandle
{
    public StreamReader? Reader;
    public StreamWriter? Writer;

    // Only set for Writer handles -- the basename to persist WriteBuffer's bytes under (via
    // GMFileSystemExtension.SavePersisted) once file_text_close flushes.
    public string? PersistedName;
    public MemoryStream? WriteBuffer;
}
