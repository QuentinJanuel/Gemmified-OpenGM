using System.Text;

namespace OpenGM;

/// <summary>
/// A path the game named, resolved: a mount and its cleaned segments. Segments keep
/// the case the game wrote; <see cref="Vfs.Key"/> is the case-insensitive identity.
/// </summary>
internal sealed record VPath(string Mount, string[] Segments);

/// <summary>
/// The game's filesystem, as GEMS defines it: the bundle of the game's own files, read-only, under the
/// store of everything it wrote, over the replica of the host's persisted entries.
///
/// This is a line-for-line port of the GEMS reference implementation, quirks included, because the
/// keys it mints are the player's saves: VfsTests pins them against that implementation's
/// output. In particular store keys keep the case the game wrote, so <c>Save\a</c> and
/// <c>save\a</c> are two entries, and temp paths go to their own mount.
/// </summary>
internal sealed class Vfs(string game, IEnumerable<KeyValuePair<string, string>> seed, Action<string, string?> mirror, Action saved)
{
    private static readonly string[] Mounts = ["game", "appdata", "temp"];
    // Temp comes first: `AppData\Local\Temp\` is not application data, so a mod's temp
    // put must not be persisted and synced as a save. The AppData markers keep their list order below: a path
    // holding two of them must keep the key it has always had.
    private static readonly string[] TempMarkers = ["appdata/local/temp/", "local settings/temp/"];
    private static readonly string[] AppDataMarkers = ["appdata/local/", "appdata/roaming/", "application data/"];

    // The replica of the host's entries; every change but a host's own drop is mirrored back.
    private readonly Dictionary<string, string> _backend = new(seed);
    // `temp` files, whiteouts and directory marks: this session only, keyed like the persisted ones.
    private readonly Dictionary<string, byte[]> _session = [];
    private readonly HashSet<string> _sessionFlags = [];
    // The game's own files, by Key. Read on demand: in the browser they stay JS arrays, outside wasm
    // memory, which never shrinks.
    private readonly Dictionary<string, (VPath Path, Func<byte[]> Read)> _bundle = [];

    // -- paths -------------------------------------------------------------------------------------

    /// <summary>
    /// JavaScript's toLowerCase, which lengthens "İ" to "i̇". The TypeScript found positions in the
    /// lowered string and sliced the original at them, so that shift moves keys and is kept.
    /// shortcut: Greek final sigma is not modelled; it only affects case-insensitive identity, never a key.
    /// </summary>
    internal static string JsLower(string s) => s.Replace("İ", "i̇", StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>A path string as the game handed it, resolved.</summary>
    internal static VPath ParsePath(string raw)
    {
        var unified = raw.Replace('\\', '/');
        var lower = JsLower(unified);
        foreach (var mount in Mounts)
        {
            var bare = "/" + mount;
            if (lower.StartsWith(bare + "/", StringComparison.Ordinal) || lower == bare)
            {
                return new(mount, CleanSegments(unified[bare.Length..]));
            }
        }
        if (AfterFirst(lower, TempMarkers) is { } temp)
        {
            return new("temp", CleanSegments(temp < unified.Length ? unified[temp..] : ""));
        }
        // The first marker in this order that occurs anywhere, not the first occurrence.
        foreach (var marker in AppDataMarkers)
        {
            var at = lower.IndexOf(marker, StringComparison.Ordinal);
            if (at != -1)
            {
                var from = at + marker.Length;
                return new("appdata", CleanSegments(from < unified.Length ? unified[from..] : ""));
            }
        }
        // Another drive is dropped, so the path stays inside the game's own folder.
        var withoutDrive = unified.Length >= 2 && char.IsAsciiLetter(unified[0]) && unified[1] == ':' ? unified[2..] : unified;
        return new("game", CleanSegments(withoutDrive));
    }

    /// <summary>Where the first of the temp markers ends in <paramref name="lower"/>: the earliest by position.</summary>
    private static int? AfterFirst(string lower, string[] markers)
    {
        int? end = null;
        var first = int.MaxValue;
        foreach (var marker in markers)
        {
            var at = lower.IndexOf(marker, StringComparison.Ordinal);
            if (at != -1 && at < first)
            {
                first = at;
                end = at + marker.Length;
            }
        }
        return end;
    }

    /// <summary>`..` pops, and one that would climb above the root is dropped.</summary>
    private static string[] CleanSegments(string raw)
    {
        var segments = new List<string>();
        foreach (var segment in raw.Split('\\', '/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                continue;
            }
            segments.Add(segment);
        }
        return [.. segments];
    }

    /// <summary>Identity, case-insensitively.</summary>
    internal static string Key(VPath path) => $"{path.Mount}:{JsLower(string.Join('/', path.Segments))}";

    private static bool IsInside(VPath dir, VPath path) =>
        dir.Mount == path.Mount
        && path.Segments.Length > dir.Segments.Length
        && dir.Segments.Select((segment, i) => JsLower(segment) == JsLower(path.Segments[i])).All(same => same);

    // -- the key codec ----------------------------------------------------------------------------

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>ECMAScript's encodeURIComponent, which also throws on a lone surrogate.</summary>
    private static string EncodeComponent(string s)
    {
        var builder = new StringBuilder(s.Length);
        foreach (var b in StrictUtf8.GetBytes(s))
        {
            if (char.IsAsciiLetterOrDigit((char)b) || "-_.!~*'()".Contains((char)b))
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return builder.ToString();
    }

    /// <summary>ECMAScript's decodeURIComponent, which throws on a malformed escape.</summary>
    private static string DecodeComponent(string s)
    {
        var builder = new StringBuilder(s.Length);
        var bytes = new List<byte>();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '%')
            {
                builder.Append(StrictUtf8.GetString([.. bytes]));
                bytes.Clear();
                builder.Append(s[i]);
                continue;
            }
            if (i + 2 >= s.Length || !char.IsAsciiHexDigit(s[i + 1]) || !char.IsAsciiHexDigit(s[i + 2]))
            {
                throw new FormatException($"URI malformed: {s}");
            }
            bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
            i += 2;
        }
        return builder.Append(StrictUtf8.GetString([.. bytes])).ToString();
    }

    internal string StoreKey(string tag, VPath path) =>
        $"gems:{tag}:{game}:{path.Mount}:{string.Join('/', path.Segments.Select(EncodeComponent))}";

    /// <summary>A file key back to its game and path, or null for anything else.</summary>
    private static (string Game, VPath Path)? ParseFileKey(string key)
    {
        var parts = key.Split(':');
        if (parts.Length < 5 || parts[0] != "gems" || parts[1] != "file" || !Mounts.Contains(parts[3]))
        {
            return null;
        }
        var encoded = string.Join(':', parts[4..]);
        return (parts[2], new(parts[3], encoded == "" ? [] : [.. encoded.Split('/').Select(DecodeComponent)]));
    }

    // -- the replica ---------------------------------------------------------------------------------

    private void Set(string key, string value)
    {
        _backend[key] = value;
        mirror(key, value);
    }

    /// <summary>Mirrored whether or not the key was there, as it always was.</summary>
    private void Unset(string key)
    {
        _backend.Remove(key);
        mirror(key, null);
    }

    /// <summary>The host deleted an entry: dropped here, and the host is not told back.</summary>
    internal void Drop(string key) => _backend.Remove(key);

    internal void AddBundle(VPath path, Func<byte[]> read) => _bundle[Key(path)] = (path, read);

    // -- the overlay --------------------------------------------------------------------------------

    private bool IsWhitedOut(VPath path) => path.Mount == "temp"
        ? _sessionFlags.Contains(StoreKey("gone", path))
        : _backend.ContainsKey(StoreKey("gone", path));

    private bool HasStored(VPath path) => path.Mount == "temp"
        ? _session.ContainsKey(StoreKey("file", path))
        : _backend.ContainsKey(StoreKey("file", path));

    internal byte[]? Read(VPath path)
    {
        var key = StoreKey("file", path);
        if (path.Mount == "temp" ? _session.TryGetValue(key, out var data) : TryDecode(key, out data))
        {
            return (byte[])data.Clone();
        }
        if (IsWhitedOut(path))
        {
            return null;
        }
        return _bundle.TryGetValue(Key(path), out var file) ? file.Read() : null;
    }

    // shortcut: strict base64 where the TypeScript's atob forgave missing padding; every value is minted padded.
    private bool TryDecode(string key, out byte[] data)
    {
        data = [];
        if (!_backend.TryGetValue(key, out var value))
        {
            return false;
        }
        try
        {
            data = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal bool Exists(VPath path) => HasStored(path) || (!IsWhitedOut(path) && _bundle.ContainsKey(Key(path)));

    /// <summary>Writing a path also lifts any whiteout on it.</summary>
    internal void Write(VPath path, byte[] data)
    {
        if (path.Mount == "temp")
        {
            _sessionFlags.Remove(StoreKey("gone", path));
            _session[StoreKey("file", path)] = (byte[])data.Clone();
            return;
        }
        Unset(StoreKey("gone", path));
        Set(StoreKey("file", path), Convert.ToBase64String(data));
        saved();
    }

    /// <summary>Whether anything was there to delete, which is what GML's file_delete reports.</summary>
    internal bool Remove(VPath path)
    {
        var existed = Exists(path);
        var inBundle = _bundle.ContainsKey(Key(path));
        if (path.Mount == "temp")
        {
            _session.Remove(StoreKey("file", path));
            if (inBundle)
            {
                _sessionFlags.Add(StoreKey("gone", path));
            }
            return existed;
        }
        Unset(StoreKey("file", path));
        if (inBundle)
        {
            Set(StoreKey("gone", path), "1");
        }
        saved();
        return existed;
    }

    /// <summary>
    /// A mount root, a directory created explicitly (matched by its exact case, as it always was), or
    /// one some visible file is inside.
    /// </summary>
    internal bool DirectoryExists(VPath dir)
    {
        if (dir.Segments.Length == 0)
        {
            return true;
        }
        var mark = StoreKey("dir", dir);
        if (dir.Mount == "temp" ? _sessionFlags.Contains(mark) : _backend.ContainsKey(mark))
        {
            return true;
        }
        var stored = _backend.Keys.Concat(_session.Keys)
            .Select(ParseFileKey)
            .Where(parsed => parsed?.Game == game)
            .Select(parsed => parsed!.Value.Path);
        var bundled = _bundle.Values.Select(file => file.Path).Where(path => !IsWhitedOut(path));
        return bundled.Concat(stored).Any(path => IsInside(dir, path));
    }

    /// <summary>GML's directory_create: recorded, so it exists while empty. A mount root always exists.</summary>
    internal void CreateDirectory(VPath dir)
    {
        if (dir.Segments.Length == 0)
        {
            return;
        }
        if (dir.Mount == "temp")
        {
            _sessionFlags.Add(StoreKey("dir", dir));
            return;
        }
        Set(StoreKey("dir", dir), "1");
    }

    internal void RemoveDirectory(VPath dir)
    {
        if (dir.Mount == "temp")
        {
            _sessionFlags.Remove(StoreKey("dir", dir));
            return;
        }
        Unset(StoreKey("dir", dir));
    }
}
