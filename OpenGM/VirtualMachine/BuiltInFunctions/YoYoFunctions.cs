using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Rendering;
using System.Globalization;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class YoYoFunctions
    {
        // ... YoYo_

        // ... virtual_key_

        // ... push_

        // ... achievement_

        // ... cloud_

        // ... YoYo_OpenURL_

        // ... url_

        // ... clickable_

        // shop_get_rating

        // ... YoYo_

        [GMLFunction("get_timer")]
        public static object get_timer(object?[] args)
        {
            return (int)(DateTime.Now - Entry.GameLoadTime).TotalMicroseconds; // TODO : is this floored? i assume it is
        }

        [GMLFunction("os_get_config")]
        public static object? os_get_config(object?[] args) => GameLoader.GeneralInfo.Config;

        // os_get_info

        [GMLFunction("os_get_language")]
        public static object os_get_language(object?[] args)
        {
            var lang = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
            return (lang == "iv") ? "en" : lang; //just in case it returns iv
        }

        [GMLFunction("os_get_region")]
        public static object os_get_region(object?[] args)
        {
            bool invariant = (CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "iv"); //just in case it returns iv
            return ((invariant) ? "GB" : (new RegionInfo(CultureInfo.CurrentCulture.Name).TwoLetterISORegionName));
        }

        // os_request_permission
        // os_check_permision

        [GMLFunction("code_is_compiled")]
        public static object code_is_compiled(object?[] args)
        {
            return GameLoader.GeneralInfo.IsYYC;
        }

        // .. display_

        // achievement_login_status

        // .. YoYo_

        [GMLFunction("device_mouse_check_button")]
        public static object? device_mouse_check_button(object?[] args)
        {
            // TODO: implement multiple devices.

            var device = args[0].Conv<int>();
            var button = args[1].Conv<int>();

            return InteractionFunctions.mouse_check_button([button]);
        }

        [GMLFunction("device_mouse_check_button_pressed")]
        public static object? device_mouse_check_button_pressed(object?[] args)
        {
            // TODO: implement multiple devices.

            var device = args[0].Conv<int>();
            var button = args[1].Conv<int>();

            return InteractionFunctions.mouse_check_button_pressed([button]);
        }

        [GMLFunction("device_mouse_check_button_released")]
        public static object? device_mouse_check_button_released(object?[] args)
        {
            // TODO: implement multiple devices.

            var device = args[0].Conv<int>();
            var button = args[1].Conv<int>();

            return InteractionFunctions.mouse_check_button_released([button]);
        }
        // device_mouse_x
        // device_mouse_y
        // device_mouse_raw_x
        // device_mouse_raw_y
        // device_mouse_x_to_gui
        // device_mouse_y_to_gui
        // device_get_tilt_x
        // device_get_tilt_y
        // device_get_tilt_z
        // device_is_keypad_open

        [GMLFunction("os_is_paused", GMLFunctionFlags.Stub)]
        public static object os_is_paused(object?[] args)
        {
            return false;
        }

        // window_has_focus
        // base64_encode/base64_decode moved to MathFunctions.cs (next to the other string_* helpers)

        // ...

        // gml_release_mode

        [GMLFunction("application_surface_draw_enable", GMLFunctionFlags.Stub, stubLogType: DebugLog.LogType.Warning)]
        public static object? application_surface_draw_enable(object?[] args)
        {
            //SurfaceManager.AppSurfaceAutodraw = args[0].Conv<bool>();

            return null;
        }

        [GMLFunction("application_get_position")]
        public static object? application_get_position(object?[] args)
        {
            var vec = SurfaceManager.FullScreenOffset();

            return new List<float>() { vec.X, vec.Y, vec.Z, vec.W };
        }

        [GMLFunction("application_surface_enable", GMLFunctionFlags.Stub)]
        public static object? application_surface_enable(object?[] args)
        {
            return null;
        }

        // application_surface_is_enabled

        [GMLFunction("extension_stubfunc_real")]
        public static object? extension_stubfunc_real(object?[] args) => 0;

        [GMLFunction("extension_stubfunc_string")]
        public static object? extension_stubfunc_string(object?[] args) => "";

        // ...

        // video_* functions live in YoYoVideoFunctions.cs -- they're the only YoYo built-ins that
        // depend on MotionTK (native FFmpeg), so they're split out to keep the rest of this file
        // usable on browser-wasm (see YoYoVideoFunctions.cs's own doc comment).

        [GMLFunction("switch_get_operation_mode", GMLFunctionFlags.Stub)]
        public static object? switch_get_operation_mode(object?[] args)
        {
            return null;
        }

        // ...
    }
}
