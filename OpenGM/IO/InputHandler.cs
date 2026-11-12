using OpenGM.Rendering;
using OpenGM.VirtualMachine;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Text;

namespace OpenGM.IO;

public class InputHandler
{
    public enum State
    {
        NORMAL,
        PLAYBACK,
        RECORD
    }

    public static readonly byte[] ReplayHeader = "OGMR"u8.ToArray();

    public static State HandlerState = State.NORMAL;
    public static Stream? IOStream;

    public static bool[] KeyDown = new bool[256];
    public static bool[] KeyPressed = new bool[256];
    public static bool[] KeyReleased = new bool[256];

    /// <summary>
    /// What each physical key reports as -- keyboard_set_map's table, identity until a game changes
    /// it. Indexed by the virtual key the player presses; the value is the one the game will see.
    /// </summary>
    /// <remarks>
    /// A value below 2 means discard the press: vk_nokey and vk_anykey are not real keys but
    /// questions answered by sweeping this array (see keyboard_check's case 0/1), so a press written
    /// into their slots would make "no key is down" report that one is.
    /// </remarks>
    public static byte[] KeyMap = IdentityKeyMap();

    /// <summary>Raw key states for one frame, and the same frame folded through KeyMap. Reused
    /// rather than allocated every frame -- this runs once per step.</summary>
    private static readonly bool[] RawKeys = new bool[256];
    private static readonly bool[] MappedKeys = new bool[256];

    /// <summary>
    /// Folds one frame of real key states through <see cref="KeyMap"/>, writing what the game will
    /// see into <paramref name="mapped"/>.
    /// </summary>
    /// <remarks>
    /// Separate from reading the keyboard, and pure, because this is where a rebind is either right
    /// or silently wrong. Two things make it more than an array copy: the mapping is many-to-one, so
    /// two keys reporting as the same one must OR rather than overwrite; and a key mapped below vk 2
    /// is discarded, because vk_nokey and vk_anykey are questions answered by sweeping this array
    /// rather than slots in it, so a press stored there would make "no key is down" report one.
    ///
    /// Slots 0 and 1 are left alone for that reason -- they are maintained separately from whether
    /// any key is physically down.
    /// </remarks>
    public static void ApplyKeyMap(bool[] raw, bool[] mapped)
    {
        for (var i = 2; i < 256; i++)
        {
            mapped[i] = false;
        }

        for (var i = 2; i < 256; i++)
        {
            if (!raw[i])
            {
                continue;
            }

            var to = KeyMap[i];
            if (to < 2)
            {
                continue;
            }

            mapped[to] = true;
        }
    }

    public static byte[] IdentityKeyMap()
    {
        var map = new byte[256];
        for (var i = 0; i < map.Length; i++)
        {
            map[i] = (byte)i;
        }
        return map;
    }

    // Backs the keyboard_key/keyboard_lastkey built-in variables (VariableResolver.cs). Matches
    // real GM8 semantics (confirmed against OpenGMK's Input::button_press/button_release):
    // keyboard_key is set to whichever key was just pressed and reset to vk_nokey (0) once THAT
    // specific key is released; keyboard_lastkey is set alongside it on press but never reset on
    // release, so it keeps reporting the most recently pressed key even after it's back up.
    public static int KeyboardKey = 0;
    public static int KeyboardLastKey = 0;

    public static bool[] MouseDown = new bool[5];
    public static bool[] MousePressed = new bool[5];
    public static bool[] MouseReleased = new bool[5];

    public static Vector2 MousePos = new();

    public static bool MouseWheelUp;
    public static bool MouseWheelDown;

    public static string KeyboardString = "";

    public static void UpdateMouseState(MouseState state)
    {
        var mouseButtons = new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.Button1, MouseButton.Button2 };

        for (var i = 0; i < 5; i++)
        {
            var isDown = state.IsButtonDown(mouseButtons[i]);
            var wasDown = MouseDown[i];

            MousePressed[i] = isDown && !wasDown;
            MouseReleased[i] = !isDown && wasDown;
            MouseDown[i] = isDown;
        }

        MousePos.X = state.X;
        MousePos.Y = state.Y;

        // Real OpenTK's ScrollDelta is already a per-frame delta on native (auto-reset by its own
        // event pump); the wasm shim's version reads from a JS-side accumulator that only resets
        // via an explicit clear, so that has to happen here, once per frame, right after reading it.
        var scrollY = state.ScrollDelta.Y;
        MouseWheelUp = scrollY > 0;
        MouseWheelDown = scrollY < 0;
#if BROWSER
        BrowserInput.ClearWheelDelta();
#endif
    }

    /// <summary>
    /// https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
    /// most virtual keys map to opentk keys, but some dont, so we gotta do that here
    /// </summary>
    public static Keys[] Convert(int keyid)
    {
        return keyid switch
        {
            0x08 => [Keys.Backspace],
            0x0D => [Keys.Enter],
            0x10 => [Keys.LeftShift, Keys.RightShift],
            0x11 => [Keys.LeftControl, Keys.RightControl],
            0x12 => [Keys.LeftAlt, Keys.RightAlt],
            0x1B => [Keys.Escape],

            0x21 => [Keys.PageUp],
            0x22 => [Keys.PageDown],
            0x23 => [Keys.End],
            0x24 => [Keys.Home],
            0x25 => [Keys.Left],
            0x26 => [Keys.Up],
            0x27 => [Keys.Right],
            0x28 => [Keys.Down],
            0x2D => [Keys.Insert],
            0x2E => [Keys.Delete],

            0x70 => [Keys.F1],
            0x71 => [Keys.F2],
            0x72 => [Keys.F3],
            0x73 => [Keys.F4],
            0x74 => [Keys.F5],
            0x75 => [Keys.F6],
            0x76 => [Keys.F7],
            0x77 => [Keys.F8],
            0x78 => [Keys.F9],
            0x79 => [Keys.F10],
            0x7A => [Keys.F11],
            0x7B => [Keys.F12],
            0x7C => [Keys.F13],
            0x7D => [Keys.F14],
            0x7E => [Keys.F15],
            0x7F => [Keys.F16],
            0x80 => [Keys.F17],
            0x81 => [Keys.F18],
            0x82 => [Keys.F19],
            0x83 => [Keys.F20],
            0x84 => [Keys.F21],
            0x85 => [Keys.F22],
            0x86 => [Keys.F23],
            0x87 => [Keys.F24],

            0xA0 => [Keys.LeftShift],
            0xA1 => [Keys.RightShift],

            _ => [(Keys)keyid]
        };
    }

    private static void RecordIOState()
    {
        if (IOStream == null)
        {
            throw new NullReferenceException("IOStream is null.");
        }

        IOStream.Write(new byte[4]); // IO_LastChar

        // TODO: no idea if this matches the runner, but assuming that
        // it's a 4 byte length + 4096 byte buffer

        var inputBytes = Encoding.ASCII.GetBytes(KeyboardString);
        IOStream.Write(BitConverter.GetBytes(inputBytes.Length));

        Array.Resize(ref inputBytes, 4096);
        IOStream.Write(inputBytes);

        IOStream.Write(new byte[4]); // IO_LastKey
        IOStream.Write(new byte[4]); // IO_CurrentKey

        for (var i = 0; i < 256; i++)
        {
            IOStream.WriteByte(KeyDown[i] ? (byte)1 : (byte)0);
        }

        for (var i = 0; i < 256; i++)
        {
            IOStream.WriteByte(KeyReleased[i] ? (byte)1 : (byte)0);
        }

        for (var i = 0; i < 256; i++)
        {
            IOStream.WriteByte(KeyPressed[i] ? (byte)1 : (byte)0);
        }

        IOStream.Write(new byte[40]); // IO_LastButton
        IOStream.Write(new byte[40]); // IO_CurrentButton

        IOStream.Write(new byte[50]); // IO_ButtonDown
        IOStream.Write(new byte[50]); // IO_ButtonReleased
        IOStream.Write(new byte[50]); // IO_ButtonPressed

        IOStream.Write(new byte[10]); // IO_WheelUp
        IOStream.Write(new byte[10]); // IO_WheelDown

        IOStream.Write(new byte[8]); // IO_MousePos
        IOStream.Write(new byte[4]); // IO_MouseX
        IOStream.Write(new byte[4]); // IO_MouseY
    }

    private static void PlaybackIOState()
    {
        if (IOStream == null)
        {
            throw new NullReferenceException("IOStream is null.");
        }

        CustomWindow.Instance.UpdateFrequency = 0.0;

        try
        {
            IOStream.ReadExactly(new byte[4]); // IO_LastChar

            var inputLengthBuf = new byte[4];
            IOStream.ReadExactly(inputLengthBuf);
            var inputLength = BitConverter.ToInt32(inputLengthBuf);

            var inputBytes = new byte[4096];
            IOStream.ReadExactly(inputBytes);
            KeyboardString = Encoding.ASCII.GetString(inputBytes[..inputLength]);

            IOStream.ReadExactly(new byte[4]); // IO_LastKey
            IOStream.ReadExactly(new byte[4]); // IO_CurrentKey

            for (var i = 0; i < 256; i++)
            {
                KeyDown[i] = IOStream.ReadByte() == 1;
            }

            for (var i = 0; i < 256; i++)
            {
                KeyReleased[i] = IOStream.ReadByte() == 1;
            }

            for (var i = 0; i < 256; i++)
            {
                KeyPressed[i] = IOStream.ReadByte() == 1;
            }

            IOStream.ReadExactly(new byte[40]); // IO_LastButton
            IOStream.ReadExactly(new byte[40]); // IO_CurrentButton

            IOStream.ReadExactly(new byte[50]); // IO_ButtonDown
            IOStream.ReadExactly(new byte[50]); // IO_ButtonReleased
            IOStream.ReadExactly(new byte[50]); // IO_ButtonPressed

            IOStream.ReadExactly(new byte[10]); // IO_WheelUp
            IOStream.ReadExactly(new byte[10]); // IO_WheelDown

            IOStream.ReadExactly(new byte[8]); // IO_MousePos
            IOStream.ReadExactly(new byte[4]); // IO_MouseX
            IOStream.ReadExactly(new byte[4]); // IO_MouseY
        }
        catch (EndOfStreamException)
        {
            DebugLog.LogInfo("Hit EOF, ending replay.");
            CustomWindow.Instance.UpdateFrequency = Entry.GameSpeed;
            IOStream.Close();
            HandlerState = State.NORMAL;
        }
    }

    public static void UpdateKeyboardState(KeyboardState state)
    {
        if (HandlerState == State.PLAYBACK)
        {
            PlaybackIOState();
            return;
        }

        void CalculateKey(int vk, bool isDown)
        {
            var wasDown = KeyDown[vk];

            KeyPressed[vk] = isDown && !wasDown;
            KeyReleased[vk] = !isDown && wasDown;
            KeyDown[vk] = isDown;
        }

        CalculateKey(0, !state.IsAnyKeyDown); // vk_nokey
        CalculateKey(1, state.IsAnyKeyDown); // vk_anykey

        // Read every key's real state first, then fold the whole frame through KeyMap in one go --
        // see ApplyKeyMap for why that cannot be done while sweeping.
        for (var i = 2; i < 256; i++)
        {
            RawKeys[i] = CustomWindow.Instance.IsFocused && Convert(i).Any(state.IsKeyDown);
        }

        ApplyKeyMap(RawKeys, MappedKeys);

        for (var i = 2; i < 256; i++)
        {
            CalculateKey(i, MappedKeys[i]);

            if (KeyPressed[i])
            {
                KeyboardKey = i;
                KeyboardLastKey = i;
            }
            else if (KeyReleased[i] && KeyboardKey == i)
            {
                KeyboardKey = 0;
            }
        }

        // TODO: account for caps lock?
        for (var i = 'A'; i <= 'Z'; i++)
        {
            if (KeyPressed[i])
            {
                var chr = i;

                if (!KeyPressed[0x10])
                {
                    chr += (char)32;
                }

                KeyboardString += chr;
            }
        }

        if (KeyPressed[8] && KeyboardString.Length > 0)
        {
            // backspace
            KeyboardString = KeyboardString[..^1];
        }

        // debug
        VMExecutor.VerboseStackLogs = VMExecutor.ForceVerboseStackLogs || state.IsKeyDown(Keys.F1);

        if (state.IsKeyDown(Keys.F2))
        {
            CustomWindow.Instance.UpdateFrequency = 0.0; // This means fastest
        }
        else if (state.IsKeyDown(Keys.F3))
        {
            CustomWindow.Instance.UpdateFrequency = 2;
        }
        else
        {
            CustomWindow.Instance.UpdateFrequency = Entry.GameSpeed;
        }

        if (state.IsKeyPressed(Keys.F5))
        {
            VMExecutor.DebugMode = !VMExecutor.DebugMode;
            VariableResolver.GlobalVariables["debug"] = VMExecutor.DebugMode;
            DebugLog.LogInfo($"Debug mode : {VMExecutor.DebugMode}");
        }

        if (state.IsKeyPressed(Keys.F6))
        {
            TimingManager.DebugTime = !TimingManager.DebugTime;
        }

        if (state.IsKeyPressed(Keys.KeyPad0))
        {
            DebugLog.PrintInstances(DebugLog.LogType.Info);
            DebugLog.PrintInactiveInstances(DebugLog.LogType.Info);
            DebugLog.PrintDrawObjects(DebugLog.LogType.Info);
        }

        if (state.IsKeyPressed(Keys.KeyPad1))
        {
            DebugLog.Log("LAYERS :");
            foreach (var (id, layer) in RoomManager.CurrentRoom.Layers)
            {
                DebugLog.Log($" - Layer: {layer.Name} ({id})");
                foreach (var element in layer.ElementsToDraw)
                {
                    var str = $"     - {element.GetType().Name} ({element.instanceId})";
                    if (element is GMSprite sprite)
                    {

                        str += $" [{sprite.X}, {sprite.Y}]";
                    }
                    else if (element is GMBackground bg)
                    {

                        str += $" Index:{bg.Element.Index} Frame:{bg.FrameIndex}";
                    }
                    DebugLog.Log(str);
                }
            }
        }

        if (state.IsKeyPressed(Keys.KeyPad2))
        {
            DrawManager.DebugBBoxes = !DrawManager.DebugBBoxes;
            DebugLog.LogInfo($"Draw bounding boxes: {DrawManager.DebugBBoxes}");
        }

        if (state.IsKeyPressed(Keys.KeyPad3))
        {
            if (HandlerState == State.RECORD)
            {
                DebugLog.LogInfo("Finished recording.");
                HandlerState = State.NORMAL;
            }
        }

        if (state.IsKeyPressed(Keys.KeyPad4))
        {
            DrawManager.ShouldDrawGui = !DrawManager.ShouldDrawGui;
            DebugLog.LogInfo($"GUI rendering {(DrawManager.ShouldDrawGui ? "enabled" : "disabled")}.");
        }

        if (state.IsKeyPressed(Keys.KeyPad5))
        {
            GraphicsManager.EnableCulling = !GraphicsManager.EnableCulling;
            DebugLog.LogInfo($"Frustum culling {(GraphicsManager.EnableCulling ? "enabled" : "disabled")}.");
        }

        if (state.IsKeyPressed(Keys.KeyPad6))
        {
            CameraManager.LockToRoomBounds = !CameraManager.LockToRoomBounds;
            DebugLog.LogInfo($"Camera room bounds locking {(CameraManager.LockToRoomBounds ? "enabled" : "disabled")}.");
        }

        if (HandlerState == State.RECORD)
        {
            RecordIOState();
        }
    }

    public static bool KeyboardCheckDirect(int key)
    {
        // TODO : work out how this is different? tried using GetAsyncKeyState and it didnt work
        if (key < 256)
        {
            return KeyDown[key];
        }

        return false;
    }
}
