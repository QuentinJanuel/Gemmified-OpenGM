using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenGM.IO;
using System.Globalization;
using System.Text;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static partial class DataStructuresFunctions
    {
        private static Dictionary<int, List<object?>> _dsListDict = new();
        private static Dictionary<int, Dictionary<object, object?>> _dsMapDict = new();

        /// <summary>
        /// How each map was last read: which format, and the key order it arrived in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is what lets a save written by a real GameMaker install load here.</b> Games
        /// check their own saves -- I Wanna Spook Jam's load path is
        /// <c>ds_map_delete(map, "mapMd5"); md5_string_unicode(ds_map_write(map) + salt)</c>
        /// compared against the value in the file -- and <c>ds_map_write</c> serialises in
        /// *iteration* order. A real runner iterates its hash table; we iterate insertion order.
        /// Identical data therefore hashes differently, the check fails, and the game shows
        /// "Save invalid!" and starts over. No format fix can help: the orders simply differ.
        /// </para>
        /// <para>
        /// Replaying the order the file arrived in sidesteps needing GameMaker's hash at all, and
        /// it is exactly right for the read-then-write shape every one of these load routines has.
        /// Verified against a real save: taking that file's own order, dropping <c>mapMd5</c>, and
        /// re-serialising reproduces the checksum the real runner stored, byte for byte -- which
        /// also proves that deleting a key leaves the order of the rest alone.
        /// </para>
        /// <para>
        /// Keys added after the read have no remembered position and follow, in insertion order. A
        /// map built from scratch has no entry here at all and is written the way it always was.
        /// </para>
        /// </remarks>
        private static readonly Dictionary<int, MapReadShape> _dsMapReadShape = new();

        private sealed record MapReadShape(bool Legacy, List<object> Order);
        private static Dictionary<int, Queue<object?>> _dsQueueDict = new();
        private static Dictionary<int, Stack<object?>> _dsStackDict = new();
        /// <summary>
        /// A ds_priority is a flat list of (value, priority) pairs, not a heap. Half the API
        /// addresses entries *by value* -- ds_priority_find_priority, _change_priority and
        /// _delete_value -- which a binary heap can't do at all, and ties have to resolve to the
        /// oldest entry rather than to wherever the heap happened to sift it. Scanning a list is
        /// also what the runner does; these are never big enough for the complexity to matter.
        /// </summary>
        private static Dictionary<int, List<(object? Value, double Priority)>> _priorityDict = new();

        public static void Clear()
        {
            _dsListDict.Clear();
            _dsMapDict.Clear();
            _dsMapReadShape.Clear();
            _dsQueueDict.Clear();
            _dsStackDict.Clear();
            _priorityDict.Clear();
            _dsGridDict.Clear();
            _secureSavePrefixes.Clear();
        }

        // ...

        [GMLFunction("ds_exists")]
        public static object ds_exists(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var type = (DSType)args[1].Conv<int>();

            return type switch
            {
                DSType.Map => _dsMapDict.ContainsKey(id),
                DSType.List => _dsListDict.ContainsKey(id),
                DSType.Stack => _dsStackDict.ContainsKey(id),
                DSType.Queue => _dsQueueDict.ContainsKey(id),
                DSType.Grid => _dsGridDict.ContainsKey(id),
                DSType.Priority => _priorityDict.ContainsKey(id),
                _ => throw new NotImplementedException(),
            };
        }

        [GMLFunction("ds_list_create")]
        public static object ds_list_create(params object?[] args)
        {
            var highestIndex = -1;

            if (_dsListDict.Count > 0)
            {
                highestIndex = _dsListDict.Keys.Max();
            }

            _dsListDict.Add(highestIndex + 1, new());
            return highestIndex + 1;
        }

        [GMLFunction("ds_list_destroy")]
        public static object? ds_list_destroy(object?[] args)
        {
            var index = args[0].Conv<int>();
            _dsListDict.Remove(index);
            return null;
        }

        [GMLFunction("ds_list_clear")]
        public static object? ds_list_clear(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            _dsListDict[id].Clear();
            return null;
        }

        [GMLFunction("ds_list_copy")]
        public static object? ds_list_copy(object?[] args)
        {
            var id = args[0].Conv<int>();
            var source = args[1].Conv<int>();

            if (!_dsListDict.ContainsKey(id) || !_dsListDict.ContainsKey(source))
            {
                return null;
            }

            // TODO: shallow copy, make sure that's not an issue
            _dsListDict[id] = [.. _dsListDict[source]];
            return null;
        }

        [GMLFunction("ds_list_size")]
        public static object? ds_list_size(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsListDict.ContainsKey(id))
            {
                DebugLog.LogError($"Data structure with index {id} does not exist.");
                return 0;
            }

            return _dsListDict[id].Count;
        }

        [GMLFunction("ds_list_empty")]
        public static object? ds_list_empty(params object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            return _dsListDict[id].Count == 0;
        }

        [GMLFunction("ds_list_add")]
        public static object? ds_list_add(params object?[] args)
        {
            // Real GM's ds_* functions silently no-op on a bogus id rather than crashing -- games
            // sometimes pass an undefined id here (e.g. a `noone`-coalescing helper resolving to a
            // missing instance's missing variable), so bail out before Conv<int>() would throw on
            // undefined instead of just failing the ContainsKey check below like any other bad id.
            if (args[0] is null)
            {
                return null;
            }

            var id = args[0].Conv<int>();
            var values = args[1..];

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            var list = _dsListDict[id];
            list.AddRange(values!);
            return null;
        }

        [GMLFunction("ds_list_insert")]
        public static object? ds_list_insert(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var pos = args[1].Conv<int>();
            var val = args[2];

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            var list = _dsListDict[id];
            list.Insert(pos, val);
            return null;
        }

        [GMLFunction("ds_list_set")]
        public static object? ds_list_set(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var pos = args[1].Conv<int>();
            var val = args[2];

            if (pos < 0 || !_dsListDict.TryGetValue(id, out var list))
            {
                return null;
            }

            // Where ds_list_replace only writes over an existing entry, ds_list_set grows the list
            // to reach the position, padding with 0 -- that's what the `list[| i] = v` accessor
            // compiles to, so a game can use it to fill a list it never sized.
            while (list.Count <= pos)
            {
                list.Add(0);
            }

            list[pos] = val;
            return null;
        }

        [GMLFunction("ds_list_replace")]
        public static object? ds_list_replace(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var pos = args[1].Conv<int>();
            var val = args[2];

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            var list = _dsListDict[id];
            list[pos] = val;
            return null;
        }

        [GMLFunction("ds_list_delete")]
        public static object? ds_list_delete(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var pos = args[1].Conv<int>();

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            _dsListDict[id].RemoveAt(pos);
            return null;
        }

        [GMLFunction("ds_list_find_index")]
        public static object? ds_list_find_index(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var value = args[1];

            if (!_dsListDict.ContainsKey(id))
            {
                return -1;
            }

            if (value is null)
            {
                return -1;
            }

            var list = _dsListDict[id];

            // Values that reach a list via different paths can be boxed as different CLR numeric
            // types for the same GML number -- a literal like `11` in compiled bytecode is a boxed
            // short (see PushOpcode's VMType.e / VMCodeInstruction.ShortData), while a runtime
            // variable read (e.g. the `room` builtin) is a boxed int, and arithmetic results are
            // usually double. list.IndexOf(value) uses raw object.Equals, which treats those as
            // unequal even when numerically identical, so this always returned -1 for exactly the
            // "found via a variable, stored via a literal" case real games hit constantly (e.g.
            // `ds_list_find_index(list, room)` against a list built from room-index literals).
            // DictHash is the same numeric-normalizing comparison ds_map already keys on below.
            var hashedValue = VMExecutor.DictHash(value);
            return list.FindIndex(x => Equals(VMExecutor.DictHash(x), hashedValue));
        }

        [GMLFunction("ds_list_find_value")]
        public static object? ds_list_find_value(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var pos = args[1].Conv<int>();

            /*
             * TODO - this shit:
             * Note that if you give a position that is outside of the given list size (i.e.: position 11 in a 10 value list)
             * then the function may return undefined or 0.
             * This is because when you create the list, internally the first few entries in the list
             * are set to 0 to minimize performance issues when initially adding items to the list
             * (although the ds_list_size() function will still return 0 on a newly created list).
             */

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            var list = _dsListDict[id];

            if (pos >= list.Count)
            {
                return null;
            }

            return list[pos];
        }

        // ds_list_is_map
        // ds_list_is_list
        // ds_list_mark_as_list
        // ds_list_mark_as_map

        [GMLFunction("ds_list_sort")]
        public static object? ds_list_sort(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var ascending = args[1].Conv<bool>();

            if (!_dsListDict.TryGetValue(id, out var list))
            {
                return null;
            }

            // OrderBy rather than List.Sort: it's a stable sort, so equal elements keep the order
            // they were added in. List.Sort is introsort and would shuffle ties unpredictably --
            // which for a list of (score, name) pairs sorted by score is a visible difference.
            var sorted = ascending
                ? list.OrderBy(x => x, GMValueComparer.Instance).ToList()
                : list.OrderByDescending(x => x, GMValueComparer.Instance).ToList();

            list.Clear();
            list.AddRange(sorted);
            return null;
        }

        /// <summary>
        /// GML's ordering over mixed values, shared by ds_list_sort and ds_grid_sort so the engine
        /// only has one answer to "which of these two comes first".
        ///
        /// <b>Every real sorts before every string</b>, reals compare numerically among themselves
        /// and strings compare ordinally among themselves. Measured on GMS 1.4.9999: sorting a list
        /// of <c>9</c> and <c>"10"</c> ascending puts the real 9 first, and <c>is_real</c> confirms
        /// the 9 is still a real afterwards.
        ///
        /// The tempting alternative -- "as soon as either side is a string, compare both as text",
        /// which is what ds_grid_sort used to do independently -- gets that case backwards, because
        /// as text "10" precedes "9". It agrees on the easy cases, which is why an earlier capture
        /// (-2.5/1/5/Apple/apple/banana) couldn't tell the two apart.
        ///
        /// Sorting also does not *retype* anything: a real stays a real. The "-2.50" in that capture
        /// was just <c>string()</c>'s runtime formatting, not a converted value.
        ///
        /// Reals are normalised through <see cref="VMExecutor.DictHash"/> before comparing: a
        /// literal small enough for a 16-bit immediate boxes as <c>short</c> while the same number
        /// read back from a variable boxes as <c>int</c>, and treating those as different types is
        /// the bug that already bit ds_list_find_index.
        /// </summary>
        internal class GMValueComparer : IComparer<object?>
        {
            public static readonly GMValueComparer Instance = new();

            public int Compare(object? a, object? b)
            {
                var aIsString = a is string;
                var bIsString = b is string;

                if (aIsString && bIsString)
                    return string.CompareOrdinal((string)a!, (string)b!);
                if (aIsString)
                    return 1;   // strings sort after reals
                if (bIsString)
                    return -1;

                return Num(a).CompareTo(Num(b));
            }

            private static double Num(object? value) => VMExecutor.DictHash(value) is double d ? d : 0;
        }

        [GMLFunction("ds_list_shuffle")]
        public static object? ds_list_shuffle(params object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsListDict.TryGetValue(id, out var list))
            {
                return null;
            }

            // Drives GameMaker's own RNG rather than Random.Shared, so a shuffle is reproducible
            // from a given random_set_seed -- which is the whole reason a game would seed at all.
            // Fisher-Yates walked downwards, matching the runner.
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = (int)GMRandom.YYRandom(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }

            return null;
        }

        [GMLFunction("ds_list_write")]
        public static object? ds_list_write(params object?[] args)
        {

            //TODO : Make it suport legacy format;

            var id = args[0].Conv<int>();

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            var list = _dsListDict[id];

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Header
            writer.Write(DsHeaderList);
            writer.Write(list.Count);

            foreach (var item in list)
            {
                WriteDsValue(writer, item);
            }

            writer.Flush();
            return BitConverter.ToString(stream.ToArray()).Replace("-", "");
        }

        [GMLFunction("ds_list_read")]
        public static object? ds_list_read(params object?[] args)
        {

            //TODO : Make it suport legacy format;

            var id = args[0].Conv<int>();
            var str = args[1].Conv<string>();
            //ignoring the [legacy] param

            if (!_dsListDict.ContainsKey(id))
            {
                return null;
            }

            var data = Convert.FromHexString(str);
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);

            reader.ReadInt32(); // header -- ds_list_write emits one, and skipping it here read the
                                // header itself as the entry count
            var count = reader.ReadInt32();
            var list = _dsListDict[id];

            for (var i = 0; i < count; i++)
            {
                list.Add(ReadDsValue(reader));
            }

            return null;
        }

        // ds_list_set
        // ds_list_set_post
        // ds_list_set_pre

        [GMLFunction("ds_map_create")]
        public static object ds_map_create(params object?[] args)
        {
            var highestIndex = -1;
            if (_dsMapDict.Count > 0)
            {
                highestIndex = _dsMapDict.Keys.Max();
            }

            _dsMapDict.Add(highestIndex + 1, []);
            return highestIndex + 1;
        }

        /// <summary>
        /// GM's json_encode, which is a *text* format games checksum, not just a data format.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three things have to match a real runner byte for byte, and all three were measured
        /// against a save file a real GMS 1.4 install wrote (Madoka Needle's, see
        /// <c>RealJsonSaveIntegrityTests</c>):
        /// </para>
        /// <list type="bullet">
        /// <item>Key order: the runner's hash-bucket order -- the same <see cref="InRunnerOrder"/>
        /// ds_map_write already used, which reproduces that file's 26-key order exactly.</item>
        /// <item>Spacing: <c>{ "k": v, "k2": v2 }</c>, not the compact form.</item>
        /// <item>Reals: always six decimal places, <c>1.000000</c>.</item>
        /// </list>
        /// <para>
        /// This used to be a Newtonsoft JObject built in insertion order and written compact, which
        /// got every one of the three wrong. The number format was wrong in a way worth naming: a
        /// JValue renders by its *CLR* type, so the same GML value came out as <c>0</c> or
        /// <c>0.0</c> depending on whether it had passed through arithmetic. GML has one number
        /// type; a real runner cannot produce that inconsistency, and the user's own save files
        /// show it on neighbouring keys.
        /// </para>
        /// <para>
        /// Why it matters: these games store <c>md5_string_unicode(json_encode(map) + salt)</c> in
        /// the save and recompute it on load, so a save written here was rejected by a real install
        /// and vice versa -- "Save invalid!", and the game starts over. Self-consistent within one
        /// runtime, which is exactly why it went unnoticed.
        /// </para>
        /// </remarks>
        [GMLFunction("json_encode")]
        public static object json_encode(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsMapDict.ContainsKey(id))
            {
                throw new ArgumentException($"ds_map index {id} does not exist");
            }

            var sb = new StringBuilder();
            WriteJsonMap(sb, id, [id]);
            return sb.ToString();
        }

        private static void WriteJsonMap(StringBuilder sb, int mapId, HashSet<int> open)
        {
            sb.Append('{');

            var first = true;
            foreach (var (key, value) in InWriteOrder(mapId))
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append(' ');
                WriteJsonString(sb, key.ToString()!);
                sb.Append(": ");
                WriteJsonValue(sb, value, open);
            }

            sb.Append(" }");
        }

        // The bracket spacing here is inferred from the object form rather than measured -- no
        // sample of a real runner's json_encode over a nested ds_list has turned up. Everything
        // inside it (the number format, the ", " between elements) is the measured one.
        private static void WriteJsonList(StringBuilder sb, int listId, HashSet<int> open)
        {
            sb.Append('[');

            var first = true;
            foreach (var value in _dsListDict[listId])
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append(' ');
                WriteJsonValue(sb, value, open);
            }

            sb.Append(" ]");
        }

        /// <summary>
        /// Mirrors json_decode's Parse (FileFunctions.cs) in reverse: an int that is the id of a
        /// live ds_map/ds_list encodes as a nested object/array, which is what a real runner's
        /// json_encode does for nested data structures.
        /// </summary>
        /// <remarks>
        /// That test is a guess at the value's *meaning* from its CLR type, and it cannot be
        /// anything better here: a ds id is an int and so is nothing else this engine stores, but
        /// the engine cannot tell "the map with id 2" from "the number 2" if a game ever boxes one
        /// as an int. <paramref name="open"/> only stops that guess from being fatal -- a map that
        /// reaches itself is written as a plain number rather than recursing until the stack goes.
        /// </remarks>
        private static void WriteJsonValue(StringBuilder sb, object? value, HashSet<int> open)
        {
            if (value is int intVal && open.Add(intVal))
            {
                try
                {
                    if (_dsMapDict.ContainsKey(intVal))
                    {
                        WriteJsonMap(sb, intVal, open);
                        return;
                    }

                    if (_dsListDict.ContainsKey(intVal))
                    {
                        WriteJsonList(sb, intVal, open);
                        return;
                    }
                }
                finally
                {
                    open.Remove(intVal);
                }
            }

            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string str:
                    WriteJsonString(sb, str);
                    break;
                default:
                    // Every GML number is one type, however this engine happens to be boxing it.
                    sb.Append(value.Conv<double>().ToString("F6", CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        [GMLFunction("ds_map_destroy")]
        public static object? ds_map_destroy(object?[] args)
        {
            var index = args[0].Conv<int>();
            _dsMapDict.Remove(index);
            _dsMapReadShape.Remove(index);
            return null;
        }

        [GMLFunction("ds_map_clear")]
        public static object? ds_map_clear(object?[] args)
        {
            var index = args[0].Conv<int>();
            _dsMapDict[index].Clear();
            // Nothing of what was read survives, so neither should its order.
            _dsMapReadShape.Remove(index);
            return null;
        }

        [GMLFunction("ds_map_copy")]
        public static object? ds_map_copy(object?[] args)
        {
            var id = args[0].Conv<int>();
            var source = args[1].Conv<int>();
            if (!_dsMapDict.ContainsKey(id) || !_dsMapDict.ContainsKey(source))
            {
                return null;
            }

            // TODO: this is a shallow clone i think, see if that causes issues
            _dsMapDict[id] = new Dictionary<object, object?>(_dsMapDict[source]);
            return null;
        }

        [GMLFunction("ds_map_size")]
        public static object ds_map_size(object?[] args)
        {
            var id = args[0].Conv<int>();
            return _dsMapDict[id].Count;
        }

        [GMLFunction("ds_map_empty")]
        public static object? ds_map_empty(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            return _dsMapDict[id].Count == 0;
        }

        [GMLFunction("ds_map_add")]
        public static object ds_map_add(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1])!;
            var value = args[2]!;

            if (!_dsMapDict.ContainsKey(id))
            {
                return false;
            }

            var dict = _dsMapDict[id];
            if (dict.ContainsKey(key))
            {
                return false;
            }

            dict.Add(key, value);
            return true;
        }

        [GMLFunction("ds_map_replace")]
        public static object ds_map_replace(object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1])!;
            var value = args[2]!;

            if (!_dsMapDict.TryGetValue(id, out var dict))
            {
                return false;
            }

            // Unlike ds_map_add this overwrites, and unlike a plain set it reports whether there was
            // anything there to overwrite.
            var replaced = dict.ContainsKey(key);
            dict[key] = value;
            return replaced;
        }

        [GMLFunction("ds_map_set")]
        public static object? ds_map_set(object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1])!;
            var value = args[2]!;

            if (!_dsMapDict.ContainsKey(id))
            {
                return false;
            }

            var dict = _dsMapDict[id];
            dict[key] = value;

            return null;
        }

        // ds_map_set_pre
        // ds_map_set_post
        // ds_map_add_list
        // ds_map_add_map
        // ds_map_replace
        // ds_map_replace_list
        // ds_map_replace_map

        [GMLFunction("ds_map_delete")]
        public static object? ds_map_delete(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1])!;

            if (!_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            _dsMapDict[id].Remove(key);
            return null;
        }

        [GMLFunction("ds_map_exists")]
        public static object? ds_map_exists(object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1])!;

            if (!_dsMapDict.ContainsKey(id))
            {
                return false;
            }

            var dict = _dsMapDict[id];
            return dict.ContainsKey(key);
        }

        // ds_map_keys_to_array
        // ds_map_values_to_array

        [GMLFunction("ds_map_find_value")]
        public static object? ds_map_find_value(object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1]);

            if (key is null || !_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            var dict = _dsMapDict[id];
            if (!dict.ContainsKey(key))
            {
                return null;
            }

            return dict[key];
        }

        // ds_map_is_map
        // ds_map_is_list

        [GMLFunction("ds_map_find_previous")]
        public static object? ds_map_find_previous(object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1]);

            if (key is null || !_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            var dict = _dsMapDict[id];
            var index = dict.Keys.ToList().IndexOf(key);
            if (index == -1 || index == 0)
            {
                return null;
            }

            return dict.Keys.ElementAt(index - 1);
        }

        [GMLFunction("ds_map_find_next")]
        public static object? ds_map_find_next(object?[] args)
        {
            var id = args[0].Conv<int>();
            var key = VMExecutor.DictHash(args[1]);

            if (key is null || !_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            var dict = _dsMapDict[id];
            var index = dict.Keys.ToList().IndexOf(key);
            if (index == -1 || index == dict.Keys.Count - 1)
            {
                return null;
            }

            return dict.Keys.ElementAt(index + 1);
        }

        [GMLFunction("ds_map_find_first")]
        public static object? ds_map_find_first(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            var dict = _dsMapDict[id];

            return dict.Keys.FirstOrDefault();
        }

        [GMLFunction("ds_map_find_last")]
        public static object? ds_map_find_last(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            var dict = _dsMapDict[id];

            return dict.Keys.LastOrDefault();
        }

        /// <summary>
        /// One value, tagged and length-prefixed, exactly as a real runner writes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Two tags, not five.</b> A real GMS1 runner has only reals and strings in a data
        /// structure -- everything numeric goes out as tag 0 and an IEEE-754 double. This used to
        /// write tag 2 for the integer widths, 3 for bools and 5 for NaN, which are ours alone: a
        /// list holding the number 1 came out as `02000000 0100000000000000` where a real install
        /// wrote `00000000 000000000000F03F`, and neither could read the other's.
        /// </para>
        /// <para>
        /// The read path still understands all five, because saves carrying the old tags are
        /// already in players' browsers and cloud rows. Nothing writes them any more.
        /// </para>
        /// </remarks>
        private static void WriteDsValue(BinaryWriter writer, object? item)
        {
            if (item is string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                writer.Write(1);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                return;
            }

            writer.Write(0);
            writer.Write(DsText.Real(item));
        }

        /// <summary>
        /// Tags 0 and 1 are a real runner's. Tags 2, 3 and 5 are ones this engine used to write,
        /// kept so the saves that carry them keep loading -- see <see cref="WriteDsValue"/>.
        /// </summary>
        private static object? ReadDsValue(BinaryReader reader)
        {
            var type = reader.ReadInt32();
            return type switch
            {
                5 => double.NaN,
                0 => reader.ReadDouble(),
                1 => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())),
                2 => reader.ReadInt64(),
                3 => reader.ReadInt32() != 0,
                _ => null
            };
        }

        [GMLFunction("ds_map_write")]
        public static object? ds_map_write(params object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            // `legacy` is the same optional argument ds_map_read takes: write the pre-Studio text
            // format instead of the current binary one. Honoured rather than ignored because a game
            // that asks for it on a real runner *gets* it, and a save this engine wrote in the wrong
            // one of the two is a save that install cannot read. I Wanna Spook Jam's save file is in
            // the text format, which is how we know games really do ask.
            var shape = _dsMapReadShape.GetValueOrDefault(id);
            var ordered = InWriteOrder(id);

            // The explicit flag wins; otherwise write back whichever format this map was read in,
            // and only fall back to the current one for a map built from scratch. A save file has
            // to go home in the format its own runner writes -- and that is not a constant: this
            // game's 2016 runner writes the text format from a bare ds_map_write, a current one
            // writes the binary blob.
            var legacy = args.Length > 1
                ? args[1].Conv<bool>()
                : shape?.Legacy ?? CompatFlags.LegacyDataStructures;

            return legacy
                ? DsText.WriteMap(ordered)
                : BitConverter.ToString(SerializeMap(ordered)).Replace("-", "");
        }

        /// <summary>
        /// A map's entries in the order anything that serialises one has to emit them: ds_map_write
        /// and json_encode both, since a game checksums whichever of the two it uses and both are
        /// just this map being iterated.
        /// </summary>
        /// <remarks>
        /// A map that came from a file goes back out in the order it arrived, which is exact
        /// whatever runtime wrote it. A map built from scratch -- or decoded from JSON, which
        /// inserts rather than reads -- has no such record, so it is ordered the way that runtime
        /// would have ordered it.
        /// </remarks>
        private static IEnumerable<KeyValuePair<object, object?>> InWriteOrder(int id)
        {
            var map = _dsMapDict[id];
            var shape = _dsMapReadShape.GetValueOrDefault(id);
            return shape is null ? InRunnerOrder(map) : InReadOrder(map, shape.Order);
        }

        /// <summary>
        /// A map's entries in the order a real GameMaker runner would iterate them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Buckets from the highest index down, the bucket being <c>crc32(key) mod tableSize</c>,
        /// with ties falling in insertion order. Reverse-engineered, then checked against data it
        /// was not fitted to: it reproduces all four synthetic orderings printed by
        /// a probe project, a fifth over 32 real save-field names, and -- at the other table
        /// size -- the 33-key order inside a save file written by a real install.
        /// </para>
        /// <para>
        /// This is what <c>ds_map_write</c> serialises in, and games check their saves against it:
        /// I Wanna Spook Jam stores <c>md5_string_unicode(ds_map_write(map) + salt)</c> and
        /// recomputes it on load. Iterating in insertion order -- which is what this engine used to
        /// do -- gives a different string for identical data, so the game shows "Save invalid!" and
        /// starts over. No amount of getting the *format* right helps with the order wrong.
        /// </para>
        /// <para>
        /// <b>String keys only.</b> How a real runner buckets a numeric key is not known -- the one
        /// sample containing one (a two-entry map keyed by <c>2</c> and <c>"k"</c>) is not enough to
        /// derive it, and hashing its decimal form puts it on the wrong side of that pair. A map
        /// with any non-string key therefore keeps insertion order, which is what this engine did
        /// everywhere before: no better, but nothing invented either. Save maps are string-keyed,
        /// so this costs nothing today.
        /// </para>
        /// </remarks>
        private static IEnumerable<KeyValuePair<object, object?>> InRunnerOrder(
            Dictionary<object, object?> map)
        {
            if (!map.Keys.All(key => key is string))
                return map;

            var size = CompatFlags.LegacyDataStructures ? 4096 : 256;
            var insertion = map.Keys.ToList();
            return map.OrderByDescending(pair => Crc32OfKey((string)pair.Key) % size)
                .ThenBy(pair => insertion.IndexOf(pair.Key));
        }

        private static uint Crc32OfKey(string key) => Crc32.Compute(Encoding.UTF8.GetBytes(key));

        /// <summary>
        /// The map's entries, those that were read first and in that order, then anything added
        /// since. Keys deleted in between simply do not appear.
        /// </summary>
        private static IEnumerable<KeyValuePair<object, object?>> InReadOrder(
            Dictionary<object, object?> map,
            List<object> order)
        {
            var seen = new HashSet<object>();
            foreach (var key in order)
            {
                if (!map.TryGetValue(key, out var value) || !seen.Add(key))
                    continue;
                yield return new KeyValuePair<object, object?>(key, value);
            }

            foreach (var pair in map)
            {
                if (!seen.Contains(pair.Key))
                    yield return pair;
            }
        }

        [GMLFunction("ds_map_read")]
        public static object? ds_map_read(params object?[] args)
        {
            var id = args[0].Conv<int>();
            var str = args[1].Conv<string>();
            //ignoring the [legacy] param

            if (!_dsMapDict.ContainsKey(id))
            {
                return null;
            }

            // Both formats, and the discrimination is exact rather than a guess: the legacy
            // encoding is bare hex, so a ':' can only ever be the real format's field separator.
            // Reading both is what keeps every save already written by an older build of this
            // engine -- in players' browsers, and in their cloud save rows -- loadable.
            if (str.Length == 0)
            {
                return null;
            }

            if (str.Contains(':'))
            {
                if (DsText.TryReadMap(str, _dsMapDict[id], out var textOrder))
                {
                    _dsMapReadShape[id] = new MapReadShape(true, textOrder);
                }
                else
                {
                    DebugLog.LogError(
                        $"ds_map_read couldn't parse a map: {str[..Math.Min(str.Length, 64)]}");
                }

                return null;
            }

            try
            {
                var order = DeserializeMapInto(Convert.FromHexString(str), _dsMapDict[id]);
                _dsMapReadShape[id] = new MapReadShape(false, order);
            }
            catch (Exception e)
            {
                // Neither format. Better an empty map and a line in the log than an exception out
                // of a load routine the game has no way to catch.
                DebugLog.LogError($"ds_map_read couldn't parse a map: {e.Message}");
            }

            return null;
        }

        /// <summary>
        /// The current (post-GM8) ds_map_write format: a header, an entry count, then each key and
        /// value through <see cref="WriteDsValue"/>. Byte-for-byte what a real GMS1 runner writes --
        /// see the note on <see cref="DsHeaderMap"/> for the measurement that settled it.
        /// </summary>
        private static byte[] SerializeMap(IEnumerable<KeyValuePair<object, object?>> entries)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Materialised because the count is written before the entries and the sequence may be
            // lazy (see InReadOrder).
            var pairs = entries.ToList();
            writer.Write(DsHeaderMap);
            writer.Write(pairs.Count);

            foreach (var (key, value) in pairs)
            {
                WriteDsValue(writer, key);
                WriteDsValue(writer, value);
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static List<object> DeserializeMapInto(byte[] data, Dictionary<object, object?> dict)
        {
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);

            reader.ReadInt32(); // header
            var count = reader.ReadInt32();

            var order = new List<object>(count);
            for (var i = 0; i < count; i++)
            {
                var key = VMExecutor.DictHash(ReadDsValue(reader))!;
                dict[key] = ReadDsValue(reader);
                order.Add(key);
            }

            return order;
        }

        // A secure save is real GameMaker's own format -- a 40-character "encrypted UDID" followed
        // by base64(json_encode(map)) -- so saves transfer both ways with a real install. Only the
        // payload is portable: the prefix is a per-machine/per-runner identifier (a double SHA1 of
        // a reversed device id) that exists to bind a save to the machine that wrote it.
        //
        // Loading therefore never checks the prefix, which is what lets a save copied in from a real
        // GameMaker install load here. Saving writes back whatever prefix that file was last loaded
        // with, so a save that came from an install keeps that install's identifier and still passes
        // its check when it goes back; only files first created here fall back to the constant.

        /// <summary>
        /// The prefix the HTML5 runner writes: sha1(sha1("000001023793500225082")), the double SHA1
        /// of its hardcoded device identifier reversed. Used only for saves first created here --
        /// the desktop runners derive theirs differently, which is the whole reason loading ignores
        /// the prefix instead of verifying it.
        /// </summary>
        private const string SecureSaveDefaultPrefix = "6496af6d018f7edd220d024fc6ca533a8eaf848b";

        /// <summary>
        /// Prefix each save file was last loaded with, keyed like the persisted store.
        /// </summary>
        private static readonly Dictionary<string, string> _secureSavePrefixes = new();

        [GMLFunction("ds_map_secure_save")]
        public static object ds_map_secure_save(object?[] args)
        {
            var id = args[0].Conv<int>();
            var filename = args[1].Conv<string>();

            if (!_dsMapDict.ContainsKey(id))
            {
                return false;
            }

            var payload = MathFunctions.base64_encode([json_encode([id])]).Conv<string>();
            if (FileFunctions.PersistedName(filename) is not { } name)
            {
                return false; // refused by the save-path rules
            }

            var prefix = _secureSavePrefixes.GetValueOrDefault(name, SecureSaveDefaultPrefix);

            // Goes through the same basename-keyed persisted store as file_text_*/file_bin_* (real
            // files on native, localStorage on browser-wasm), so a game can file_exists/file_delete
            // its own secure saves.
            GMFileSystemExtension.SavePersisted(name, Encoding.UTF8.GetBytes(prefix + payload));
            return true;
        }

        [GMLFunction("ds_map_secure_load")]
        public static object ds_map_secure_load(object?[] args)
        {
            var filename = args[0].Conv<string>();

            var data = FileFunctions.ReadPersistedOrBundled(filename);
            if (data == null)
            {
                // Games branch on this: a missing or unreadable save has to come back as -1, not as
                // an empty map that would read as a save full of zeroes.
                return -1;
            }

            if (!TryReadSecurePayload(Encoding.UTF8.GetString(data), out var prefix, out var json))
            {
                DebugLog.LogError($"ds_map_secure_load couldn't find a payload in {filename}");
                return -1;
            }

            try
            {
                var map = FileFunctions.json_decode([json]);
                if (FileFunctions.PersistedName(filename) is { } name)
                {
                    _secureSavePrefixes[name] = prefix;
                }
                return map ?? -1;
            }
            catch (Exception e)
            {
                DebugLog.LogError($"ds_map_secure_load failed to read {filename} : {e}");
                return -1;
            }
        }

        /// <summary>
        /// Splits a secure save into its opaque prefix and its JSON payload.
        /// </summary>
        private static bool TryReadSecurePayload(string text, out string prefix, out string json)
        {
            foreach (var offset in SecurePayloadOffsets(text))
            {
                string decoded;
                try
                {
                    decoded = Encoding.UTF8.GetString(Convert.FromBase64String(text[offset..]));
                }
                catch (FormatException)
                {
                    continue;
                }

                if (decoded.StartsWith('{'))
                {
                    prefix = text[..offset];
                    json = decoded;
                    return true;
                }
            }

            prefix = "";
            json = "";
            return false;
        }

        private static IEnumerable<int> SecurePayloadOffsets(string text)
        {
            // Every runner seen so far writes a 40-character SHA1 prefix, so try that first.
            const int sha1Length = 40;
            if (text.Length > sha1Length)
            {
                yield return sha1Length;
            }

            // Otherwise go looking, in case some runner's identifier is a different length. The
            // payload is base64 of JSON starting with '{', and base64 only ever renders a leading
            // '{' as 'e', so anywhere else is not worth trying to decode.
            for (var i = 0; i < text.Length; i++)
            {
                if (i != sha1Length && text[i] == 'e')
                {
                    yield return i;
                }
            }
        }

        // ds_map_secure_load_buffer
        // ds_map_secure_save_buffer

        [GMLFunction("ds_priority_create")]
        public static object? ds_priority_create(object?[] args)
        {
            var highestIndex = -1;
            if (_priorityDict.Count > 0)
            {
                highestIndex = _priorityDict.Keys.Max();
            }

            _priorityDict.Add(highestIndex + 1, new());
            return highestIndex + 1;
        }

        [GMLFunction("ds_priority_destroy")]
        public static object? ds_priority_destroy(object?[] args)
        {
            var id = args[0].Conv<int>();
            _priorityDict.Remove(id);
            return null;
        }

        [GMLFunction("ds_priority_clear")]
        public static object? ds_priority_clear(object?[] args)
        {
            var id = args[0].Conv<int>();
            _priorityDict[id].Clear();
            return null;
        }

        [GMLFunction("ds_priority_copy")]
        public static object? ds_priority_copy(object?[] args)
        {
            var id = args[0].Conv<int>();
            var source = args[1].Conv<int>();

            var to = _priorityDict[id];
            var from = _priorityDict[source];

            to.Clear();
            to.AddRange(from);

            return null;
        }

        [GMLFunction("ds_priority_empty")]
        public static object? ds_priority_empty(object?[] args)
        {
            var id = args[0].Conv<int>();
            return _priorityDict[id].Count == 0;
        }

        [GMLFunction("ds_priority_size")]
        public static object? ds_priority_size(object?[] args)
        {
            var id = args[0].Conv<int>();
            return _priorityDict[id].Count;
        }

        [GMLFunction("ds_priority_add")]
        public static object? ds_priority_add(object?[] args)
        {
            var id = args[0].Conv<int>();
            var val = args[1];
            var priority = args[2].Conv<double>();

            _priorityDict[id].Add((val, priority));
            return null;
        }

        /// <summary>
        /// Index of the entry holding <paramref name="value"/>, or -1. Compares through
        /// <see cref="VMExecutor.DictHash"/> so that a boxed short and a boxed int holding the same
        /// GML number match -- the same normalisation ds_map keys on and that ds_list_find_index
        /// was silently missing.
        /// </summary>
        private static int FindPriorityEntry(List<(object? Value, double Priority)> queue, object? value)
        {
            var hashed = VMExecutor.DictHash(value);
            return queue.FindIndex(x => Equals(VMExecutor.DictHash(x.Value), hashed));
        }

        /// <summary>
        /// Index of the lowest- (or highest-) priority entry. Scans forward and only replaces on a
        /// strict improvement, so a tie resolves to the *oldest* matching entry.
        /// </summary>
        private static int FindExtreme(List<(object? Value, double Priority)> queue, bool max)
        {
            if (queue.Count == 0)
                return -1;

            var best = 0;
            for (var i = 1; i < queue.Count; i++)
            {
                if (max ? queue[i].Priority > queue[best].Priority : queue[i].Priority < queue[best].Priority)
                {
                    best = i;
                }
            }
            return best;
        }

        [GMLFunction("ds_priority_change_priority")]
        public static object? ds_priority_change_priority(object?[] args)
        {
            var id = args[0].Conv<int>();
            var val = args[1];
            var priority = args[2].Conv<double>();

            var queue = _priorityDict[id];
            var index = FindPriorityEntry(queue, val);
            if (index >= 0)
            {
                queue[index] = (queue[index].Value, priority);
            }

            return null;
        }

        [GMLFunction("ds_priority_find_priority")]
        public static object? ds_priority_find_priority(object?[] args)
        {
            var id = args[0].Conv<int>();
            var queue = _priorityDict[id];
            var index = FindPriorityEntry(queue, args[1]);
            return index >= 0 ? queue[index].Priority : (object?)null;
        }

        [GMLFunction("ds_priority_delete_value")]
        public static object? ds_priority_delete_value(object?[] args)
        {
            var id = args[0].Conv<int>();
            var queue = _priorityDict[id];
            var index = FindPriorityEntry(queue, args[1]);
            if (index >= 0)
            {
                queue.RemoveAt(index);
            }

            return null;
        }

        [GMLFunction("ds_priority_find_min")]
        public static object? ds_priority_find_min(object?[] args)
        {
            var id = args[0].Conv<int>();
            var queue = _priorityDict[id];
            var index = FindExtreme(queue, max: false);
            return index >= 0 ? queue[index].Value : (object?)null;
        }

        [GMLFunction("ds_priority_find_max")]
        public static object? ds_priority_find_max(object?[] args)
        {
            var id = args[0].Conv<int>();
            var queue = _priorityDict[id];
            var index = FindExtreme(queue, max: true);
            return index >= 0 ? queue[index].Value : (object?)null;
        }

        [GMLFunction("ds_priority_delete_min")]
        public static object? ds_priority_delete_min(object?[] args)
        {
            var id = args[0].Conv<int>();
            var queue = _priorityDict[id];

            var index = FindExtreme(queue, max: false);
            if (index < 0)
            {
                return 0;
            }

            var value = queue[index].Value;
            queue.RemoveAt(index);
            return value;
        }

        [GMLFunction("ds_priority_delete_max")]
        public static object? ds_priority_delete_max(object?[] args)
        {
            var id = args[0].Conv<int>();
            var queue = _priorityDict[id];

            var index = FindExtreme(queue, max: true);
            if (index < 0)
            {
                return 0;
            }

            var value = queue[index].Value;
            queue.RemoveAt(index);
            return value;
        }

        [GMLFunction("ds_priority_write")]
        public static object? ds_priority_write(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_priorityDict.TryGetValue(id, out var queue))
            {
                return null;
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(DsHeaderPriority);
            writer.Write(queue.Count);

            foreach (var (value, priority) in queue)
            {
                WriteDsValue(writer, value);
                writer.Write(priority);
            }

            return BitConverter.ToString(stream.ToArray()).Replace("-", "");
        }

        [GMLFunction("ds_priority_read")]
        public static object? ds_priority_read(object?[] args)
        {
            var id = args[0].Conv<int>();
            var str = args[1].Conv<string>();

            if (!_priorityDict.TryGetValue(id, out var queue))
            {
                return null;
            }

            using var stream = new MemoryStream(Convert.FromHexString(str));
            using var reader = new BinaryReader(stream);

            reader.ReadInt32(); // header
            var count = reader.ReadInt32();

            // GM's read replaces the contents rather than appending to them.
            queue.Clear();
            for (var i = 0; i < count; i++)
            {
                var value = ReadDsValue(reader);
                queue.Add((value, reader.ReadDouble()));
            }

            return null;
        }

        // ds_grid_create
        // ds_grid_destroy
        // ds_grid_copy
        // ds_grid_resize
        // ds_grid_width
        // ds_grid_height
        // ds_grid_clear
        // ds_grid_set
        // ds_grid_set_pre
        // ds_grid_set_post

        [GMLFunction("ds_queue_create")]
        public static object ds_queue_create(params object?[] args)
        {
            var highestIndex = -1;
            if (_dsQueueDict.Count > 0)
            {
                highestIndex = _dsQueueDict.Keys.Max();
            }

            _dsQueueDict.Add(highestIndex + 1, new());
            return highestIndex + 1;
        }

        [GMLFunction("ds_queue_destroy")]
        public static object? ds_queue_destroy(object?[] args)
        {
            var index = args[0].Conv<int>();
            _dsQueueDict.Remove(index);
            return null;
        }

        [GMLFunction("ds_queue_clear")]
        public static object? ds_queue_clear(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsQueueDict.ContainsKey(id))
            {
                return false;
            }

            var queue = _dsQueueDict[id];
            queue.Clear();

            return null;
        }

        [GMLFunction("ds_queue_empty")]
        public static object? ds_queue_empty(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsQueueDict.ContainsKey(id))
            {
                return false;
            }

            var queue = _dsQueueDict[id];
            return queue.Count == 0;
        }

        [GMLFunction("ds_queue_size")]
        public static object? ds_queue_size(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsQueueDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsQueueDict[id];
            return queue.Count;
        }

        [GMLFunction("ds_queue_dequeue")]
        public static object? ds_queue_dequeue(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsQueueDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsQueueDict[id];
            return queue.Dequeue();
        }

        [GMLFunction("ds_queue_enqueue")]
        public static object? ds_queue_enqueue(object?[] args)
        {
            var id = args[0].Conv<int>();
            var value = args[1]!;

            if (!_dsQueueDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsQueueDict[id];
            queue.Enqueue(value);

            return null;
        }

        [GMLFunction("ds_queue_head")]
        public static object? ds_queue_head(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsQueueDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsQueueDict[id];

            if (queue.Count == 0)
            {
                return null;
            }

            return queue.Peek();
        }

        [GMLFunction("ds_queue_tail")]
        public static object? ds_queue_tail(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsQueueDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsQueueDict[id];

            if (queue.Count == 0)
            {
                return null;
            }

            return queue.Last();
        }

        [GMLFunction("ds_queue_copy")]
        public static object? ds_queue_copy(object?[] args)
        {
            var id = args[0].Conv<int>();
            var source = args[1].Conv<int>();

            if (!_dsQueueDict.TryGetValue(id, out var to) || !_dsQueueDict.TryGetValue(source, out var from))
            {
                return null;
            }

            to.Clear();
            foreach (var item in from)
            {
                to.Enqueue(item);
            }

            return null;
        }

        [GMLFunction("ds_queue_write")]
        public static object? ds_queue_write(object?[] args)
        {
            var id = args[0].Conv<int>();
            return _dsQueueDict.TryGetValue(id, out var queue)
                ? WriteDsSequence(DsHeaderQueue, queue)
                : null;
        }

        [GMLFunction("ds_queue_read")]
        public static object? ds_queue_read(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsQueueDict.TryGetValue(id, out var queue))
            {
                return null;
            }

            queue.Clear();
            foreach (var item in ReadDsSequence(args[1].Conv<string>()))
            {
                queue.Enqueue(item);
            }

            return null;
        }

        [GMLFunction("ds_stack_create")]
        public static object ds_stack_create(params object?[] args)
        {
            var highestIndex = -1;
            if (_dsStackDict.Count > 0)
            {
                highestIndex = _dsStackDict.Keys.Max();
            }

            _dsStackDict.Add(highestIndex + 1, new());
            return highestIndex + 1;
        }

        [GMLFunction("ds_stack_destroy")]
        public static object? ds_stack_destroy(object?[] args)
        {
            var index = args[0].Conv<int>();
            _dsStackDict.Remove(index);
            return null;
        }

        [GMLFunction("ds_stack_clear")]
        public static object? ds_stack_clear(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsStackDict.ContainsKey(id))
            {
                return false;
            }

            var queue = _dsStackDict[id];
            queue.Clear();

            return null;
        }

        [GMLFunction("ds_stack_empty")]
        public static object? ds_stack_empty(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsStackDict.ContainsKey(id))
            {
                return false;
            }

            var queue = _dsStackDict[id];
            return queue.Count == 0;
        }

        [GMLFunction("ds_stack_size")]
        public static object? ds_stack_size(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsStackDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsStackDict[id];
            return queue.Count;
        }

        [GMLFunction("ds_stack_top")]
        public static object? ds_stack_top(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsStackDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsStackDict[id];

            if (queue.Count == 0)
            {
                return null;
            }

            return queue.Peek();
        }

        [GMLFunction("ds_stack_pop")]
        public static object? ds_stack_pop(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsStackDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsStackDict[id];
            return queue.Pop();
        }

        [GMLFunction("ds_stack_push")]
        public static object? ds_stack_push(object?[] args)
        {
            var id = args[0].Conv<int>();
            var value = args[1]!;

            if (!_dsStackDict.ContainsKey(id))
            {
                return -1;
            }

            var queue = _dsStackDict[id];
            queue.Push(value);

            return null;
        }

        [GMLFunction("ds_stack_copy")]
        public static object? ds_stack_copy(object?[] args)
        {
            var id = args[0].Conv<int>();
            var source = args[1].Conv<int>();

            if (!_dsStackDict.TryGetValue(id, out var to) || !_dsStackDict.TryGetValue(source, out var from))
            {
                return null;
            }

            to.Clear();
            // Stack's own enumerator runs top-to-bottom, so pushing in that order would invert the
            // copy. Reverse first so the destination ends up with the same element on top.
            foreach (var item in from.Reverse())
            {
                to.Push(item);
            }

            return null;
        }

        [GMLFunction("ds_stack_write")]
        public static object? ds_stack_write(object?[] args)
        {
            var id = args[0].Conv<int>();
            return _dsStackDict.TryGetValue(id, out var stack)
                // Bottom-to-top, so a read can push straight through and land the same way up.
                ? WriteDsSequence(DsHeaderStack, stack.Reverse())
                : null;
        }

        [GMLFunction("ds_stack_read")]
        public static object? ds_stack_read(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (!_dsStackDict.TryGetValue(id, out var stack))
            {
                return null;
            }

            stack.Clear();
            foreach (var item in ReadDsSequence(args[1].Conv<string>()))
            {
                stack.Push(item);
            }

            return null;
        }

        /*
         * Shared (de)serialisation for the flat sequence structures. Same shape as ds_list_write's:
         * a hex string of [header][count][value...], with the header telling read paths apart.
         */

        /*
         * The header a real GMS1 runner writes, for the three whose whole encoding we now know
         * matches ours. Measured on Windows, not guessed -- a probe project prints
         * ds_*_write for a fixed pair of values and a real install answered:
         *
         *     map    92010000 02000000 00000000 0000000000000040 01000000 01000000 76 …
         *     list   2E010000 02000000 00000000 000000000000F03F 01000000 01000000 78
         *     stack  66000000 02000000 00000000 000000000000F03F 01000000 03000000 74776F
         *
         * Parse those and the body is byte-for-byte what WriteDsValue already emits: a count, then
         * each value as a 4-byte type tag (0 real, 1 string) and its payload, little-endian, with
         * strings length-prefixed. The *only* thing that was wrong was these four numbers, which
         * had been guessed as a run of consecutive values starting at the list's.
         *
         * That is why a save written here could not be loaded by a real install of the same game,
         * and vice versa -- the thing save sync made matter.
         */
        private const int DsHeaderList = 0x0000012E;
        private const int DsHeaderMap = 0x00000192;
        private const int DsHeaderStack = 0x00000066;

        /*
         * Two things this does NOT yet get right, both found by trying to move a real save between
         * this engine and a real install of I Wanna Spook Jam (GMS 1.4.1757), and both bigger than
         * a constant.
         *
         * 1. **The format is runtime-dependent.** The headers above were measured against a current
         *    1.4 install. That game's own 2016 runner writes the *legacy text* format from a bare
         *    `ds_map_write(map)` -- its save file is `1:hex:hex,` -- so somewhere between 1.4.1757
         *    and now the default changed. Picking correctly means keying off the game's own version
         *    (GEN8 carries it), and the threshold is not known: two observations bracket it.
         *
         * 2. **Iteration order is observable, and ours is wrong.** ds_map_write serialises in
         *    iteration order, and a game that checksums its save -- Spook Jam does exactly
         *    `md5_string_unicode(ds_map_write(map))`, verified in its bytecode -- gets a different
         *    checksum from a differently-ordered map holding identical data. We iterate in
         *    insertion order; a real runner iterates its hash table. For the same 33 keys the two
         *    orders share almost no positions, so such a save is rejected as corrupt whichever way
         *    it crosses. This is not only a save-file problem: ds_map_find_first/_next expose the
         *    same order to gameplay code.
         *
         * Reproducing that order needs measurement rather than inference -- a hash-and-modulo over
         * the final key set does not reproduce it (tried; chance-level), because growth and
         * chaining history are part of the answer. A probe project prints the order for
         * several map sizes and for this game's own key set.
         */

        /*
         * Still ours, and still wrong, because the real ones are more than a header.
         *
         * The same probe caught ds_queue_write emitting `CA000000 02000000 00000000 10000000 …`
         * and then several hundred bytes of zeros for a two-entry queue: a head index, a capacity,
         * and the whole 16-slot backing array rather than just the live entries. Changing the
         * header alone would make our output *claim* to be that format while not being it, which
         * is worse than being visibly foreign. ds_priority_write was never probed at all.
         *
         * Both still round-trip within this engine. Fixing them needs the probe extended, not a
         * guess -- which is exactly the mistake these constants used to encode.
         */
        private const int DsHeaderQueue = 0x00000130;
        private const int DsHeaderPriority = 0x00000132;

        private static string WriteDsSequence(int header, IEnumerable<object?> items)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var materialised = items.ToList();
            writer.Write(header);
            writer.Write(materialised.Count);

            foreach (var item in materialised)
            {
                WriteDsValue(writer, item);
            }

            return BitConverter.ToString(stream.ToArray()).Replace("-", "");
        }

        private static List<object?> ReadDsSequence(string hex)
        {
            using var stream = new MemoryStream(Convert.FromHexString(hex));
            using var reader = new BinaryReader(stream);

            reader.ReadInt32(); // header
            var count = reader.ReadInt32();

            var items = new List<object?>(count);
            for (var i = 0; i < count; i++)
            {
                items.Add(ReadDsValue(reader));
            }

            return items;
        }

        /// <summary>
        /// Sets how close two reals have to be for ds_map/ds_grid lookups to call them equal.
        /// Registered but inert: every comparison here already normalises through
        /// <see cref="VMExecutor.DictHash"/> and matches exactly, which is what GM does at its own
        /// default precision. A game that lowers the precision to make near-misses match would not
        /// get that -- but an unresolved call would abort its whole event, which is strictly worse.
        /// </summary>
        [GMLFunction("ds_set_precision")]
        public static object? ds_set_precision(object?[] args) => null;

        // ...
    }

    public enum DSType
    {
        Map = 1,
        List = 2,
        Stack = 3,
        Queue = 4,
        Grid = 5,
        Priority = 6
    }
}
