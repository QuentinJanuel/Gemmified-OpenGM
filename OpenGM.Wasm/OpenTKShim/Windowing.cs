using OpenTK.Mathematics;

// Minimal, mostly-inert replacements for the OpenTK.Windowing.* namespaces (Desktop/Common/
// GraphicsLibraryFramework) -- like GL.cs, these exist purely so CustomWindow.cs and the handful of
// GML built-ins that read/write window chrome (GraphicFunctions.cs, InputHandler.cs,
// MiscFunctions.cs) compile completely unmodified. GLFW itself has no wasm binding at all, so none
// of this is backed by a real native window; CustomWindow is never actually instantiated as a
// GLFW-backed window under BROWSER (see Entry.cs's `#if !BROWSER` guard, unchanged). This shim only
// needs to satisfy the compiler and give sane, inert values; the window chrome a page can honour
// (fullscreen) is handled by BrowserWindow.cs instead.

namespace OpenTK.Windowing.Common
{
    public enum ContextFlags { Default, Debug }
    public enum WindowBorder { Resizable, Fixed, Hidden }
    public enum WindowState { Normal, Minimized, Maximized, Fullscreen }

    public readonly struct FrameEventArgs(double time)
    {
        public double Time { get; } = time;
    }

    public readonly struct UpdateFrameEventArgs(double time)
    {
        public double Time { get; } = time;
    }
}

namespace OpenTK.Windowing.GraphicsLibraryFramework
{
    /// <summary>
    /// Real OpenTK's Keys enum mirrors GLFW key codes, which deliberately align with ASCII (and
    /// therefore Windows virtual-key codes) for space/digits/letters. InputHandler.Convert() relies
    /// on exactly that coincidence -- for any VK code it doesn't special-case, it just casts the raw
    /// VK code straight to Keys ("_ => [(Keys)keyid]"). A-Z/D0-D9/Space below are given the matching
    /// real values so that fallback cast produces the right key; the explicitly-handled members
    /// (arrows, F-keys, etc.) don't strictly need matching values since Convert() names them
    /// directly, but are given them anyway for consistency.
    /// </summary>
    public enum Keys
    {
        Space = 32,
        D0 = 48, D1 = 49, D2 = 50, D3 = 51, D4 = 52, D5 = 53, D6 = 54, D7 = 55, D8 = 56, D9 = 57,
        A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74, K = 75,
        L = 76, M = 77, N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86,
        W = 87, X = 88, Y = 89, Z = 90,
        Escape = 256, Enter = 257, Tab = 258, Backspace = 259, Insert = 260, Delete = 261,
        Right = 262, Left = 263, Down = 264, Up = 265,
        PageUp = 266, PageDown = 267, Home = 268, End = 269,
        F1 = 290, F2 = 291, F3 = 292, F4 = 293, F5 = 294, F6 = 295, F7 = 296, F8 = 297, F9 = 298,
        F10 = 299, F11 = 300, F12 = 301,
        // F13 upwards: no keyboard has had these since the 1980s, which is exactly why they are
        // useful -- a controller-mapping mod can map a controller onto them so that a pad can press a
        // key no player ever can. They are ordinary virtual keys otherwise, and a game is free to
        // read them.
        F13 = 302, F14 = 303, F15 = 304, F16 = 305, F17 = 306, F18 = 307,
        F19 = 308, F20 = 309, F21 = 310, F22 = 311, F23 = 312, F24 = 313,
        KeyPad0 = 320, KeyPad1 = 321, KeyPad2 = 322, KeyPad3 = 323, KeyPad4 = 324,
        KeyPad5 = 325, KeyPad6 = 326, KeyPad7 = 327, KeyPad8 = 328, KeyPad9 = 329,
        LeftShift = 340, LeftControl = 341, LeftAlt = 342, RightShift = 344, RightControl = 345, RightAlt = 346,
    }

    public enum MouseButton { Left, Right, Middle, Button1, Button2, Button3, Button4, Button5 }

    public enum WindowAttribute { Decorated, Resizable }
    public enum WindowAttributeGetBool { Decorated, Resizable }

    public sealed class KeyboardState
    {
        public bool IsKeyDown(Keys key) => OpenGM.BrowserInput.IsKeyDown(key);
        public bool IsKeyPressed(Keys key) => OpenGM.BrowserInput.IsKeyPressed(key);
        public bool IsKeyReleased(Keys key) => false;
        public bool IsAnyKeyDown => OpenGM.BrowserInput.IsAnyKeyDown();
    }

    public sealed class MouseState
    {
        public float X => (float)OpenGM.BrowserInput.GetMouseX();
        public float Y => (float)OpenGM.BrowserInput.GetMouseY();
        public bool IsButtonDown(MouseButton button) => OpenGM.BrowserInput.IsMouseButtonDown(button);
        public Vector2 ScrollDelta => new(0, OpenGM.BrowserInput.GetScrollDeltaY());
    }

    /// <summary>Opaque stand-in for GLFW's native `GLFWwindow*` -- never dereferenced under BROWSER.</summary>
    public sealed class WindowHandle;

    public enum WindowHintBool { ScaleFramebuffer, ScaleToMonitor }

    public static class GLFW
    {
        public static void WindowHint(WindowHintBool hint, bool value) { }
        public static void SetWindowAttrib(WindowHandle window, WindowAttribute attrib, bool value) { }
        public static bool GetWindowAttrib(WindowHandle window, WindowAttributeGetBool attrib) => false;
        public static IntPtr GetWin32Window(WindowHandle window) => IntPtr.Zero;
    }
}

namespace OpenTK.Windowing.Desktop
{
    using OpenTK.Windowing.Common;
    using OpenTK.Windowing.GraphicsLibraryFramework;

    public static class GLFWProvider
    {
        public static void SetErrorCallback(Action<int, string> callback) { }
    }

    public sealed class MonitorInfo
    {
        public int HorizontalResolution { get; init; } = 1920;
        public int VerticalResolution { get; init; } = 1080;
    }

    public static class Monitors
    {
        public static MonitorInfo GetPrimaryMonitor() => new();
    }

    public class GameWindowSettings
    {
        public static GameWindowSettings Default => new();
        public double UpdateFrequency { get; set; }
    }

    public class NativeWindowSettings
    {
        public static NativeWindowSettings Default => new() { ClientSize = new Vector2i(800, 600) };
        public WindowBorder WindowBorder { get; set; }
        public Vector2i ClientSize { get; set; }
        public Version APIVersion { get; set; } = new(4, 6);
        public ContextFlags Flags { get; set; }
        public object? API { get; set; }
        public object? Profile { get; set; }
    }

    /// <summary>
    /// Stand-in base class for the real GLFW-backed GameWindow. Holds plain state instead of
    /// driving a real native window/render loop -- <c>Run</c> is intentionally not implemented for
    /// BROWSER (a real blocking loop would freeze the single-threaded browser tab); frames are
    /// instead pumped from JS via requestAnimationFrame calling into a small additive render entry
    /// point (see WasmEntry.cs / CustomWindow.cs's `#if BROWSER` RenderFrame wrapper).
    /// </summary>
    public class GameWindow
    {
        public GameWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        {
            ClientSize = nativeWindowSettings.ClientSize;
        }

        public Vector2i FramebufferSize { get; private set; }

        private Vector2i _clientSize;
        // On real (native) OpenTK, assigning ClientSize resizes the actual OS window; GLFW then
        // reports the new framebuffer size back and the real GameWindow updates FramebufferSize
        // itself. There's no OS window here, so this setter does that job by hand: keep
        // FramebufferSize in lockstep and resize the actual <canvas> backing buffer to match,
        // otherwise a runtime window_set_size (e.g. a UI toggling a sidebar and resizing the game
        // area) only changes this in-memory property -- the GL viewport, the app-surface stretch
        // target (both keyed off FramebufferSize, see DrawManager.cs), and the DOM canvas's real
        // pixel dimensions (which input.js's mouse-coordinate mapping depends on) all stay stale,
        // producing a stretched frame and wrong mouse coordinates.
        public Vector2i ClientSize
        {
            get => _clientSize;
            set
            {
                _clientSize = value;
                FramebufferSize = value;
                OpenGM.WasmEntry.ResizeCanvas(value.X, value.Y);
            }
        }
        public Vector2i ClientLocation { get; set; }
        public double UpdateFrequency { get; set; }
        public bool IsFocused { get; set; } = true;
        public bool IsFullscreen { get; set; }
        public bool AlwaysOnTop { get; set; }

        private string _title = "";
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OpenGM.BrowserWindow.SetTitle(value);
            }
        }
        public WindowState WindowState { get; set; } = WindowState.Normal;
        public WindowHandle WindowPtr { get; } = new();
        public KeyboardState KeyboardState { get; } = new();
        public MouseState MouseState { get; } = new();

        // Native GLFW would tear the window down and end the run loop here; in the browser there's
        // no run loop to exit, so instead flag it so the JS host can notice the game ended (game_end
        // reaches this via RoomManager.EndGame) and tear the engine down. See WasmEntry.GameEnded.
        public void Close() => OpenGM.WasmEntry.GameEnded = true;
        public void CenterWindow() { }
        public void SwapBuffers() { }

        protected virtual void OnLoad() { }
        protected virtual void OnRenderFrame(FrameEventArgs args) { }
        protected virtual void OnUpdateFrame(UpdateFrameEventArgs args) { }
    }
}
