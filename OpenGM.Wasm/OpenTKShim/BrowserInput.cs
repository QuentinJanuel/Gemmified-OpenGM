using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Runtime.InteropServices.JavaScript;

namespace OpenGM;

/// <summary>
/// Backs the shimmed KeyboardState/MouseState (OpenTKShim/Windowing.cs) with real browser input via
/// wwwroot/input.js. Kept separate from the GL shim since it's not GL-related.
/// </summary>
internal static partial class BrowserInput
{
    private const string Module = "input";

    [JSImport("isKeyDown", Module)]
    private static partial bool IsKeyDownInternal(string code);

    [JSImport("isKeyPressed", Module)]
    private static partial bool IsKeyPressedInternal(string code);

    [JSImport("clearJustPressed", Module)]
    public static partial void ClearJustPressed();

    [JSImport("isAnyKeyDown", Module)]
    public static partial bool IsAnyKeyDown();

    [JSImport("isMouseButtonDown", Module)]
    private static partial bool IsMouseButtonDownInternal(int button);

    [JSImport("getMouseX", Module)]
    public static partial double GetMouseX();

    [JSImport("getMouseY", Module)]
    public static partial double GetMouseY();

    [JSImport("getWheelDeltaY", Module)]
    private static partial double GetWheelDeltaYInternal();

    [JSImport("clearWheelDelta", Module)]
    public static partial void ClearWheelDelta();

    // Negated to match real OpenTK/GLFW's own MouseState.ScrollDelta convention (positive = wheel
    // scrolled up/away from the user), the opposite of the browser's native WheelEvent.deltaY sign.
    public static float GetScrollDeltaY() => (float)-GetWheelDeltaYInternal();

    private static string? MapKey(Keys key) => key switch
    {
        Keys.Backspace => "Backspace",
        Keys.Enter => "Enter",
        Keys.LeftShift => "ShiftLeft",
        Keys.RightShift => "ShiftRight",
        Keys.LeftControl => "ControlLeft",
        Keys.RightControl => "ControlRight",
        Keys.LeftAlt => "AltLeft",
        Keys.RightAlt => "AltRight",
        Keys.Escape => "Escape",
        Keys.PageUp => "PageUp",
        Keys.PageDown => "PageDown",
        Keys.End => "End",
        Keys.Home => "Home",
        Keys.Left => "ArrowLeft",
        Keys.Up => "ArrowUp",
        Keys.Right => "ArrowRight",
        Keys.Down => "ArrowDown",
        Keys.Insert => "Insert",
        Keys.Delete => "Delete",
        Keys.F1 => "F1",
        Keys.F2 => "F2",
        Keys.F3 => "F3",
        Keys.F4 => "F4",
        Keys.F5 => "F5",
        Keys.F6 => "F6",
        Keys.F7 => "F7",
        Keys.F8 => "F8",
        Keys.F9 => "F9",
        Keys.F10 => "F10",
        Keys.F11 => "F11",
        Keys.F12 => "F12",
        Keys.F13 => "F13",
        Keys.F14 => "F14",
        Keys.F15 => "F15",
        Keys.F16 => "F16",
        Keys.F17 => "F17",
        Keys.F18 => "F18",
        Keys.F19 => "F19",
        Keys.F20 => "F20",
        Keys.F21 => "F21",
        Keys.F22 => "F22",
        Keys.F23 => "F23",
        Keys.F24 => "F24",
        Keys.KeyPad0 => "Numpad0",
        Keys.KeyPad1 => "Numpad1",
        Keys.KeyPad2 => "Numpad2",
        Keys.KeyPad3 => "Numpad3",
        Keys.KeyPad4 => "Numpad4",
        Keys.KeyPad5 => "Numpad5",
        Keys.KeyPad6 => "Numpad6",
        Keys.KeyPad7 => "Numpad7",
        Keys.KeyPad8 => "Numpad8",
        Keys.KeyPad9 => "Numpad9",
        Keys.Space => "Space",
        Keys.Tab => "Tab",
        >= Keys.D0 and <= Keys.D9 => "Digit" + (key - Keys.D0),
        >= Keys.A and <= Keys.Z => "Key" + (char)('A' + (key - Keys.A)),
        _ => null,
    };

    public static bool IsKeyDown(Keys key) => MapKey(key) is { } code && IsKeyDownInternal(code);
    public static bool IsKeyPressed(Keys key) => MapKey(key) is { } code && IsKeyPressedInternal(code);

    private static int MapButton(MouseButton button) => button switch
    {
        MouseButton.Left => 0,
        MouseButton.Middle => 1,
        MouseButton.Right => 2,
        MouseButton.Button1 => 3,
        MouseButton.Button2 => 4,
        _ => -1,
    };

    public static bool IsMouseButtonDown(MouseButton button) => IsMouseButtonDownInternal(MapButton(button));
}
