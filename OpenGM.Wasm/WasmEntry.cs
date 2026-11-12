using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Rendering;
using System.Runtime.InteropServices.JavaScript;
using UndertaleModLib;

namespace OpenGM;

/// <summary>
/// Browser-wasm entry point -- mirrors <see cref="Entry"/>'s own `LoadGame`/main-loop shape, but
/// driven by a JS `requestAnimationFrame` loop calling <see cref="RenderFrame"/> once per frame
/// instead of a blocking `window.Run()` (which doesn't exist here -- see OpenTKShim/Windowing.cs
/// and the `#if !BROWSER` guard still around the real `window.Run()` call in `Entry.cs`).
///
/// Two ways in: <see cref="LoadGame"/> takes an already-converted `data_OpenGM.win` blob (used for
/// the bundled sample games, produced offline ahead of time). <see cref="WriteGameFile"/> +
/// <see cref="LoadGameFromDataWin"/> take a raw, unconverted game folder (a real `data.win`, plus
/// whatever loose assets sit next to it -- audio group .dat files, external .ogg, etc, exactly like
/// a user would drag out of a GameMaker install dir) and run the conversion in-browser:
/// `UndertaleModLib`/`GameConverter` are already part of this build's shared `OpenGM/**/*.cs`
/// source (see OpenGM.Wasm.csproj), and `Entry.LoadGame` already knows to invoke them itself
/// whenever a `data_OpenGM.win` isn't already sitting next to the `data.win` it's given -- so this
/// needed no new conversion logic, just staging the dropped files into the wasm in-memory FS at the
/// paths that existing code already expects.
/// </summary>
public static partial class WasmEntry
{
    // Modules only need importing once per page load; JS's own module cache would no-op a second
    // ImportAsync of the same specifier anyway, but this avoids even asking on every reload.
    private static bool _modulesImported;

    // Guards RenderFrame against running before any game has been loaded yet (idle on first page
    // load, waiting for a drop) and against a stale previous game's state being stepped for one
    // frame while a new one is mid-load.
    internal static bool IsGameLoaded { get; private set; }

    // Set when the game ends (game_end -> RoomManager.EndGame -> GameWindow.Close). The JS host
    // polls IsGameEnded() after each frame to surface "Game ended" and tear the engine down, the way
    // native OpenGM would exit its run loop. Reset on every load.
    internal static bool GameEnded { get; set; }

    internal static bool IsGameEnded() => GameEnded;

    // wwwroot/*.js modules are plain static files (unlike dotnet.js/the .wasm payload itself, which
    // get content-hashed filenames via the SDK's static web assets pipeline) -- a browser can serve
    // a stale cached copy of one indefinitely, surviving even a hard refresh in some cases (a real,
    // repeatedly-hit issue during this project's own testing). Bumping this query suffix any time
    // one of these files changes forces every browser to treat it as a brand new URL, bypassing
    // both the normal HTTP cache and the JS engine's own per-specifier ES module registry cache --
    // no reliance on the tester remembering to hard-refresh or clear Cache Storage.
    private const string AssetVersion = "?v=9";

    private static async Task EnsureModulesImportedAsync()
    {
        if (_modulesImported)
        {
            return;
        }

        // Must be imported before Entry.LoadGame runs -- it calls GraphicsManager.Init() and
        // ShaderManager.CompileShaders() internally, both of which touch GL.* (OpenTKShim/GL.cs),
        // which is backed by this module.
        await JSHost.ImportAsync("webgl2", "../webgl2.js" + AssetVersion);
        // Backs OpenTKShim/BrowserInput.cs -- real keyboard/mouse state read from
        // InputHandler.UpdateKeyboardState/UpdateMouseState inside RenderFrame below.
        await JSHost.ImportAsync("input", "../input.js" + AssetVersion);
        // Backs BrowserDialog.cs -- the blocking GML dialogs (show_message/show_question/get_string/
        // get_integer) that suspend the worker via Atomics.wait while the main thread shows the
        // styled React dialog.
        await JSHost.ImportAsync("dialog", "../dialog.js" + AssetVersion);
        // Backs BrowserWindow.cs -- window_set_caption sets the real browser tab title. CustomWindow's
        // constructor (inside Entry.LoadGame) doesn't set Title itself, but window_set_caption can be
        // called as early as a Create event, so this needs to be ready before LoadGame runs too.
        await JSHost.ImportAsync("browserwindow", "../browserwindow.js" + AssetVersion);
        // Backs OpenTKShim/AL.cs -- AudioManager.Init() (inside Entry.LoadGame) calls
        // ALC.OpenDevice/CreateContext/MakeContextCurrent, all backed by this module.
        await JSHost.ImportAsync("webaudio", "../webaudio.js" + AssetVersion);

        _modulesImported = true;
    }

    private static void WriteShaderFiles()
    {
        File.WriteAllText("shader.vsh", VertexShaderEs300);
        File.WriteAllText("shader.fsh", FragmentShaderEs300);
    }

    // internal, not private: also called from OpenTKShim/Windowing.cs's GameWindow.ClientSize
    // setter, so a runtime window_set_size/window_set_rectangle call (real GLFW resizes the OS
    // window and updates FramebufferSize automatically on native; there's no OS window here) keeps
    // the actual <canvas> backing buffer in sync instead of only updating the in-memory ClientSize
    // property.
    internal static void ResizeCanvas(int width, int height)
    {
        Gems.Size = (width, height);
        ResizeCanvasJs(width, height);
    }

    [JSImport("resizeCanvas", "webgl2")]
    private static partial void ResizeCanvasJs(int width, int height);

    // webaudio.js's sound sources play on their own Web Audio timeline once started -- nothing
    // about RenderFrame no-op'ing (below) stops already-triggered audio_play_sound/instrument
    // playback, so this is called explicitly.
    [JSImport("stopAll", "webaudio")]
    internal static partial void StopAllAudio();

    // Stops the currently-loaded game -- called from the web shell when the page switches away to
    // a *different engine* entirely (loading another GMS game in place just goes through
    // LoadGameFromDataWin again, which already replaces the current game cleanly). Without this,
    // switching to OpenGMK left OpenGM's RenderFrame loop and already-playing audio running
    // forever in the background alongside whatever loaded next.
    internal static void UnloadGame()
    {
        IsGameLoaded = false;
        if (_modulesImported)
        {
            StopAllAudio();
        }
    }

    private static void RunLoadGame(string dataWinPath)
    {
        IsGameLoaded = false;
        GameEnded = false;
        // A fresh load needs its own frame-timing baseline -- otherwise a large gap accumulated
        // while idle (or against the previous game's pacing) would fast-forward the new game
        // through a burst of catch-up steps the instant it starts.
        _lastFrameTimeSeconds = -1;
        _accumulatedSeconds = 0;

        try
        {
            // parameter_string(0) is the program itself, as on a real command line.
            Entry.LoadGame(dataWinPath, [dataWinPath, .. _launchArgs]);
            // The DOM canvas's actual backing-buffer size only ever starts out at index.html's
            // static placeholder attributes -- match it to this game's real resolution now that
            // GameLoader has read it, or GL.Viewport ends up larger than the canvas's real drawing
            // buffer and anything outside the placeholder size silently gets clipped (this is what
            // "crops the game" turned out to be: Indigo Needle's real 800x608 against an 800x600
            // canvas). Every load, not just the first, since a second game can be a different size.
            ResizeCanvas(GameLoader.GeneralInfo.DefaultWindowSize.X, GameLoader.GeneralInfo.DefaultWindowSize.Y);
            IsGameLoaded = true;
        }
        catch (Exception ex)
        {
            // The default ManagedError surfaced to JS collapses TypeInitializationException down to
            // just its outer type name, hiding the actual InnerException -- print the full chain
            // here so the real root cause is visible in the browser console.
            for (var e = ex; e is not null; e = e.InnerException)
            {
                Console.WriteLine($"[LoadGame error] {e.GetType().FullName}: {e.Message}\n{e.StackTrace}");
            }
            throw;
        }
    }

    // shader.vsh/shader.fsh (native, #version 330 core, texture2D()/gl_FragColor) are invalid
    // GLSL under WebGL2's GLSL ES 3.00 (#version 300 es, texture(), explicit `out vec4`).
    // ShaderManager.CompileShaders() reads these via File.ReadAllText("shader.vsh"/"shader.fsh")
    // relative to the working directory -- writing ES-flavored copies to those exact paths before
    // Entry.LoadGame runs (which calls CompileShaders internally) swaps them in with zero changes
    // to OpenGM's own shared source. Native build's real files on disk are untouched.
    private const string VertexShaderEs300 = """
        #version 300 es

        #define MATRIX_VIEW 0
        #define MATRIX_PROJECTION 1
        #define MATRIX_WORLD 2
        #define MATRIX_WORLD_VIEW 3
        #define MATRIX_WORLD_VIEW_PROJECTION 4
        #define MATRICES_MAX 5
        #define FOG_SETTINGS 0
        #define FOG_COLOUR 1

        layout (location = 0) in vec3 a_pos;
        layout (location = 1) in vec4 a_color;
        layout (location = 2) in vec2 a_uv;

        out vec4 fcolor;
        out vec2 texc;
        out float fogFactor;

        uniform mat4 gm_Matrices[MATRICES_MAX];
        uniform float gm_FogStart;
        uniform float gm_RcpFogRange;
        uniform bool gm_VS_FogEnabled;

        float CalcFogFactor(vec4 pos)
        {
        	if (gm_VS_FogEnabled)
        	{
        		vec4 viewpos = gm_Matrices[MATRIX_WORLD_VIEW] * pos;
        		// -viewpos.z: see shader.vsh, whose comment explains the sign. Fog distances are
        		// measured in front of the camera, and this is a GL view matrix, so view-space z is
        		// negative there. Keep the two copies in step.
        		float fogfactor = ((-viewpos.z - gm_FogStart) * gm_RcpFogRange);
        		return fogfactor;
        	}
        	else
        	{
        		return 0.0;
        	}
        }

        void main() {
            fcolor = a_color;
            texc = a_uv;
            vec4 pos = vec4(a_pos.xyz, 1);
            fogFactor = CalcFogFactor(pos);
            gl_Position = gm_Matrices[MATRIX_WORLD_VIEW_PROJECTION] * pos;
            gl_PointSize = 1.0;
        }
        """;

    private const string FragmentShaderEs300 = """
        #version 300 es
        precision mediump float;

        in vec4 fcolor;
        in vec2 texc;
        in float fogFactor;

        uniform sampler2D gm_BaseTexture;
        uniform bool gm_PS_FogEnabled;
        uniform vec4 gm_FogColour;
        uniform bool gm_AlphaTestEnabled;
        uniform float gm_AlphaRefValue;

        out vec4 outColor;

        void DoAlphaTest(vec4 SrcColour)
        {
            if (gm_AlphaTestEnabled)
            {
                if (SrcColour.a <= gm_AlphaRefValue)
                {
                    discard;
                }
            }
        }

        void DoFog(inout vec4 SrcColour, float fogval)
        {
        	if (gm_PS_FogEnabled)
        	{
        		// RGB only; see shader.fsh, whose comment explains why. Keep the two in step.
        		SrcColour.rgb = mix(SrcColour.rgb, gm_FogColour.rgb, clamp(fogval, 0.0, 1.0));
        	}
        }

        void main() {
            vec4 color = texture(gm_BaseTexture, texc).rgba * fcolor.rgba;
            DoAlphaTest(color);
            DoFog(color, fogFactor);
            outColor = color;
        }
        """;

    [JSExport]
    internal static async Task LoadGame(byte[] dataOpenGmWinBytes)
    {
        await EnsureModulesImportedAsync();

        // Keep the browser console down to warnings/errors (which already cover every unresolved-
        // function/stub/crash case -- see DebugLog.LogWarning/.LogError call sites) instead of the
        // Info-level chatter (room changes, compat flags, per-call window_set_caption, etc) real GM
        // debugging normally wants -- makes it practical to just play the game and see exactly what
        // still needs implementing. Native's own default (DebugLog.cs) is untouched; this only
        // applies to this browser-wasm entry point.
        DebugLog.Verbosity = DebugLog.LogType.Warning;

        if (Directory.Exists("/game"))
        {
            Directory.Delete("/game", true);
        }
        Directory.CreateDirectory("/game");
        File.WriteAllBytes("/game/data_OpenGM.win", dataOpenGmWinBytes);
        WriteShaderFiles();

        // The "data.win" path itself is never actually read here: Entry.LoadGame only touches it
        // if "data_OpenGM.win" is missing (the offline-conversion path), which it isn't -- it's
        // used solely to compute DataWinFolder.
        RunLoadGame("/game/data.win");
    }

    // Stages one file of a dropped/picked game folder into the wasm in-memory FS ahead of
    // LoadGameFromDataWin -- called once per file from JS (the raw `data.win` itself, plus any
    // loose assets sitting next to it: audiogroup*.dat, external .ogg, etc), each keyed by its
    // path relative to the game folder's own root so GameConverter's own relative File.OpenWrite/
    // FileStream calls (see ExportSounds' audio-group lookup) resolve exactly as they would
    // running natively next to a real data.win.
    internal static void WriteGameFile(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine("/game", relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(fullPath, bytes);
    }

    // Clears out any previous game's staged files before a new WriteGameFile/LoadGameFromDataWin
    // sequence starts. Without this, a second drop would find the first game's already-converted
    // "/game/data_OpenGM.win" still sitting there and Entry.LoadGame would skip conversion entirely
    // (see its `if (!File.Exists(.../data_OpenGM.win))` guard), silently loading the previous game's
    // data again instead of the new one.
    internal static void ResetGameFiles()
    {
        if (Directory.Exists("/game"))
        {
            Directory.Delete("/game", true);
        }
        Directory.CreateDirectory("/game");
    }

    // Converts + loads whatever was staged via WriteGameFile("data.win", ...) (plus any loose
    // assets alongside it) -- the in-browser equivalent of the desktop build's conversion step:
    // UndertaleModLib/GameConverter run inside the wasm module itself, triggered automatically by
    // Entry.LoadGame the moment it sees no data_OpenGM.win already sitting next to the data.win it's given.
    internal static async Task LoadGameFromDataWin()
    {
        await EnsureModulesImportedAsync();
        DebugLog.Verbosity = DebugLog.LogType.Warning;
        WriteShaderFiles();
        RunLoadGame("/game/data.win");
    }

    // requestAnimationFrame fires at the display's refresh rate (commonly 60Hz, sometimes more),
    // completely unrelated to the game's own speed (Entry.GameSpeed -- the project's General Info
    // FPS, also what GML's room_speed reads/writes, see VariableResolver.cs). Native paces
    // DrawManager.FixedUpdate() (one full GM step, logic + draw, per call) via
    // `gameSettings.UpdateFrequency = GameSpeed` (Entry.cs), which real OpenTK's GLFW-backed
    // GameWindow.Run() loop actually throttles to. The wasm GameWindow shim's UpdateFrequency is
    // inert (OpenTKShim/Windowing.cs), so without this accumulator every rAF tick would run a full
    // step -- e.g. 2x too fast on a 60Hz display for a 30fps game. Reading Entry.GameSpeed fresh
    // every tick (rather than caching it) keeps this correct if a game sets room_speed at runtime.
    private static double _lastFrameTimeSeconds = -1;
    private static double _accumulatedSeconds;

    internal static void RenderFrame(double time)
    {
        if (!IsGameLoaded)
        {
            return;
        }

        try
        {
            var timeSeconds = time / 1000.0;
            if (_lastFrameTimeSeconds < 0)
            {
                _lastFrameTimeSeconds = timeSeconds;
            }

            var elapsed = timeSeconds - _lastFrameTimeSeconds;
            _lastFrameTimeSeconds = timeSeconds;

            // A backgrounded/throttled tab can report a huge gap when it resumes -- drop the lost
            // time instead of fast-forwarding through a burst of catch-up steps.
            const double MaxElapsedSeconds = 0.25;
            if (elapsed > MaxElapsedSeconds)
            {
                elapsed = MaxElapsedSeconds;
            }

            _accumulatedSeconds += elapsed;

            var stepSeconds = 1.0 / Entry.GameSpeed;

            // Cap catch-up steps per tick so a single slow frame can't spiral.
            const int MaxStepsPerTick = 4;
            var stepsThisTick = 0;
            while (_accumulatedSeconds >= stepSeconds && stepsThisTick < MaxStepsPerTick)
            {
                Rendering.CustomWindow.Instance.RenderFrame(stepSeconds);
                _accumulatedSeconds -= stepSeconds;
                stepsThisTick++;

                // "Just pressed this frame" state needs to reset once InputHandler has had a
                // chance to read it for this step, before the next catch-up step (if any) runs.
                BrowserInput.ClearJustPressed();
            }
        }
        catch (Exception ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
            {
                Console.WriteLine($"[RenderFrame error] {e.GetType().FullName}: {e.Message}\n{e.StackTrace}");
            }
            throw;
        }
    }

    internal static void SetAudioCachePolicy(int shortCompressedBytes, double shortDurationSecs, double musicBudgetBytes)
    {
        AudioManager.SetAudioCachePolicy(shortCompressedBytes, shortDurationSecs, (long)musicBudgetBytes);
    }

    internal static string GetAudioCacheStatsJson()
    {
        var s = AudioManager.GetAudioCacheStats();
        var last = s.LastEventType is null
            ? "null"
            : $"{{\"type\":\"{s.LastEventType}\",\"id\":\"{EscapeJson(s.LastEventId ?? "")}\",\"at\":{s.LastEventAt.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        return "{"
            + $"\"decodedMusicCount\":{s.DecodedMusicCount},"
            + $"\"decodedMusicBytes\":{s.DecodedMusicBytes},"
            + $"\"decodedSfxCount\":{s.DecodedSfxCount},"
            + $"\"compressedBytes\":{s.CompressedBytes},"
            + $"\"pinnedCount\":{s.PinnedCount},"
            + $"\"lastEvent\":{last}"
            + "}";
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    // -- Launch arguments and loopback (VirtualMachine/BuiltInFunctions/NetworkFunctions.cs) ----

    /// <summary>The command line after the program; read by the next load's parameter_string.</summary>
    private static string[] _launchArgs = [];

    /// <summary>
    /// What the next load is launched with: its command line after the program, and the loopback
    /// ports the host answers. Call before LoadGameFromDataWin.
    /// </summary>
    internal static void SetLaunch(string[] args, int[] ports)
    {
        _launchArgs = args;
        VirtualMachine.BuiltInFunctions.NetworkFunctions.Ports = ports;
    }

    [JSExport]
    internal static void Step()
    {
        DrawManager.HeadlessStep();
    }
}
