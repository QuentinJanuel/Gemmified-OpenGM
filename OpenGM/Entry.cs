using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Particles;
using OpenGM.Rendering;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Globalization;
using System.IO.Compression;
using UndertaleModLib;

namespace OpenGM;

internal class Entry
{
    /// <summary>
    /// The machine's own culture, captured before the invariant one is installed below.
    ///
    /// Only the date and time *display* builtins use it. Real GameMaker formats those with the
    /// system's short date and time patterns, so a game showing "21/09/2026" on a French machine is
    /// showing exactly what it should. That is the one place in this engine where the player's
    /// locale is the right answer.
    /// </summary>
    public static readonly CultureInfo DisplayCulture = CultureInfo.CurrentCulture;

    static Entry()
    {
        // **Everything else formats and parses numbers invariantly.**
        //
        // GML has no notion of locale: `string(1.5)` is "1.50" on every machine GameMaker runs on,
        // and `real("1.50")` reads it back. .NET routes both through the current culture unless told
        // otherwise, so on a French machine a number formatted anywhere in the engine comes back
        // with a decimal comma and then fails to parse -- silently corrupting any game that builds a
        // number into a string and reads it again. Seen in the wild, from a saved position: `Couldn't read "1534,4650000000022" as a real!`.
        //
        // The individual conversions were each fixed as they were found -- `string`,
        // `string_format`, Conv's own double branch -- and that is whack-a-mole with data loss as
        // the prize; the one that produced the message above was never located. This settles it for
        // every path at once, including the ones nobody has looked at and the ones not written yet:
        // string interpolation, a bare `ToString()`, `double.Parse`, a future builtin.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    public static float GameSpeed { get; private set; } = 30;

    private static CustomWindow window = null!;

    public static string[] LaunchParameters = [];
    public static string DataWinFolder = "";

    public static string? PathOverride = null;

    /// <summary>
    /// Resolves a GML-supplied path (backslashes, as games always author them) against
    /// DataWinFolder for real, staged-file access (bundled assets shipped alongside data.win, e.g.
    /// GMFileSystemExtension's directory_exists/file_text_* and sprite_add). working_directory
    /// itself already returns DataWinFolder (see VariableResolver.get_working_directory), so a path
    /// a game built as "working_directory + fname" arrives here already absolute -- Path.Combine-ing
    /// it against DataWinFolder again would double the prefix (e.g. "/game/game/fname"), so an
    /// already-rooted path is used as-is instead.
    /// </summary>
    public static string ResolveDataWinPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var resolved = Path.IsPathRooted(normalized) ? normalized : Path.Combine(DataWinFolder, normalized.TrimStart('/'));
        return ResolveExistingCase(resolved);
    }

    /// <summary>
    /// The real path of <paramref name="path"/> when the only thing wrong with it is its
    /// capitalisation.
    ///
    /// Every GameMaker game is authored and tested against Windows, whose filesystem is
    /// case-insensitive, so a mismatch between the name in the GML and the name on disk is
    /// invisible there and nobody ever fixes it. The Farewell Medley asks for
    /// <c>music/musKickIt.ogg</c> and ships <c>music/musKickit.ogg</c>; on Windows that is the same
    /// file, and in this engine -- whose in-memory browser filesystem is case-sensitive, as is any
    /// Linux host -- it was simply a missing track.
    ///
    /// The exact path always wins, and a path that matches nothing comes back untouched so a
    /// caller creating a new file still gets the name it asked for. Only the miss pays for the
    /// directory walk.
    /// </summary>
    private static string ResolveExistingCase(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            return path;
        }

        var current = path.StartsWith('/') ? "/" : "";
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var exact = Path.Combine(current, segment);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                current = exact;
                continue;
            }

            var parent = current.Length == 0 ? "." : current;
            if (!Directory.Exists(parent))
            {
                return path;
            }

            var match = Directory.GetFileSystemEntries(parent)
                .FirstOrDefault(x => string.Equals(Path.GetFileName(x), segment, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return path;
            }

            current = match;
        }

        return current;
    }

    public static string GamePath { get; private set; } = "";

    static int Main(string[] args)
    {
        var passedArgs = ProcessArgs(args);

        // The sin/cos cache this used to prime is now filled by GraphicFunctions' static
        // constructor, so the browser entry point gets it too -- see there.

        var exeLocation = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(exeLocation, "game");

        if (PathOverride == null)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            path = PathOverride;
            if (!Path.Exists(PathOverride))
            {
                DebugLog.LogError($"ERROR - Path {PathOverride} not found.");
                return -1;
            }
        }

        path = Path.GetFullPath(path);
        GamePath = File.Exists(path) ? path : Path.Combine(path, "data.win");
        DebugLog.Log($"GamePath: {GamePath}");

        LoadGame(GamePath, passedArgs);

        return GameFunctions.GameEndReturnCode;
    }

    static string[] ProcessArgs(string[] args)
    {
        var passedArgs = new List<string>();
        var endOfOptions = false;
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (endOfOptions)
            {
                passedArgs.Add(arg);
                continue;
            }

            switch (arg)
            {
                case "--game-path":
                    if (i == args.Length - 1)
                    {
                        DebugLog.LogError("ERROR - Must provide a path to --game-path.");
                        Environment.Exit(1);
                    }

                    PathOverride = args[++i];
                    break;

                case "--warnings-only":
                    DebugLog.Verbosity = DebugLog.LogType.Warning;
                    break;

                case "--errors-only":
                    DebugLog.Verbosity = DebugLog.LogType.Error;
                    break;

                case "--verbose":
                case "-v":
                    DebugLog.Verbosity = DebugLog.LogType.Verbose;
                    break;

                case "--log-all-stubs":
                    ScriptResolver.AlwaysLogStubs = true;
                    break;

                case "--record-legacy":
                    {
                        InputHandler.HandlerState = InputHandler.State.RECORD;

                        var path = args[++i];
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }

                        InputHandler.IOStream = File.OpenWrite(path);
                        break;
                    }

                case "--record":
                    {
                        InputHandler.HandlerState = InputHandler.State.RECORD;

                        var path = args[++i];
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }

                        var source = File.OpenWrite(path);
                        source.Write(InputHandler.ReplayHeader);

                        var compressed = new GZipStream(source, CompressionMode.Compress, leaveOpen: false);
                        InputHandler.IOStream = compressed;
                        break;
                    }

                case "--playback":
                    InputHandler.HandlerState = InputHandler.State.PLAYBACK;

                    var stream = File.OpenRead(args[++i]);
                    var buf = new byte[InputHandler.ReplayHeader.Length];
                    stream.ReadExactly(buf);

                    if (buf.SequenceEqual(InputHandler.ReplayHeader))
                    {
                        // compressed OpenGM replay
                        InputHandler.IOStream = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: false);
                    }
                    else
                    {
                        // legacy GameMaker replay
                        stream.Seek(0, SeekOrigin.Begin);
                        InputHandler.IOStream = stream;
                    }

                    break;

                case "--stack-logs":
                    VMExecutor.VerboseStackLogs = true;
                    VMExecutor.ForceVerboseStackLogs = true;
                    break;

                case "--script-name-filter":
                    VMExecutor.ScriptFilter = new(args[++i]);
                    break;

                case "--enable-flag":
                    CompatFlags.ForcedValues[args[++i]] = true;
                    break;

                case "--disable-flag":
                    CompatFlags.ForcedValues[args[++i]] = false;
                    break;

                case "--":
                    endOfOptions = true;
                    break;

                default:
                    passedArgs.Add(arg);
                    break;
            }
        }

        return passedArgs.ToArray();
    }

    public static DateTime GameLoadTime;

    public static void LoadGame(string dataWinPath, string[] parameters)
    {
        GameLoadTime = DateTime.Now;

        LaunchParameters = parameters;

        // WasmEntry calls LoadGame directly rather than going through Main() (which is what sets
        // this on native), so GamePath was left at its "" default there -- LoadSave.GetFilePrePend
        // then did Path.GetDirectoryName("") (null) and assigned it to Environment.CurrentDirectory,
        // throwing ArgumentNullException the first time any file_* function touched a game-relative
        // path (e.g. file_exists). Main already passes GamePath itself as dataWinPath, so setting it
        // here too is a no-op there and fixes the browser path.
        GamePath = dataWinPath;

        DataWinFolder = new FileInfo(dataWinPath).DirectoryName!;

        if (!File.Exists(Path.Combine(DataWinFolder, "data_OpenGM.win")))
        {
            if (!File.Exists(dataWinPath))
            {
                DebugLog.LogError($"ERROR - data.win not found. Make sure all game files are copied to {DataWinFolder}");
                return;
            }

            Console.WriteLine($"Extracting game assets...");
            using var stream = new FileStream(dataWinPath, FileMode.Open, FileAccess.Read);
            // UndertaleIO.Read throws IOException on ANY warning (even routine, harmless ones,
            // e.g. minor chunk-length/padding mismatches) when given no warning handler --
            // UndertaleModTool's own GUI passes one that just shows a dialog and keeps loading
            // (MainWindow.xaml.cs), never treating a warning as fatal. Match that here instead of
            // letting a benign quirk in one chunk hard-fail the entire load.
            using var data = UndertaleIO.Read(stream, (warning, isImportant) =>
            {
                DebugLog.LogWarning($"[UndertaleModLib] {warning}");
            });
            GameConverter.ConvertGame(data);

#if BROWSER
            // Nothing reads data.win again once data_OpenGM.win exists -- this method's own guard
            // above is the only thing that ever opens it -- and in the browser it is not a file on
            // a disk somewhere, it is bytes held in the wasm heap that everything else is competing
            // for. The Farewell Medley's is 551MB of a 2048MB ceiling, a quarter of the address
            // space spent on a copy of something already converted.
            //
            // Browser-only, emphatically: on desktop this path is the player's actual game file.
            stream.Dispose();
            File.Delete(dataWinPath);
#endif
        }

        //CollisionManager.colliders.Clear();

        AudioManager.Dispose();
        AudioManager.Init();
        GameLoader.LoadGame();
        VersionManager.Init();
        ScriptResolver.InitGMLFunctions(); // needs version stuff

        VMExecutor.EnvStack.Clear();
        VMExecutor.CallStack.Clear();
        InstanceManager.ClearAllInstances();
        DrawManager._drawObjects.Clear();
        DataStructuresFunctions.Clear();
        ParticleManager.Reset();
        VariableResolver.GlobalVariables.Clear();
        VirtualMachine.BuiltInFunctions.NetworkFunctions.Reset();

        // Must run after the resets above (an extension's Init script -- e.g. jtool's bundled TGMS
        // Tweener library setting global.TGMS_SharedTweener = noone -- would otherwise have its
        // globals wiped straight back out by GlobalVariables.Clear()) and before the first room's
        // instances are created (real GM runs it once, automatically, before any Create event).
        ExtensionManager.Init();

        GameSpeed = GameLoader.GeneralInfo.FPS;
        TimingManager.Initialize();
        InstanceManager.NextInstanceID = GameLoader.GeneralInfo.LastObjectID + 1;

        // TODO : is RNG re-initialized after game_change?
        GMRandom.InitRandom(0);

        // Under BROWSER, CustomWindow's real constructor still runs (GraphicsManager.Init(),
        // blend state, Instance = this, etc.) -- it's just backed by OpenTKShim's inert GameWindow/
        // GLFW stand-ins (OpenGM.Wasm/OpenTKShim/Windowing.cs) instead of a real GLFW window, since
        // GLFW itself has no wasm binding. Only the blocking `window.Run()` loop below is unsafe
        // under wasm (would freeze the single-threaded browser tab) and stays excluded.
        if (window == null)
        {
            var gameSettings = GameWindowSettings.Default;
            gameSettings.UpdateFrequency = GameSpeed;
            var nativeSettings = NativeWindowSettings.Default;
            nativeSettings.WindowBorder = WindowBorder.Fixed;
            nativeSettings.ClientSize = GameLoader.GeneralInfo.DefaultWindowSize;
            nativeSettings.APIVersion = new(4, 6); // just require latest version. we use dsa
            nativeSettings.Flags = ContextFlags.Default;
#if DEBUG_EXTRA
            nativeSettings.Flags = ContextFlags.Debug;
#endif
            GLFW.WindowHint(WindowHintBool.ScaleFramebuffer, false);
            GLFW.WindowHint(WindowHintBool.ScaleToMonitor, false);

            window = new CustomWindow(gameSettings, nativeSettings);
        }
        else
        {
            window.ClientSize = GameLoader.GeneralInfo.DefaultWindowSize;
            SurfaceManager.ApplicationWidth = window.FramebufferSize.X;
            SurfaceManager.ApplicationHeight = window.FramebufferSize.Y;
        }

        // Real GM has the application surface up before any game code runs, so GML that reads it
        // during Game Start (Vandal: display_set_gui_size(surface_get_width(application_surface),
        // ...)) gets the real size. Creating it only on the first draw meant surface_get_width
        // returned -1 there -- see SurfaceManager.EnsureApplicationSurface.
        SurfaceManager.EnsureApplicationSurface();

        DebugLog.LogInfo($"Binding page textures...");
        PageManager.BindTextures();

        DebugLog.LogInfo("Compiling shaders...");
        ShaderManager.CompileShaders();

        DebugLog.LogInfo($"Executing global init scripts...");

        VMExecutor.GlobalInit = true;
        foreach (var item in ScriptResolver.GlobalInit)
        {
            VMExecutor.ExecuteCode(item, null);
        }
        VMExecutor.GlobalInit = false;

        DebugLog.LogInfo($"Changing to first room...");

        RoomManager.FirstRoom = true;
        RoomManager.New_Room = GameLoader.GeneralInfo.RoomOrder[0];
        RoomManager.ChangeToWaitingRoom();

#if !BROWSER
        DebugLog.LogInfo($"Starting main loop...");
        window.Run();

        AudioManager.Dispose();
        InputHandler.IOStream?.Close();
#endif
    }

    public static void SetGameSpeed(int fps)
    {
        GameSpeed = fps;

        if (window != null) // only null in tests, same as CustomWindow.Instance
        {
            window.UpdateFrequency = fps;
        }
    }
}
