using OpenGM.VirtualMachine.BuiltInFunctions;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text.RegularExpressions;

namespace OpenGM;

/// <summary>
/// This engine's half of the plugin's internal wire, in its worker.
///
/// wwwroot/opengm-worker.js boots the runtime once the host's init arrives and hands every message
/// to <see cref="OnMessage"/>; the host realm's half is host/plugin.js. Requests are init, start,
/// tick and stats; what the game tells the host goes out as notes, through wwwroot/services.js,
/// which is also where the JS glue (WebGL, Web Audio, dialogs) sends its own.
/// </summary>
internal static partial class Gems
{
    private const string EngineId = "opengm";
    private const int ApiVersion = 5;
    private const string Module = "services";

    // The audio-cache knobs (host/plugin.js declares them), with this engine's own fallbacks.
    private static readonly (string Key, double Fallback)[] AudioSettings =
        [("audio.shortCompressedBytes", 262144), ("audio.shortDurationSecs", 5), ("audio.musicBudgetBytes", 201326592)];

    private static readonly Regex DataWin = new(@"(^|/)data\.win\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The game's filesystem, from init on.</summary>
    internal static Vfs? Files { get; private set; }

    /// <summary>The host's last reported fullscreen state, so a mid-frame query needs no round trip.</summary>
    internal static bool Fullscreen { get; private set; }

    /// <summary>The drawing buffer's size, as last set; start answers with it.</summary>
    internal static (int Width, int Height) Size { get; set; } = (800, 600);

    private static bool _initialized;
    private static bool _ended;
    // After unload: nothing more comes in.
    private static bool _closed;
    private static string[] _launchArgs = [];
    private static int[] _ports = [];
    private static readonly Dictionary<string, double> _settings = [];
    // Input since the last frame, applied at the start of the next one.
    private static readonly List<JSObject> _input = [];

    private sealed class Connection(int id)
    {
        public int Id { get; } = id;
        public List<byte[]> Inbox { get; } = [];
        public bool Closed { get; set; }
    }

    // The game's loopback connections: by the id the wire knows them by, which this side mints, and
    // by the game's own id, in the order the game opened them.
    private static readonly Dictionary<int, Connection> _wire = [];
    private static readonly OrderedDictionary<int, Connection> _connections = [];
    private static int _nextConnection = 1;

    private sealed class EngineException(string code, string detail) : Exception(detail)
    {
        public string Code { get; } = code;
    }

    [JSImport("install", Module)]
    private static partial void Install(JSObject canvas, JSObject? dialogBuffer);

    [JSImport("reply", Module)]
    private static partial void Reply(int id, string? json);

    [JSImport("fail", Module)]
    private static partial void Fail(int id, string code, string? detail);

    [JSImport("fsSet", Module)]
    private static partial void FsSet(string key, string value);

    [JSImport("fsRemove", Module)]
    private static partial void FsRemove(string key);

    [JSImport("save", Module)]
    private static partial void Saved();

    [JSImport("netOpen", Module)]
    private static partial void NetOpen(int id, int port);

    [JSImport("netSend", Module)]
    private static partial void NetSend(int id, byte[] bytes);

    [JSImport("netEnd", Module)]
    private static partial void NetEnd(int id);

    [JSImport("applyInput", Module)]
    private static partial void ApplyInput(JSObject input);

    [JSImport("audioEnded", Module)]
    private static partial void AudioEnded(int id);

    [JSImport("close", Module)]
    private static partial void Close();

    /// <summary>An ArrayBuffer's or a Uint8Array's bytes, copied in.</summary>
    [JSImport("bytes", Module)]
    private static partial byte[] Bytes(JSObject buffer);

    /// <summary>An object's own entries, flattened: key, value, key, value.</summary>
    [JSImport("entries", Module)]
    private static partial string[] Entries(JSObject record);

    /// <summary>Called by the worker once the runtime is up, before any message.</summary>
    [JSExport]
    internal static async Task Boot() => await JSHost.ImportAsync(Module, "../services.js");

    /// <summary>Every envelope from the host realm's half. Anything else is ignored.</summary>
    [JSExport]
    internal static void OnMessage(JSObject data)
    {
        using (data)
        {
            if (_closed)
            {
                return;
            }
            var body = data.GetPropertyAsJSObject("body");
            switch (data.GetPropertyAsString("w"))
            {
                case "note" when body is not null:
                    using (body)
                    {
                        Note(body);
                    }
                    break;
                case "req":
                    Request(data.GetPropertyAsInt32("id"), body);
                    break;
                default:
                    body?.Dispose();
                    break;
            }
        }
    }

    private static void Request(int id, JSObject? body)
    {
        try
        {
            switch (body?.GetPropertyAsString("kind"))
            {
                case "init":
                    using (var init = body.GetPropertyAsJSObject("init")!)
                    {
                        Reply(id, Init(init));
                    }
                    break;
                case "start":
                    // Answers itself once the game is loaded.
                    _ = Start(id, body);
                    return;
                case "tick":
                    Reply(id, Tick(body.GetPropertyAsDouble("now")) ? """{"running":true}""" : """{"running":false}""");
                    break;
                case "stats":
                    RequireInit();
                    Reply(id, WasmEntry.GetAudioCacheStatsJson());
                    break;
                default:
                    Reply(id, null);
                    break;
            }
        }
        catch (Exception error)
        {
            Failed(id, error);
        }
        body?.Dispose();
    }

    /// <summary>A request that threw: the code a host reads, and a line for its log.</summary>
    private static void Failed(int id, Exception error) =>
        Fail(id, error is EngineException engine ? engine.Code : "internal", error.Message);

    private static void RequireInit()
    {
        if (!_initialized)
        {
            throw new EngineException("internal", "the host has not sent init yet");
        }
    }

    private static string Init(JSObject init)
    {
        if (_initialized)
        {
            throw new EngineException("internal", "init arrived twice");
        }
        var api = init.GetPropertyAsDouble("apiVersion");
        if (api != ApiVersion)
        {
            throw new EngineException(
                "unsupported-environment",
                $"host implements contract {api.ToString(CultureInfo.InvariantCulture)}, this engine implements {ApiVersion}");
        }

        string[] seed;
        using (var record = init.GetPropertyAsJSObject("storeSeed")!)
        {
            seed = Entries(record);
        }
        var pairs = Enumerable.Range(0, seed.Length / 2).Select(i => KeyValuePair.Create(seed[2 * i], seed[(2 * i) + 1]));
        Files = new Vfs(init.GetPropertyAsString("gameId") ?? "", pairs, Mirror, Saved);

        using (var settings = init.GetPropertyAsJSObject("settings")!)
        {
            foreach (var (key, _) in AudioSettings)
            {
                Remember(key, settings, key);
            }
        }
        _launchArgs = ReadArray(init, "launchArgs", (array, i) => array.GetPropertyAsString(i) ?? "");
        _ports = ReadArray(init, "ports", (array, i) => array.GetPropertyAsInt32(i));

        // The glue the runtime imports during start reads these.
        using (var canvas = init.GetPropertyAsJSObject("canvas")!)
        using (var dialogBuffer = init.GetPropertyAsJSObject("dialogBuffer"))
        {
            Install(canvas, dialogBuffer);
        }

        _initialized = true;
        // Before the load, which is where LoadSounds decides how much of the soundtrack it decodes.
        ApplyAudioCachePolicy();
        return $$"""{"engineId":"{{EngineId}}"}""";
    }

    private static T[] ReadArray<T>(JSObject owner, string name, Func<JSObject, string, T> item)
    {
        using var array = owner.GetPropertyAsJSObject(name)!;
        return [.. Enumerable.Range(0, array.GetPropertyAsInt32("length")).Select(i => item(array, i.ToString(CultureInfo.InvariantCulture)))];
    }

    /// <summary>A number setting as the host sent it; anything else falls back to the default.</summary>
    private static void Remember(string key, JSObject source, string property)
    {
        if (source.GetTypeOfProperty(property) == "number")
        {
            _settings[key] = source.GetPropertyAsDouble(property);
        }
        else
        {
            _settings.Remove(key);
        }
    }

    private static double Setting(string key, double fallback) =>
        _settings.TryGetValue(key, out var value) && double.IsFinite(value) ? value : fallback;

    private static void ApplyAudioCachePolicy() => WasmEntry.SetAudioCachePolicy(
        (int)Setting(AudioSettings[0].Key, AudioSettings[0].Fallback),
        Setting(AudioSettings[1].Key, AudioSettings[1].Fallback),
        Setting(AudioSettings[2].Key, AudioSettings[2].Fallback));

    private static void Mirror(string key, string? value)
    {
        if (value is null)
        {
            FsRemove(key);
        }
        else
        {
            FsSet(key, value);
        }
    }

    private static async Task Start(int id, JSObject body)
    {
        try
        {
            RequireInit();
            var files = ReadArray(body, "files", (array, i) =>
            {
                using var file = array.GetPropertyAsJSObject(i)!;
                return (Path: file.GetPropertyAsString("path") ?? "", Bytes: file.GetPropertyAsJSObject("bytes")!);
            });
            var entry = Array.FindIndex(files, file => DataWin.IsMatch(file.Path));
            if (entry < 0)
            {
                throw new EngineException("unsupported-game", "no data.win in this game");
            }
            // The game's own folder, so a loose asset beside data.win (audiogroup*.dat, an external
            // .ogg) keeps the path the game's own relative file calls expect.
            var root = files[entry].Path[..^"data.win".Length];

            WasmEntry.ResetGameFiles();
            byte[] dataWin = [];
            for (var i = 0; i < files.Length; i++)
            {
                var (path, bytes) = files[i];
                var relative = i == entry ? "data.win" : path.StartsWith(root, StringComparison.Ordinal) ? path[root.Length..] : path;
                // Both layers, because they are different things: the wasm FS is what GameConverter
                // reads during the load, and the bundle is what the running game's own file calls see.
                Files!.AddBundle(Vfs.ParsePath("/game/" + relative), () => Bytes(bytes));
                if (i == entry)
                {
                    dataWin = Bytes(bytes);
                }
                else
                {
                    WasmEntry.WriteGameFile(relative, Bytes(bytes));
                }
            }
            // Written last so the conversion sees its siblings already in place.
            WasmEntry.WriteGameFile("data.win", dataWin);

            WasmEntry.SetLaunch(_launchArgs, _ports);
            await WasmEntry.LoadGameFromDataWin();
            RelayLoopback();
            Reply(id, $$"""{"width":{{Size.Width}},"height":{{Size.Height}}}""");
        }
        catch (Exception error)
        {
            Failed(id, error);
        }
        finally
        {
            body.Dispose();
        }
    }

    private static bool Tick(double now)
    {
        RequireInit();
        if (_ended)
        {
            return false;
        }
        foreach (var input in _input)
        {
            using (input)
            {
                ApplyInput(input);
            }
        }
        _input.Clear();

        // What the host sent since the last frame, so this frame's Async Networking event sees it.
        foreach (var (id, connection) in _connections.ToArray())
        {
            var bytes = connection.Inbox.Count == 1 ? connection.Inbox[0] : [.. connection.Inbox.SelectMany(part => part)];
            connection.Inbox.Clear();
            if (bytes.Length > 0)
            {
                NetworkFunctions.Push(id, bytes);
            }
            if (connection.Closed)
            {
                NetworkFunctions.ClosedByHost(id);
                _connections.Remove(id);
            }
        }

        WasmEntry.RenderFrame(now);
        RelayLoopback();

        // game_end closes the (no-op) window; report it so the host ends the session.
        if (WasmEntry.IsGameEnded())
        {
            _ended = true;
            return false;
        }
        return true;
    }

    /// <summary>What the game did on loopback since the last call, out to the host.</summary>
    private static void RelayLoopback()
    {
        var (events, sent) = NetworkFunctions.Drain();
        var at = 0;
        for (var i = 0; i < events.Length; i += 3)
        {
            var (kind, id, arg) = ((NetworkFunctions.EventKind)events[i], events[i + 1], events[i + 2]);
            switch (kind)
            {
                case NetworkFunctions.EventKind.Open when _ports.Contains(arg):
                    var opened = new Connection(_nextConnection++);
                    _wire[opened.Id] = opened;
                    _connections[id] = opened;
                    NetOpen(opened.Id, arg);
                    break;
                case NetworkFunctions.EventKind.Send:
                    if (_connections.TryGetValue(id, out var open) && !open.Closed && arg > 0)
                    {
                        NetSend(open.Id, sent[at..(at + arg)]);
                    }
                    at += arg;
                    break;
                case NetworkFunctions.EventKind.End:
                    if (_connections.Remove(id, out var ended) && !ended.Closed)
                    {
                        ended.Closed = true;
                        NetEnd(ended.Id);
                    }
                    break;
            }
        }
    }

    private static void Note(JSObject body)
    {
        switch (body.GetPropertyAsString("kind"))
        {
            case "input":
                _input.Add(body.GetPropertyAsJSObject("input")!);
                break;
            case "setting":
                var key = body.GetPropertyAsString("key") ?? "";
                Remember(key, body, "value");
                // Acted on now, not at the next read: the engine re-reads its budget.
                if (_initialized && key.StartsWith("audio.", StringComparison.Ordinal))
                {
                    ApplyAudioCachePolicy();
                }
                break;
            case "net-data":
                using (var bytes = body.GetPropertyAsJSObject("bytes")!)
                {
                    if (_wire.TryGetValue(body.GetPropertyAsInt32("id"), out var connection))
                    {
                        connection.Inbox.Add(Bytes(bytes));
                    }
                }
                break;
            case "net-close":
                if (_wire.TryGetValue(body.GetPropertyAsInt32("id"), out var closed))
                {
                    closed.Closed = true;
                }
                break;
            // A file the host added goes straight into the filesystem the game reads.
            case "fs-put":
                using (var bytes = body.GetPropertyAsJSObject("bytes")!)
                {
                    Files?.Write(Vfs.ParsePath(body.GetPropertyAsString("path") ?? ""), Bytes(bytes));
                }
                break;
            // The host deleted an entry: the running game stops seeing it, and the host is not told back.
            case "fs-drop":
                Files?.Drop(body.GetPropertyAsString("key") ?? "");
                break;
            case "fullscreen-changed":
                Fullscreen = body.GetPropertyAsBoolean("enabled");
                break;
            case "audio-ended":
                AudioEnded(body.GetPropertyAsInt32("id"));
                break;
            case "unload":
                try
                {
                    if (_initialized)
                    {
                        WasmEntry.UnloadGame();
                    }
                }
                finally
                {
                    _closed = true;
                    Close();
                }
                break;
        }
    }
}
