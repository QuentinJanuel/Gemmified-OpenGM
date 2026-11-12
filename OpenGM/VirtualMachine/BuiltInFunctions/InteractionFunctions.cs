using OpenGM.IO;
using OpenGM.Rendering;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class InteractionFunctions
    {
        // show_message
        // show_question
        // show_message_async
        // show_question_async
        // show_error
        // show_info
        // load_info
        // highscore_clear
        // highscore_add
        // highscore_value
        // highscore_name
        // draw_highscore
        // get_integer
        // get_integer_async
        // get_string
        // get_string_async
        // get_login_async
        // keyboard_get_numlock
        // keyboard_set_numlock

        // Blocking dialogs. On browser-wasm these show the styled in-page React dialog, blocking the
        // (worker) engine thread via Atomics.wait until answered -- see BrowserDialog.cs / dialog.js.
        // Button captions are joined by U+001F; primary/cancel are indices for the Enter/Escape
        // defaults (cancel -1 = not dismissible); inputKind 0 none / 1 string / 2 integer.

        [GMLFunction("show_message")]
        public static object? show_message(object?[] args)
        {
            var text = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());
#if BROWSER
            BrowserDialog.ShowDialog(text, "OK", 0, -1, 0, "");
#endif
            return null;
        }

        [GMLFunction("show_question")]
        public static object? show_question(object?[] args)
        {
            var text = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());
#if BROWSER
            // No (index 0) is the Escape default; Yes (index 1) the Enter default.
            return BrowserDialog.ShowDialog(text, "No\u001fYes", 1, 0, 0, "") == 1;
#else
            return false;
#endif
        }

        [GMLFunction("get_string")]
        public static object? get_string(object?[] args)
        {
            var text = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());
            var def = args[1].Conv<string>();
#if BROWSER
            // Cancel (index 0) is the Escape default; OK (index 1) the Enter default. Cancel keeps
            // the default value, matching GML.
            var index = BrowserDialog.ShowDialog(text, "Cancel\u001fOK", 1, 0, 1, def);
            return index == 1 ? BrowserDialog.DialogValue() : def;
#else
            return def;
#endif
        }

        [GMLFunction("get_integer")]
        public static object? get_integer(object?[] args)
        {
            var text = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());
            var def = args[1].Conv<double>();
#if BROWSER
            var index = BrowserDialog.ShowDialog(text, "Cancel\u001fOK", 1, 0, 2, def.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (index == 1 &&
                double.TryParse(BrowserDialog.DialogValue(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
            return def;
#else
            return def;
#endif
        }

        // There's no real OS file picker available to a browser-sandboxed game, and GML filenames
        // here are just keys into the virtual save storage (see LoadSave.GetSaveFileName), not real
        // paths -- so these reuse the get_string text-prompt dialog to let the player name a save
        // slot, rather than exposing a native file chooser. Returns "" on cancel, matching GML.
        private static object? GetFilename(string fname, string title)
        {
#if BROWSER
            var index = BrowserDialog.ShowDialog(title, "CancelOK", 1, 0, 1, fname);
            return index == 1 ? BrowserDialog.DialogValue() : "";
#else
            return fname;
#endif
        }

        [GMLFunction("get_open_filename")]
        public static object? get_open_filename(object?[] args)
        {
            var fname = args[1].Conv<string>();
            return GetFilename(fname, "Open File");
        }

        [GMLFunction("get_save_filename")]
        public static object? get_save_filename(object?[] args)
        {
            var fname = args[1].Conv<string>();
            return GetFilename(fname, "Save File");
        }

        [GMLFunction("get_open_filename_ext")]
        public static object? get_open_filename_ext(object?[] args)
        {
            var fname = args[1].Conv<string>();
            var title = args[3].Conv<string>();
            return GetFilename(fname, string.IsNullOrEmpty(title) ? "Open File" : title);
        }

        [GMLFunction("get_save_filename_ext")]
        public static object? get_save_filename_ext(object?[] args)
        {
            var fname = args[1].Conv<string>();
            var title = args[3].Conv<string>();
            return GetFilename(fname, string.IsNullOrEmpty(title) ? "Save File" : title);
        }

        [GMLFunction("keyboard_key_press")]
        public static object? keyboard_key_press(object?[] args)
        {
            var key = args[0].Conv<int>();

            if (InputHandler.KeyDown[key])
            {
                return null;
            }

            InputHandler.KeyDown[key] = true;
            InputHandler.KeyPressed[key] = true;

            return null;
        }

        [GMLFunction("keyboard_key_release")]
        public static object? keyboard_key_release(object?[] args)
        {
            var key = args[0].Conv<int>();

            InputHandler.KeyDown[key] = false;
            // oddly doesn't change KeyPressed in the HTML runner

            return null;
        }

        /// <summary>
        /// Makes one key report as another: the key the player presses on the left, the key the game
        /// will see on the right. GameMaker's own way of letting a game be rebound.
        /// </summary>
        /// <remarks>
        /// Mapping a key to vk_nokey discards it, which is the other half of a rebind -- without it
        /// the key an action moved off still does it, and swapping two actions is impossible. The
        /// table is applied where input is read (see InputHandler.UpdateKeyboardState), so every way
        /// the game can ask -- keyboard_check, the keyboard events, keyboard_lastkey -- agrees.
        /// keyboard_check_direct deliberately does not: it reads the device, as it does in GameMaker.
        /// </remarks>
        [GMLFunction("keyboard_set_map")]
        public static object? keyboard_set_map(object?[] args)
        {
            var from = args[0].Conv<int>();
            var to = args[1].Conv<int>();

            if (from is < 0 or > 255 || to is < 0 or > 255)
            {
                // Out of the table rather than an error: GameMaker has no key here to remap, and a
                // game doing arithmetic on a key code should not be taken down for it.
                DebugLog.LogWarning($"keyboard_set_map({from}, {to}) - key out of range, ignoring");
                return null;
            }

            InputHandler.KeyMap[from] = (byte)to;

            return null;
        }

        [GMLFunction("keyboard_get_map")]
        public static object? keyboard_get_map(object?[] args)
        {
            var key = args[0].Conv<int>();

            if (key is < 0 or > 255)
            {
                return key;
            }

            return (int)InputHandler.KeyMap[key];
        }

        [GMLFunction("keyboard_unset_map")]
        public static object? keyboard_unset_map(object?[] args)
        {
            InputHandler.KeyMap = InputHandler.IdentityKeyMap();

            return null;
        }

        [GMLFunction("keyboard_check")]
        public static object? keyboard_check(object?[] args)
        {
            var key = args[0].Conv<int>();

            // from disassembly
            switch (key)
            {
                // vk_nokey (0) and vk_anykey (1) are purely computed at query time from whether any
                // *real* key is down (see InputHandler.CalculateKey's own case 0/1 calls) -- they are
                // not real physical keys with their own independent history, so sweeping i from 0
                // instead of 2 here would fold their own derived KeyDown[0]/KeyDown[1] values back
                // into this "is any/every real key down" check, corrupting it with self-reference
                // (confirmed against OpenGMK's keyboard_check_any_internal_indirect, which only ever
                // iterates real button-state slots, never these two pseudo-vks).
                case 0:
                    {
                        var result = true;
                        for (var i = 2; i <= 255; ++i)
                        {
                            result = InputHandler.KeyDown[i] != true && result;
                        }
                        return result;
                    }
                case 1:
                    {
                        var result = false;
                        for (var i = 2; i <= 255; ++i)
                        {
                            result = InputHandler.KeyDown[i] || result;
                        }
                        return result;
                    }
                case > 255:
                    return false;
                default:
                    return InputHandler.KeyDown[key];
            }
        }

        [GMLFunction("keyboard_check_pressed")]
        public static object? keyboard_check_pressed(object?[] args)
        {
            var key = args[0].Conv<int>();

            // from disassembly
            switch (key)
            {
                // See keyboard_check's case 0/1 comment: vk_nokey/vk_anykey aren't real keys, so this
                // sweep must skip their own derived KeyPressed[0]/KeyPressed[1] entries or it
                // self-corrupts -- concretely, releasing the last held key sets KeyPressed[0] (vk_nokey
                // "just became true"), which without this exclusion made keyboard_check_pressed(vk_anykey)
                // spuriously report true on a pure key-release frame.
                case 0:
                    {
                        var result = true;
                        for (var i = 2; i <= 255; ++i)
                        {
                            result = InputHandler.KeyPressed[i] != true && result;
                        }
                        return result;
                    }
                case 1:
                    {
                        var result = false;
                        for (var i = 2; i <= 255; ++i)
                        {
                            result = InputHandler.KeyPressed[i] || result;
                        }
                        return result;
                    }
                case > 255:
                    return false;
                default:
                    return InputHandler.KeyPressed[key];
            }
        }

        [GMLFunction("keyboard_check_released")]
        public static object? keyboard_check_released(object?[] args)
        {
            var key = args[0].Conv<int>();

            // from disassembly
            switch (key)
            {
                // See keyboard_check's case 0/1 comment -- same exclusion, same reason.
                case 0:
                    {
                        var result = true;
                        for (var i = 2; i <= 255; ++i)
                        {
                            result = InputHandler.KeyReleased[i] != true && result;
                        }
                        return result;
                    }
                case 1:
                    {
                        var result = false;
                        for (var i = 2; i <= 255; ++i)
                        {
                            result = InputHandler.KeyReleased[i] || result;
                        }
                        return result;
                    }
                case > 255:
                    return false;
                default:
                    return InputHandler.KeyReleased[key];
            }
        }

        [GMLFunction("keyboard_check_direct")]
        public static object? keyboard_check_direct(object?[] args)
        {
            var key = args[0].Conv<int>();
            return InputHandler.KeyboardCheckDirect(key);
        }

        [GMLFunction("mouse_check_button")]
        public static object mouse_check_button(object?[] args)
        {
            var numb = args[0].Conv<int>();

            return numb switch
            {
                -1 => InputHandler.MouseDown.Any(x => x),
                0 => InputHandler.MouseDown.All(x => !x),
                _ => InputHandler.MouseDown[numb - 1]
            };
        }

        [GMLFunction("mouse_check_button_pressed")]
        public static object mouse_check_button_pressed(object?[] args)
        {
            var numb = args[0].Conv<int>();
            return numb switch
            {
                -1 => InputHandler.MousePressed.Any(x => x),
                0 => InputHandler.MousePressed.All(x => !x),
                _ => InputHandler.MousePressed[numb - 1]
            };
        }

        [GMLFunction("mouse_check_button_released")]
        public static object mouse_check_button_released(object?[] args)
        {
            var numb = args[0].Conv<int>();
            return numb switch
            {
                -1 => InputHandler.MouseReleased.Any(x => x),
                0 => InputHandler.MouseReleased.All(x => !x),
                _ => InputHandler.MouseReleased[numb - 1]
            };
        }

        [GMLFunction("mouse_wheel_up")]
        public static object mouse_wheel_up(object?[] args) => InputHandler.MouseWheelUp;

        [GMLFunction("mouse_wheel_down")]
        public static object mouse_wheel_down(object?[] args) => InputHandler.MouseWheelDown;

        // keyboard_virtual_show
        // keyboard_virtual_hide
        // keyboard_virtual_status
        // keyboard_virtual_height
        // keyboard_clear
        // mouse_clear

        [GMLFunction("io_clear")]
        public static object? io_clear(object?[] args)
        {
            // TODO : clear other IO variables when we implement them

            InputHandler.KeyDown = new bool[256];
            InputHandler.KeyPressed = new bool[256];
            InputHandler.KeyReleased = new bool[256];

            InputHandler.MouseDown = new bool[5];
            InputHandler.MousePressed = new bool[5];
            InputHandler.MouseReleased = new bool[5];
            return null;
        }

        [GMLFunction("device_mouse_x_to_gui")]
        public static object? device_mouse_x_to_gui(object?[] args)
        {
            var dev = args[0].Conv<int>();

            var mouseX = InputHandler.MousePos.X;
            var windowWidth = GraphicFunctions.window_get_width([]).Conv<int>();
            var guiWidth = DrawManager.GuiSize?.X ?? windowWidth;
            return CustomMath.DoubleTilde(mouseX * (guiWidth / windowWidth));
        }

        [GMLFunction("device_mouse_y_to_gui")]
        public static object? device_mouse_y_to_gui(object?[] args)
        {
            var dev = args[0].Conv<int>();

            var mouseY = InputHandler.MousePos.Y;
            var windowHeight = GraphicFunctions.window_get_height([]).Conv<int>();
            var guiHeight = DrawManager.GuiSize?.Y ?? windowHeight;
            return CustomMath.DoubleTilde(mouseY * (guiHeight / windowHeight));
        }

        // device_mouse_dbclick_enable
        // browser_input_capture

        // gesture stuff
    }
}
