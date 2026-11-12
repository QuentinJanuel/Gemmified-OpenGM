using OpenGM.IO;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StbImageSharp;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class GraphicFunctions
    {
        public static double[] CircleCos = new double[65];
        public static double[] CircleSin = new double[65];

        /// <summary>
        /// Primes the tables above, which every circle and ellipse reads to place its rim vertices.
        ///
        /// They start all-zero, and a zero sin/cos puts every rim vertex exactly on the centre: the
        /// fan has no area, so nothing rasterises and the shape is simply absent -- no error, no
        /// GL complaint, just a draw call that covers no pixels. Only <see cref="Entry.Main"/>
        /// filled them (draw_set_circle_precision(24)), which is the desktop entry point; the
        /// browser one (OpenGM.Wasm's WasmEntry) never runs it, so under wasm draw_circle,
        /// draw_ellipse and their _color/_colour variants -- everything that goes through
        /// DrawEllipseFan or DrawEllipseColor -- silently drew nothing at all.
        ///
        /// That is invisible until a game leans on one. I wanna reach the Moon's caves build their
        /// darkness as a light-grey full-screen rectangle on a surface, subtract-blended over the
        /// room, with each light punched back out of it by a white-to-black draw_ellipse_color --
        /// the rectangle landed and the ellipses did not, so the whole area rendered as an evenly
        /// dimmed near-black screen that was otherwise perfectly playable.
        ///
        /// A static constructor rather than another entry-point call: it runs before any member of
        /// this class can be touched, so a third entry point cannot forget it.
        /// </summary>
        static GraphicFunctions()
        {
            draw_set_circle_precision(24);
        }

        // not in 2022.500, no idea where in the list it goes
        [GMLFunction("window_enable_borderless_fullscreen", GMLFunctionFlags.Stub)]
        public static object? window_enable_borderless_fullscreen(object?[] args)
        {
            // todo : implement
            return null;
        }

        [GMLFunction("display_get_width")]
        public static object display_get_width(object?[] args)
        {
            return Monitors.GetPrimaryMonitor().HorizontalResolution;
        }

        [GMLFunction("display_get_height")]
        public static object display_get_height(object?[] args)
        {
            return Monitors.GetPrimaryMonitor().VerticalResolution;
        }

        [GMLFunction("display_get_gui_width")]
        public static object display_get_gui_width(object?[] args)
        {
            if (DrawManager.GuiSize.HasValue)
            {
                return DrawManager.GuiSize.Value.X;
            }

            return window_get_width(args);
        }

        [GMLFunction("display_get_gui_height")]
        public static object display_get_gui_height(object?[] args)
        {
            if (DrawManager.GuiSize.HasValue)
            {
                return DrawManager.GuiSize.Value.Y;
            }

            return window_get_height(args);
        }

        [GMLFunction("display_get_dpi_x", GMLFunctionFlags.Stub)]
        public static object display_get_dpi_x(object?[] args)
        {
            return 1;
        }

        [GMLFunction("display_get_dpi_y", GMLFunctionFlags.Stub)]
        public static object display_get_dpi_y(object?[] args)
        {
            return 1;
        }

        // display_get_frequency
        // display_get_orientation

        [GMLFunction("display_reset", GMLFunctionFlags.Stub)]
        public static object? display_reset(object?[] args)
        {
            // aa_level, vsync -- no-op: the canvas has no real vsync toggle to flip (rendering is
            // always driven by requestAnimationFrame, which is inherently vsynced to the display)
            // and AA level isn't something this call controls post-context-creation. Real GM also
            // uses this to reset the window's position/size to the display default, which
            // YoYoYo_Engine's own scrSetVsync relies on (immediately overwriting it via an alarm
            // that restores window_set_position/window_set_size from window_get_x/y/width/height
            // saved just before this call) -- there's no window to reposition in a browser tab, so
            // that part is a no-op too, same as window_set_position/window_set_size elsewhere.
            return null;
        }

        [GMLFunction("display_mouse_get_x", GMLFunctionFlags.Stub)]
        public static object? display_mouse_get_x(object?[] args)
        {
            return window_mouse_get_x([]);
        }

        [GMLFunction("display_mouse_get_y", GMLFunctionFlags.Stub)]
        public static object? display_mouse_get_y(object?[] args)
        {
            return window_mouse_get_y([]);
        }

        // display_mouse_set
        // draw_enable_drawevent
        // display_set_windows_alternate_sync
        // display_set_ui_visibility
        // display_set_timing_method
        // display_get_timing_method
        // display_set_sleep_margin
        // display_get_sleep_margin

        [GMLFunction("display_set_gui_size")]
        public static object? display_set_gui_size(object?[] args)
        {
            var width = args[0].Conv<int>();
            var height = args[1].Conv<int>();

            DrawManager.GuiSize = new(width, height);

            return null;
        }

        [GMLFunction("display_set_gui_maximise")]
        [GMLFunction("display_set_gui_maximize")]
        public static object? display_set_gui_maximise(object?[] args)
        {
            // TODO: position offsets
            var scaleX = 1.0;
            var scaleY = 1.0;

            if (args.Length > 0)
            {
                scaleX = args[0].Conv<double>();
                scaleY = args[1].Conv<double>();
            }

            if (scaleX < 0 && scaleY < 0)
            {
                DrawManager.GuiSize = null;
            }
            else
            {
                var width = window_get_width([]).Conv<int>();
                var height = window_get_height([]).Conv<int>();

                DrawManager.GuiSize = new((int)(width * scaleX), (int)(height * scaleY));
            }

            return null;
        }

        // window_set_visible
        // window_get_visible

        [GMLFunction("window_set_fullscreen")]
        public static object? window_set_fullscreen(object?[] args)
        {
            var full = args[0].Conv<bool>();
#if BROWSER
            BrowserWindow.SetFullscreen(full);
#else
            CustomWindow.Instance.WindowState = full ? WindowState.Fullscreen : WindowState.Normal;
#endif
            return null;
        }

        [GMLFunction("window_get_fullscreen")]
        public static object window_get_fullscreen(object?[] args)
        {
#if BROWSER
            return BrowserWindow.GetFullscreen();
#else
            return CustomWindow.Instance.IsFullscreen;
#endif
        }

        [GMLFunction("window_set_showborder")]
        public static object? window_set_showborder(object?[] args)
        {
            var border = args[0].Conv<bool>();
            unsafe
            {
                GLFW.SetWindowAttrib(CustomWindow.Instance.WindowPtr, WindowAttribute.Decorated, border);
            }

            return null;
        }

        [GMLFunction("window_get_showborder")]
        public static object window_get_showborder(object?[] args)
        {
            unsafe
            {
                return GLFW.GetWindowAttrib(CustomWindow.Instance.WindowPtr, WindowAttributeGetBool.Decorated);
            }
        }

        // window_set_showicons
        // window_get_showicons

        [GMLFunction("window_set_stayontop")]
        public static object? window_set_stayontop(object?[] args)
        {
            var top = args[0].Conv<bool>();

            CustomWindow.Instance.AlwaysOnTop = top;

            return null;
        }

        [GMLFunction("window_get_stayontop")]
        public static object window_get_stayontop(object?[] args)
        {
            return CustomWindow.Instance.AlwaysOnTop;
        }

        [GMLFunction("window_set_sizeable")]
        public static object? window_set_sizeable(object?[] args)
        {
            var sizeable = args[0].Conv<bool>();
            unsafe
            {
                GLFW.SetWindowAttrib(CustomWindow.Instance.WindowPtr, WindowAttribute.Resizable, sizeable);
            }

            return null;
        }

        [GMLFunction("window_get_sizeable")]
        public static object window_get_sizeable(object?[] args)
        {
            unsafe
            {
                return GLFW.GetWindowAttrib(CustomWindow.Instance.WindowPtr, WindowAttributeGetBool.Resizable);
            }
        }

        [GMLFunction("window_set_caption")]
        public static object? window_set_caption(object?[] args)
        {
            var caption = args[0].Conv<string>();
            CustomWindow.Instance.Title = caption;
            return null;
        }

        [GMLFunction("window_get_caption")]
        public static object? window_get_caption(object?[] args)
        {
            return CustomWindow.Instance.Title;
        }

        [GMLFunction("window_set_cursor", GMLFunctionFlags.Stub)]
        public static object? window_set_cursor(object?[] args)
        {
            return null;
        }

        [GMLFunction("window_get_cursor", GMLFunctionFlags.Stub)]
        public static object? window_get_cursor(object?[] args)
        {
            return 0;
        }

        [GMLFunction("window_set_color", GMLFunctionFlags.Stub)]
        [GMLFunction("window_set_colour", GMLFunctionFlags.Stub)]
        public static object? window_set_color(object?[] args)
        {
            return null;
        }

        [GMLFunction("window_get_color", GMLFunctionFlags.Stub)]
        [GMLFunction("window_get_colour", GMLFunctionFlags.Stub)]
        public static object? window_get_color(object?[] args)
        {
            return null;
        }

        // window_set_min_width
        // window_set_max_width
        // window_set_min_height
        // window_set_max_height

        [GMLFunction("window_set_position")]
        public static object? window_set_position(object?[] args)
        {
            var x = args[0].Conv<int>();
            var y = args[1].Conv<int>();

            CustomWindow.Instance.ClientLocation = new Vector2i(x, y);

            return null;
        }

        [GMLFunction("window_set_size")]
        public static object? window_set_size(object?[] args)
        {
            var w = args[0].Conv<int>();
            var h = args[1].Conv<int>();

            DebugLog.Log($"window_set_size {w} {h}");

            CustomWindow.Instance.ClientSize = new Vector2i(w, h);

            return null;
        }

        [GMLFunction("window_set_rectangle")]
        public static object? window_set_rectangle(object?[] args)
        {
            var x = args[0].Conv<int>();
            var y = args[1].Conv<int>();
            var w = args[2].Conv<int>();
            var h = args[3].Conv<int>();

            window_set_position([x, y]);
            window_set_size([w, h]);
            return null;
        }

        [GMLFunction("window_center")]
        public static object? window_center(object?[] args)
        {
            CustomWindow.Instance.CenterWindow();

            return null;
        }

        // window_default

        [GMLFunction("window_get_x")]
        public static object? window_get_x(object?[] args)
        {
            return CustomWindow.Instance.ClientLocation.X;
        }

        [GMLFunction("window_get_y")]
        public static object? window_get_y(object?[] args)
        {
            return CustomWindow.Instance.ClientLocation.Y;
        }

        [GMLFunction("window_get_width")]
        public static object window_get_width(object?[] args)
        {
            return CustomWindow.Instance.ClientSize.X;
        }

        [GMLFunction("window_get_height")]
        public static object window_get_height(object?[] args)
        {
            return CustomWindow.Instance.ClientSize.Y;
        }

        [GMLFunction("window_has_focus")]
        public static object? window_has_focus(object?[] args)
        {
            return CustomWindow.Instance.IsFocused;
        }

        // TODO: check if these are right
        [GMLFunction("window_mouse_get_x")]
        public static object? window_mouse_get_x(object?[] args)
        {
            return InputHandler.MousePos.X;
        }

        [GMLFunction("window_mouse_get_y")]
        public static object? window_mouse_get_y(object?[] args)
        {
            return InputHandler.MousePos.Y;
        }

        // TODO: account for view that the mouse is currently in
        [GMLFunction("window_views_mouse_get_x")]
        public static object? window_views_mouse_get_x(object?[] args)
        {
            var mouseX = InputHandler.MousePos.X;
            var windowWidth = window_get_width([]).Conv<int>();
            var viewX = RoomManager.CurrentRoom.Views[0].ViewX;
            var viewWidth = RoomManager.CurrentRoom.Views[0].ViewW;
            return CustomMath.FloorToInt(viewX + (mouseX * (viewWidth / windowWidth)));
        }

        [GMLFunction("window_views_mouse_get_y")]
        public static object? window_views_mouse_get_y(object?[] args)
        {
            var mouseY = InputHandler.MousePos.Y;
            var windowHeight = window_get_height([]).Conv<int>();
            var viewY = RoomManager.CurrentRoom.Views[0].ViewY;
            var viewHeight = RoomManager.CurrentRoom.Views[0].ViewH;
            return CustomMath.FloorToInt(viewY + (mouseY * (viewHeight / windowHeight)));
        }

        // window_get_visible_rects

        // ...

        // draw_getpixel
        // draw_getpixel_ext

        [GMLFunction("draw_set_color")]
        [GMLFunction("draw_set_colour")]
        public static object? draw_set_colour(object?[] args)
        {
            var color = args[0].Conv<int>();
            SpriteManager.DrawColor = color;
            return null;
        }

        [GMLFunction("draw_set_alpha")]
        public static object? draw_set_alpha(object?[] args)
        {
            var alpha = args[0].Conv<double>();
            SpriteManager.DrawAlpha = alpha;
            return null;
        }

        [GMLFunction("draw_get_color")]
        [GMLFunction("draw_get_colour")]
        public static object draw_get_colour(object?[] args)
        {
            return SpriteManager.DrawColor;
        }

        [GMLFunction("draw_get_alpha")]
        public static object? draw_get_alpha(object?[] args)
        {
            return SpriteManager.DrawAlpha;
        }

        [GMLFunction("draw_set_blend_mode")]
        public static object? draw_set_blend_mode(object?[] args) => _3DFunctions.gpu_set_blendmode(args);

        [GMLFunction("draw_set_blend_mode_ext")]
        public static object? draw_set_blend_mode_ext(object?[] args) => _3DFunctions.gpu_set_blendmode_ext(args);

        [GMLFunction("draw_set_color_write_enable")]
        [GMLFunction("draw_set_colour_write_enable")]
        public static object? draw_set_colour_write_enable(object?[] args) => _3DFunctions.gpu_set_colourwriteenable(args);

        [GMLFunction("make_color_rgb")]
        [GMLFunction("make_colour_rgb")]
        public static object make_color_rgb(params object?[] args)
        {
            var r = args[0].Conv<int>();
            var g = args[1].Conv<int>();
            var b = args[2].Conv<int>();

            return r | g << 8 | b << 16;
        }

        [GMLFunction("make_color_hsv")]
        [GMLFunction("make_colour_hsv")]
        public static object make_color_hsv(params object?[] args)
        {
            var hue = args[0].Conv<double>();
            var sat = args[1].Conv<double>() / 255;
            var val = args[2].Conv<double>() / 255;

            var hueDegree = (hue / 255) * 360;

            if (hueDegree >= 360)
            {
                hueDegree -= 360;
            }

            var chroma = val * sat;

            var hPrime = hueDegree / 60;

            var x = chroma * (1 - Math.Abs((hPrime % 2) - 1));

            var r = 0.0;
            var g = 0.0;
            var b = 0.0;

            switch (hPrime)
            {
                case >= 0 and < 1:
                    r = chroma;
                    g = x;
                    b = 0;
                    break;
                case >= 1 and < 2:
                    r = x;
                    g = chroma;
                    b = 0;
                    break;
                case >= 2 and < 3:
                    r = 0;
                    g = chroma;
                    b = x;
                    break;
                case >= 3 and < 4:
                    r = 0;
                    g = x;
                    b = chroma;
                    break;
                case >= 4 and < 5:
                    r = x;
                    g = 0;
                    b = chroma;
                    break;
                case >= 5 and < 6:
                    r = chroma;
                    g = 0;
                    b = x;
                    break;
            }

            var m = val - chroma;
            r += m;
            g += m;
            b += m;

            // Rounded, not truncated -- same rule merge_colour follows, and captured the same way:
            // GMS 1.4.9999 answers make_color_hsv(100, 200, 255) = 0x7EFF37, where truncating the
            // blue channel's 125.7 gives 0x7DFF37.
            static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v * 255, MidpointRounding.AwayFromZero), 0, 255);

            var rByte = Channel(r);
            var gByte = Channel(g);
            var bByte = Channel(b);

            return (bByte << 16) + (gByte << 8) + rByte;
        }

        [GMLFunction("color_get_red")]
        [GMLFunction("colour_get_red")]
        public static object? color_get_red(object?[] args)
        {
            var col = args[0].Conv<int>();

            return col & 0xFF;
        }

        [GMLFunction("color_get_green")]
        [GMLFunction("colour_get_green")]
        public static object? color_get_green(object?[] args)
        {
            var col = args[0].Conv<int>();

            return (col >> 8) & 0xFF;
        }

        [GMLFunction("color_get_blue")]
        [GMLFunction("colour_get_blue")]
        public static object? color_get_blue(object?[] args)
        {
            var col = args[0].Conv<int>();

            return (col >> 16) & 0xFF;
        }

        private static (double h, double s, double v) RGBToHSV(int col)
        {
            var rprime = col.ABGRToCol4().R;
            var gprime = col.ABGRToCol4().G;
            var bprime = col.ABGRToCol4().B;

            var cmax = CustomMath.Max(rprime, gprime, bprime);
            var cmin = CustomMath.Min(rprime, gprime, bprime);

            var delta = cmax - cmin;

            var hprime = 0.0;
            var sprime = 0.0;
            var vprime = 0.0;

            if (delta / cmax == 0)
            {
                hprime = 0;
            }
            else
            {
                if (cmax == rprime)
                {
                    hprime = 60 * (gprime - bprime) / delta;
                }
                else if (cmax == gprime)
                {
                    hprime = (60 * (bprime - rprime) / delta) + 120;
                }
                else if (cmax == bprime)
                {
                    hprime = (60 * (rprime - gprime) / delta) + 240;
                }

                if (hprime < 0)
                {
                    hprime += 360;
                }
            }

            if (cmax == 0)
            {
                sprime = 0;
                hprime = 0;
            }
            else
            {
                sprime = delta / cmax;
            }

            vprime = cmax;

            var h = Math.Min(255, Math.Max(0, hprime * 255 / 360));
            var v = Math.Min(255, Math.Max(0, vprime * 255));
            var s = Math.Min(255, Math.Max(0, sprime * 255));

            return (h, s, v);
        }

        [GMLFunction("color_get_hue")]
        [GMLFunction("colour_get_hue")]
        public static object? color_get_hue(object?[] args)
        {
            var col = args[0].Conv<int>();
            return RGBToHSV(col).h;
        }

        [GMLFunction("color_get_saturation")]
        [GMLFunction("colour_get_saturation")]
        public static object? color_get_saturation(object?[] args)
        {
            var col = args[0].Conv<int>();
            return RGBToHSV(col).s;
        }

        [GMLFunction("color_get_value")]
        [GMLFunction("colour_get_value")]
        public static object? color_get_value(object?[] args)
        {
            var col = args[0].Conv<int>();
            return RGBToHSV(col).v;
        }

        [GMLFunction("merge_color")]
        [GMLFunction("merge_colour")]
        public static object merge_colour(params object?[] args)
        {
            var col1 = args[0].Conv<int>();
            var col2 = args[1].Conv<int>();
            var amount = args[2].Conv<double>();

            /*
             * GameMaker stores colors in 3 bytes - BGR
             * RED        : 255        : 00 00 FF
             * ORANGE    : 4235519    : 40 A0 FF
             * Alpha (or "blend") is not stored in colors.
             */

            var oneBytes = BitConverter.GetBytes(col1);
            var twoBytes = BitConverter.GetBytes(col2);
            amount = Math.Clamp(amount, 0, 1);
            var mr = oneBytes[0] + (twoBytes[0] - oneBytes[0]) * amount;
            var mg = oneBytes[1] + (twoBytes[1] - oneBytes[1]) * amount;
            var mb = oneBytes[2] + (twoBytes[2] - oneBytes[2]) * amount;

            // Round each channel rather than truncating it. Captured from GMS 1.4.9999:
            // merge_color(c_red, c_blue, 0.5) is 0x800080, i.e. the exact half of 0..255 lands on
            // 128. Truncating gave 127 on both channels -- a whole level darker on every blend, and
            // biased consistently one way, which is what makes it visible across a gradient.
            static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), 0, 255);

            return BitConverter.ToInt32([Channel(mr), Channel(mg), Channel(mb), 0], 0);
        }

        [GMLFunction("draw_clear")]
        public static object? draw_clear(object?[] args)
        {
            // A clear wipes whatever the queue was going to draw onto, so it has to go out first.
            GraphicsManager.FlushPolygonBatch();
            var col = args[0].Conv<int>();
            var colour = col.ABGRToCol4(1);
            GL.ClearColor(colour);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.ClearColor(0, 0, 0, 0);
            return null;
        }

        [GMLFunction("draw_clear_alpha")]
        public static object? draw_clear_alpha(object?[] args)
        {
            // A clear wipes whatever the queue was going to draw onto, so it has to go out first.
            GraphicsManager.FlushPolygonBatch();
            var col = args[0].Conv<int>();
            var alpha = args[1].Conv<double>();

            GL.ClearColor(col.ABGRToCol4(alpha));
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.ClearColor(0, 0, 0, 0);
            return null;
        }

        [GMLFunction("draw_point")]
        public static object? draw_point(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();

            draw_point_color([x, y, SpriteManager.DrawColor]);
            return null;
        }

        [GMLFunction("draw_line")]
        public static object? draw_line(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();

            var col = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);

            CustomWindow.Draw(new GMLineJob()
            {
                col1 = col,
                col2 = col,
                width = 1,
                x1 = (float)x1,
                y1 = (float)y1,
                x2 = (float)x2,
                y2 = (float)y2
            });

            return null;
        }

        [GMLFunction("draw_line_width")]
        public static object? draw_line_width(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var w = args[4].Conv<int>();

            CustomWindow.Draw(new GMLineJob()
            {
                col1 = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha),
                col2 = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha),
                x1 = (float)x1,
                y1 = (float)y1,
                x2 = (float)x2,
                y2 = (float)y2,
                width = w
            });

            return null;
        }

        [GMLFunction("draw_rectangle")]
        public static object? draw_rectangle(params object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var outline = args[4].Conv<bool>();

            x2 += 1;
            y2 += 1;

            var c = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);

            CustomWindow.Draw(new GMPolygonJob()
            {
                Colors = [c, c, c, c],
                Vertices =
                [
                    new(x1, y1, GraphicsManager.GR_Depth),
                    new(x2, y1, GraphicsManager.GR_Depth),
                    new(x2, y2, GraphicsManager.GR_Depth),
                    new(x1, y2, GraphicsManager.GR_Depth)
                ],
                Outline = outline
            });
            return null;
        }

        [GMLFunction("draw_triangle")]
        public static object? draw_triangle(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var x3 = args[4].Conv<double>();
            var y3 = args[5].Conv<double>();
            var outline = args[6].Conv<bool>();

            var c = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);

            CustomWindow.Draw(new GMPolygonJob()
            {
                Colors = [c, c, c],
                Vertices =
                [
                    new(x1, y1, GraphicsManager.GR_Depth),
                    new(x2, y2, GraphicsManager.GR_Depth),
                    new(x3, y3, GraphicsManager.GR_Depth)
                ],
                Outline = outline
            });

            return null;
        }

        /// <summary>
        /// Emits a filled/outlined ellipse fan without allocating a job or a pair of arrays per
        /// call, and using the precomputed CircleSin/CircleCos tables rather than 2 trig calls per
        /// vertex. Both matter here because the idiomatic GML radial gradient is ~50 concentric
        /// draw_circle calls PER LIGHT PER FRAME (see GraphicsManager.BatchConvexPolygon).
        ///
        /// The tables also fix a rounding bug the old inline maths had: it stepped by an integer
        /// `360 / precision` degrees, so any precision that doesn't divide 360 (e.g. 28) swept less
        /// than a full turn and left the shape visibly open.
        /// </summary>
        /// <summary>
        /// <paramref name="centreCol"/>/<paramref name="edgeCol"/> are -1 for "use the current draw
        /// colour". When they differ the fan gets a centre vertex so the gradient has something to
        /// interpolate from -- a rim-only fan would be flat.
        /// </summary>
        private static void DrawEllipseFan(double midX, double midY, double radiusX, double radiusY,
            bool outline, int centreCol = -1, int edgeCol = -1)
        {
            var precision = DrawManager.CirclePrecision;
            var alpha = SpriteManager.DrawAlpha;
            var c = (centreCol < 0 ? SpriteManager.DrawColor : centreCol).ABGRToCol4(alpha);
            var rim = (edgeCol < 0 ? SpriteManager.DrawColor : edgeCol).ABGRToCol4(alpha);
            var depth = GraphicsManager.GR_Depth;

            if (!outline && centreCol >= 0 && !c.Equals(rim))
            {
                Span<GraphicsManager.Vertex> gradient = stackalloc GraphicsManager.Vertex[precision + 2];
                gradient[0] = new GraphicsManager.Vertex(new Vector3d(midX, midY, depth), c, Vector2d.Zero);
                for (var i = 0; i < precision; i++)
                {
                    gradient[i + 1] = new GraphicsManager.Vertex(
                        new Vector3d(midX + (radiusX * CircleSin[i]), midY + (radiusY * CircleCos[i]), depth),
                        rim, Vector2d.Zero);
                }
                gradient[^1] = gradient[1];
                GraphicsManager.BatchConvexPolygon(gradient);
                return;
            }

            Span<GraphicsManager.Vertex> fan = stackalloc GraphicsManager.Vertex[precision];
            for (var i = 0; i < precision; i++)
            {
                // sin on x / cos on y, keeping the vertex order the inline maths produced.
                fan[i] = new GraphicsManager.Vertex(
                    new Vector3d(midX + (radiusX * CircleSin[i]), midY + (radiusY * CircleCos[i]), depth),
                    outline ? rim : c,
                    Vector2d.Zero);
            }

            if (outline)
            {
                GraphicsManager.BindTexture0(GraphicsManager.DefaultTexture);
                GraphicsManager.Draw(PrimitiveType.LineLoop, fan);
                return;
            }

            GraphicsManager.BatchConvexPolygon(fan);
        }

        [GMLFunction("draw_circle")]
        public static object? draw_circle(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var r = args[2].Conv<double>();
            var outline = args[3].Conv<bool>();

            DrawEllipseFan(x, y, r, r, outline);
            return null;
        }

        [GMLFunction("draw_ellipse")]
        public static object? draw_ellipse(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var outline = args[4].Conv<bool>();

            DrawEllipseFan((x1 + x2) / 2, (y1 + y2) / 2, Math.Abs((x1 - x2) / 2), Math.Abs((y1 - y2) / 2), outline);
            return null;
        }

        /// <summary>
        /// The corner radius GameMaker uses for draw_roundrect. Measured on GMS 1.4.9999 by
        /// rendering to a surface and reading back the first filled pixel of each row: the profile
        /// is <c>-1,4,2,2,1,1,1,...</c> and it is <b>identical at 10x10, 40x40, 64x32 and 96x96</b>,
        /// so the radius is a fixed number of pixels rather than a fraction of the rectangle.
        /// (It is clamped below for rectangles smaller than the radius itself.)
        /// </summary>
        private const double RoundRectRadius = 4;

        [GMLFunction("draw_roundrect")]
        public static object? draw_roundrect(object?[] args)
            => DrawRoundRect(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
                args[3].Conv<double>(), RoundRectRadius, RoundRectRadius, args[4].Conv<bool>(),
                SpriteManager.DrawColor, SpriteManager.DrawColor);

        /// <summary>
        /// The _ext variants take the corner radii per axis instead of using <see cref="RoundRectRadius"/>,
        /// so the corners can be elliptical. Note the argument order: the radii sit between the
        /// rectangle and the colours, and <c>outline</c> is last -- as it is for every draw_*_colour.
        /// </summary>
        [GMLFunction("draw_roundrect_ext")]
        public static object? draw_roundrect_ext(object?[] args)
            => DrawRoundRect(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
                args[3].Conv<double>(), args[4].Conv<double>(), args[5].Conv<double>(), args[6].Conv<bool>(),
                SpriteManager.DrawColor, SpriteManager.DrawColor);

        [GMLFunction("draw_roundrect_color")]
        [GMLFunction("draw_roundrect_colour")]
        public static object? draw_roundrect_color(object?[] args)
            => DrawRoundRect(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
                args[3].Conv<double>(), RoundRectRadius, RoundRectRadius, args[6].Conv<bool>(),
                args[4].Conv<int>(), args[5].Conv<int>());

        [GMLFunction("draw_roundrect_color_ext")]
        [GMLFunction("draw_roundrect_colour_ext")]
        public static object? draw_roundrect_color_ext(object?[] args)
            => DrawRoundRect(args[0].Conv<double>(), args[1].Conv<double>(), args[2].Conv<double>(),
                args[3].Conv<double>(), args[4].Conv<double>(), args[5].Conv<double>(), args[8].Conv<bool>(),
                args[6].Conv<int>(), args[7].Conv<int>());

        private static object? DrawRoundRect(double x1, double y1, double x2, double y2,
            double radiusX, double radiusY, bool outline, int col1, int col2)
        {
            if (x1 > x2)
                (x1, x2) = (x2, x1);
            if (y1 > y2)
                (y1, y2) = (y2, y1);

            // A rectangle narrower or shorter than two radii has no straight section left on that
            // axis, so the radius collapses to half the side rather than the corners overlapping.
            radiusX = Math.Min(radiusX, (x2 - x1) / 2);
            radiusY = Math.Min(radiusY, (y2 - y1) / 2);
            if (radiusX <= 0 || radiusY <= 0)
            {
                return draw_rectangle([x1, y1, x2, y2, outline]);
            }

            // Walk the outline as one closed loop: four quarter-circle corners joined by the
            // straight edges between them. Built once and used for both the outline and the fill so
            // the two can never disagree about where the shape is.
            var points = new List<Vector2d>();
            var precision = Math.Max(4, (int)Math.Ceiling(Math.Max(radiusX, radiusY)));

            void Corner(double cx, double cy, double startDegrees)
            {
                for (var i = 0; i <= precision; i++)
                {
                    var angle = (startDegrees + (90.0 * i / precision)) * Math.PI / 180.0;
                    points.Add(new Vector2d(cx + (Math.Cos(angle) * radiusX), cy - (Math.Sin(angle) * radiusY)));
                }
            }

            Corner(x2 - radiusX, y2 - radiusY, 270); // bottom-right
            Corner(x2 - radiusX, y1 + radiusY, 0);   // top-right
            Corner(x1 + radiusX, y1 + radiusY, 90);  // top-left
            Corner(x1 + radiusX, y2 - radiusY, 180); // bottom-left

            var c1 = col1.ABGRToCol4(SpriteManager.DrawAlpha);
            var c2 = col2.ABGRToCol4(SpriteManager.DrawAlpha);

            if (outline)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Count];
                    CustomWindow.Draw(new GMLineJob
                    {
                        col1 = c1,
                        col2 = c1,
                        width = 1,
                        x1 = (float)a.X,
                        y1 = (float)a.Y,
                        x2 = (float)b.X,
                        y2 = (float)b.Y,
                    });
                }

                return null;
            }

            // Filled: a fan from the centre, which is convex so it batches like draw_circle does --
            // one draw call however many segments the corners take. col1 is the centre colour and
            // col2 the edge, matching draw_rectangle_color's gradient.
            var fan = new GraphicsManager.Vertex[points.Count + 2];
            fan[0] = new GraphicsManager.Vertex(
                new Vector3d((x1 + x2) / 2, (y1 + y2) / 2, 0), c1, Vector2d.Zero);
            for (var i = 0; i < points.Count; i++)
            {
                fan[i + 1] = new GraphicsManager.Vertex(
                    new Vector3d(points[i].X, points[i].Y, 0), c2, Vector2d.Zero);
            }
            // Close the fan back onto its first edge point.
            fan[^1] = new GraphicsManager.Vertex(
                new Vector3d(points[0].X, points[0].Y, 0), c2, Vector2d.Zero);

            GraphicsManager.BatchConvexPolygon(fan);
            return null;
        }

        [GMLFunction("draw_arrow")]
        public static object? draw_arrow(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var size = args[4].Conv<double>();

            // todo : name all these variables better and refactor this

            var height = y2 - y1;
            var length = x2 - x1;

            var magnitude = Math.Sqrt((height * height) + (length * length));

            if (magnitude != 0)
            {
                // draw body of arrow

                var col = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);

                CustomWindow.Draw(new GMLineJob()
                {
                    col1 = col,
                    col2 = col,
                    width = 1,
                    x1 = (float)x1,
                    y1 = (float)y1,
                    x2 = (float)x2,
                    y2 = (float)y2
                });

                // draw head of arrow

                var headSize = magnitude;
                if (size <= magnitude)
                {
                    headSize = size;
                }

                var headLength = (length * headSize) / magnitude;
                var headHeight = (height * headSize) / magnitude;

                var a = (x2 - headLength) - headHeight / 3;
                var b = (headLength / 3) + (y2 - headHeight);

                var c = (headHeight / 3) + (x2 - headLength);
                var d = (y2 - headHeight) - headLength / 3;

                CustomWindow.Draw(new GMPolygonJob()
                {
                    Colors = [col, col, col],
                    Vertices = [new(x2, y2, GraphicsManager.GR_Depth), new(a, b, GraphicsManager.GR_Depth), new(c, d, GraphicsManager.GR_Depth)],
                    Outline = false
                });
            }

            return null;
        }

        // draw_button

        [GMLFunction("draw_healthbar")]
        public static object? draw_healthbar(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var amount = args[4].Conv<double>();
            var backcol = args[5].Conv<int>();
            var mincol = args[6].Conv<int>();
            var maxcol = args[7].Conv<int>();
            var direction = args[8].Conv<int>();
            var showback = args[9].Conv<bool>();
            var showborder = args[10].Conv<bool>();

            var midcol = merge_colour(new object[] { mincol, maxcol, 0.5 });

            if (showback)
            {
                draw_rectangle_colour(new object?[] { x1, y1, x2, y2, backcol, backcol, backcol, backcol, false });

                if (showborder)
                {
                    draw_rectangle_colour(new object?[] { x1, y1, x2, y2, 0, 0, 0, 0, true });
                }
            }

            amount = Math.Clamp(amount, 0, 100);

            var fraction = amount / 100;

            double barx1, bary1, barx2, bary2;

            // Only 1/2/3 are recognised directions -- everything else falls back to left-to-right,
            // matching the runner's own `switch` default (OpenGMK's kernel.rs does the same). Games
            // do pass junk here: Spook Jam's oDB_GunSpike/objFrickWitchSpike ask for 180, reading
            // the argument as degrees. Without the fallback those calls left the bar rect at
            // (0,0)-(0,0), so only the backdrop drew and the bar looked permanently full.
            switch (direction)
            {
                case 1:
                    barx1 = x2 - fraction * (x2 - x1);
                    bary1 = y1;
                    barx2 = x2;
                    bary2 = y2;
                    break;
                case 2:
                    barx1 = x1;
                    bary1 = y1;
                    barx2 = x2;
                    bary2 = y1 + fraction * (y2 - y1);
                    break;
                case 3:
                    barx1 = x1;
                    bary1 = y2 - fraction * (y2 - y1);
                    barx2 = x2;
                    bary2 = y2;
                    break;
                default:
                    barx1 = x1;
                    bary1 = y1;
                    barx2 = x1 + fraction * (x2 - x1);
                    bary2 = y2;
                    break;
            }

            var col = 0;
            if (amount > 50)
            {
                col = merge_colour(new object[] { midcol, maxcol, (amount - 50) / 50 }).Conv<int>();
            }
            else
            {
                col = merge_colour(new object[] { mincol, midcol, amount / 50 }).Conv<int>();
            }

            draw_rectangle_colour(new object?[] { barx1, bary1, barx2, bary2, col, col, col, col, false });
            if (showborder)
            {
                draw_rectangle_colour(new object?[] { x1, y1, x2, y2, 0, 0, 0, 0, true });
            }

            return null;
        }

        [GMLFunction("draw_path")]
        public static object? draw_path(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<float>();
            var y = args[2].Conv<float>();
            var absolute = args[3].Conv<bool>();

            var path = PathManager.Paths[id];

            if (absolute)
            {
                PathManager.DrawPath(path, 0, 0, absolute);
            }
            else
            {
                PathManager.DrawPath(path, x, y, absolute);
            }

            return null;
        }

        [GMLFunction("draw_point_color")]
        [GMLFunction("draw_point_colour")]
        public static object? draw_point_color(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var c = args[2].Conv<int>();

            draw_rectangle_colour([x, y, x, y, c, c, c, c, false]);
            return null;
        }

        [GMLFunction("draw_line_color")]
        [GMLFunction("draw_line_colour")]
        public static object? draw_line_color(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var col1 = args[4].Conv<int>();
            var col2 = args[5].Conv<int>();

            CustomWindow.Draw(new GMLineJob()
            {
                col1 = col1.ABGRToCol4(SpriteManager.DrawAlpha),
                col2 = col2.ABGRToCol4(SpriteManager.DrawAlpha),
                width = 1,
                x1 = (float)x1,
                y1 = (float)y1,
                x2 = (float)x2,
                y2 = (float)y2
            });

            return null;
        }

        [GMLFunction("draw_line_width_color")]
        [GMLFunction("draw_line_width_colour")]
        public static object? draw_line_width_color(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var width = args[4].Conv<double>();
            var col1 = args[5].Conv<int>();
            var col2 = args[6].Conv<int>();

            CustomWindow.Draw(new GMLineJob()
            {
                x1 = (float)x1,
                y1 = (float)y1,
                x2 = (float)x2,
                y2 = (float)y2,
                width = (float)width,
                col1 = col1.ABGRToCol4(SpriteManager.DrawAlpha),
                col2 = col2.ABGRToCol4(SpriteManager.DrawAlpha)
            });

            return null;
        }

        [GMLFunction("draw_rectangle_color")]
        [GMLFunction("draw_rectangle_colour")]
        public static object? draw_rectangle_colour(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var col1 = args[4].Conv<int>();
            var col2 = args[5].Conv<int>();
            var col3 = args[6].Conv<int>();
            var col4 = args[7].Conv<int>();
            var outline = args[8].Conv<bool>();

            x2 += 1;
            y2 += 1;

            CustomWindow.Draw(new GMPolygonJob()
            {
                Outline = outline,
                Vertices = new[]
                {
                    new Vector3d(x1, y1, GraphicsManager.GR_Depth),
                    new Vector3d(x2, y1, GraphicsManager.GR_Depth),
                    new Vector3d(x2, y2, GraphicsManager.GR_Depth),
                    new Vector3d(x1, y2, GraphicsManager.GR_Depth)
                },
                Colors = new[]
                {
                    col1.ABGRToCol4(SpriteManager.DrawAlpha),
                    col2.ABGRToCol4(SpriteManager.DrawAlpha),
                    col3.ABGRToCol4(SpriteManager.DrawAlpha),
                    col4.ABGRToCol4(SpriteManager.DrawAlpha)
                }
            });

            return null;
        }

        [GMLFunction("draw_triangle_color")]
        [GMLFunction("draw_triangle_colour")]
        public static object? draw_triangle_color(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var x3 = args[4].Conv<double>();
            var y3 = args[5].Conv<double>();
            var col1 = args[6].Conv<int>();
            var col2 = args[7].Conv<int>();
            var col3 = args[8].Conv<int>();
            var outline = args[9].Conv<bool>();

            CustomWindow.Draw(new GMPolygonJob()
            {
                Outline = outline,
                Vertices = new[]
                {
                    new Vector3d(x1, y1, GraphicsManager.GR_Depth),
                    new Vector3d(x2, y2, GraphicsManager.GR_Depth),
                    new Vector3d(x3, y3, GraphicsManager.GR_Depth)
                },
                Colors = new[] {
                    col1.ABGRToCol4(SpriteManager.DrawAlpha),
                    col2.ABGRToCol4(SpriteManager.DrawAlpha),
                    col3.ABGRToCol4(SpriteManager.DrawAlpha) }
            });

            return null;
        }

        [GMLFunction("draw_circle_color")]
        [GMLFunction("draw_circle_colour")]
        public static object? draw_circle_color(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var r = args[2].Conv<double>();
            var col1 = args[3].Conv<int>();
            var col2 = args[4].Conv<int>();
            var outline = args[5].Conv<bool>();

            DrawEllipseColor(x - r, y - r, x + r, y + r, col1.ABGRToCol4(SpriteManager.DrawAlpha), col2.ABGRToCol4(SpriteManager.DrawAlpha), outline);

            return null;
        }

        private static void DrawEllipseColor(double x1, double y1, double x2, double y2, Color4 col1, Color4 col2, bool outline)
        {
            // funky scale fixes
            x1 += 1;
            y1 += 1;
            x2 += 1;
            y2 += 1;

            var xm = (x1 + x2) / 2;
            var ym = (y1 + y2) / 2;
            var rx = Math.Abs((x1 - x2) / 2);
            var ry = Math.Abs((y1 - y2) / 2);

            if (outline)
            {
                // only need to draw a normal ellipse outline, no color gradient needed
                var points = new Vector3d[DrawManager.CirclePrecision];
                var colors = new Color4[DrawManager.CirclePrecision];
                for (var i = 0; i < DrawManager.CirclePrecision; i++)
                {
                    points[i] = new Vector3d(
                        xm + (rx * CircleCos[i]),
                        ym + (ry * CircleSin[i]),
                        GraphicsManager.GR_Depth);
                    colors[i] = col2;
                }

                CustomWindow.Draw(new GMPolygonJob()
                {
                    Colors = colors,
                    Vertices = points,
                    Outline = true
                });
            }
            else
            {
                Span<GraphicsManager.Vertex> verts = stackalloc GraphicsManager.Vertex[DrawManager.CirclePrecision * 3];

                for (var i = 0; i < DrawManager.CirclePrecision; i++)
                {
                    var p1 = new Vector3d(xm, ym, GraphicsManager.GR_Depth);
                    var p2 = new Vector3d(
                        xm + (rx * CircleCos[i]),
                        ym + (ry * CircleSin[i]),
                        GraphicsManager.GR_Depth);
                    var p3 = new Vector3d(
                        xm + (rx * CircleCos[i + 1]),
                        ym + (ry * CircleSin[i + 1]),
                        GraphicsManager.GR_Depth);

                    verts[i * 3] = new GraphicsManager.Vertex(p1, col1, Vector2d.Zero);
                    verts[(i * 3) + 1] = new GraphicsManager.Vertex(p2, col2, Vector2d.Zero);
                    verts[(i * 3) + 2] = new GraphicsManager.Vertex(p3, col2, Vector2d.Zero);
                }

                GraphicsManager.Draw(PrimitiveType.Triangles, verts);
            }
        }

        // draw_ellipse_color
        // draw_ellipse_colour
        // draw_get_circle_precision

        [GMLFunction("draw_set_circle_precision")]
        public static object? draw_set_circle_precision(params object?[] args)
        {
            var precision = args[0].Conv<int>();

            if (precision < 4)
            {
                precision = 4;
            }

            if (precision >= 65)
            {
                precision = 64;
            }

            // ensure is a multiple of 4
            precision = 4 * (int)Math.Truncate(precision / 4.0);

            DrawManager.CirclePrecision = precision;

            CircleCos[0] = 1;
            CircleSin[0] = 0;
            for (var i = 1; i < precision; i++)
            {
                CircleCos[i] = Math.Cos(i * 2 * Math.PI / precision);
                CircleSin[i] = Math.Sin(i * 2 * Math.PI / precision);
            }
            CircleCos[precision] = 1;
            CircleSin[precision] = 0;

            return null;
        }

        public static PrimitiveType PrimType;
        public static List<GraphicsManager.Vertex> Vertices = new();

        [GMLFunction("draw_primitive_begin")]
        public static object? draw_primitive_begin(object?[] args)
        {
            var kind = args[0].Conv<int>();

            // cant just convert straight to PrimitiveType bc LineLoop isnt used grr
            PrimType = GraphicsManager.ToPrimitiveType(kind);

            /* i think on c++ you could use draw_primitive_begin multiple times, and since it only
             * sets g_NumPrims to 0, it should leave the newer vertices intact while overwritting older ones
             * on html it just creates a new vbuffer so that doesnt happen
             */

            Vertices = new();
            PrimTexture = null;

            return null;
        }

        /// <summary>
        /// The texture draw_vertex_texture's uv coordinates are relative to, for the primitive
        /// currently being built -- null for an untextured one.
        /// </summary>
        public static SpritePageItem? PrimTexture;

        [GMLFunction("draw_primitive_begin_texture")]
        public static object? draw_primitive_begin_texture(object?[] args)
        {
            var kind = args[0].Conv<int>();

            draw_primitive_begin([kind]); // clears PrimTexture, so set it after
            PrimTexture = args[1] as SpritePageItem;

            return null;
        }

        /// <summary>
        /// Maps GML's 0..1-across-the-texture uv onto the atlas page the texture actually lives on:
        /// sprites and backgrounds are packed into shared pages, so a raw (0,0)-(1,1) would sample
        /// the whole page -- every other sprite on it included -- instead of just this one image.
        /// </summary>
        /// <summary>Exposed for the d3d_vertex_texture_* family, which shares this pipeline.</summary>
        public static Vector2d PrimitiveUVPublic(double xtex, double ytex) => PrimitiveUV(xtex, ytex);

        private static Vector2d PrimitiveUV(double xtex, double ytex)
        {
            if (PrimTexture == null || !PageManager.TexturePages.TryGetValue(PrimTexture.Page, out var page))
            {
                return new(xtex, ytex);
            }

            return new(
                (PrimTexture.SourceX + (xtex * PrimTexture.SourceWidth)) / page.image.Width,
                (PrimTexture.SourceY + (ytex * PrimTexture.SourceHeight)) / page.image.Height);
        }

        [GMLFunction("draw_primitive_end")]
        public static object? draw_primitive_end(object?[] args)
        {
            // draw_primitive_begin's primitives are untextured (draw_vertex hardcodes uv 0,0), and the
            // fragment shader multiplies the sampled texel into the output. Without binding the 1x1 white
            // texture the primitive samples whatever texture the previous, unrelated draw left bound -- a
            // transparent texel there silently multiplies the whole primitive away. Every other untextured
            // path (lines, polygons) already binds it.
            // A draw_primitive_begin_texture primitive binds that texture's own page instead.
            if (PrimTexture != null && PageManager.TexturePages.TryGetValue(PrimTexture.Page, out var page))
            {
                GraphicsManager.BindTexture0(page.id);
            }
            else
            {
                GraphicsManager.BindTexture0(GraphicsManager.DefaultTexture);
            }

            GraphicsManager.Draw(PrimType, Vertices.ToArray());
            return null;
        }

        [GMLFunction("draw_vertex")]
        public static object? draw_vertex(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();

            // TODO : C++ imposes a limit of 1001 vertices, should we do the same?

            Vertices.Add(new(new(x, y, GraphicsManager.GR_Depth), SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha), new(0, 0)));
            return null;
        }

        [GMLFunction("draw_vertex_color")]
        [GMLFunction("draw_vertex_colour")]
        public static object? draw_vertex_colour(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var col = args[2].Conv<int>();
            var alpha = args[3].Conv<double>();

            // TODO : C++ imposes a limit of 1001 vertices, should we do the same?

            Vertices.Add(new(new(x, y, GraphicsManager.GR_Depth), col.ABGRToCol4(alpha), new(0, 0)));
            return null;
        }

        [GMLFunction("draw_vertex_texture")]
        public static object? draw_vertex_texture(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var xtex = args[2].Conv<double>();
            var ytex = args[3].Conv<double>();

            Vertices.Add(new(new(x, y, GraphicsManager.GR_Depth),
                SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha),
                PrimitiveUV(xtex, ytex)));
            return null;
        }

        [GMLFunction("draw_vertex_texture_color")]
        [GMLFunction("draw_vertex_texture_colour")]
        public static object? draw_vertex_texture_colour(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var xtex = args[2].Conv<double>();
            var ytex = args[3].Conv<double>();
            var col = args[4].Conv<int>();
            var alpha = args[5].Conv<double>();

            Vertices.Add(new(new(x, y, GraphicsManager.GR_Depth),
                col.ABGRToCol4(alpha),
                PrimitiveUV(xtex, ytex)));
            return null;
        }

        [GMLFunction("sprite_get_uvs")]
        public static object? sprite_get_uvs(object?[] args)
        {
            var spr = args[0].Conv<int>();
            // args[1], not args[0]: reading the sprite index as the sub-image asked for frame 118 of
            // sprite 118, and GetSpritePageItem takes that modulo the frame count rather than
            // failing, so a multi-frame sprite quietly answered with some other frame's rect.
            var subimg = args[1].Conv<int>();

            if (!SpriteManager.SpriteExists(spr))
            {
                return null;
            }

            var sprite = SpriteManager.GetSpriteAsset(spr);
            var pageItem = SpriteManager.GetSpritePageItem(spr, subimg);

            var (image, id) = PageManager.TexturePages[pageItem.Page];

            return new double[8]
            {
                // TODO : SourceWidth/Height or TargetWidth/Height?
                pageItem.SourceX / (double)image.Width,
                pageItem.SourceY / (double)image.Height,
                (pageItem.SourceX + pageItem.SourceWidth) / (double)image.Width,
                (pageItem.SourceY + pageItem.SourceHeight) / (double)image.Height,
                pageItem.TargetX,
                pageItem.TargetY,
                pageItem.TargetWidth / (double)pageItem.BoundingWidth,
                pageItem.TargetHeight / (double)pageItem.BoundingHeight
            };
        }

        // font_get_uvs

        [GMLFunction("font_get_size")]
        public static object font_get_size(object?[] args)
        {
            var fontIndex = args[0].Conv<int>();
            var font = TextManager.FontAssets.ElementAtOrDefault(fontIndex);

            return font is null ? -1 : (double)font.Size;
        }

        [GMLFunction("font_get_name")]
        public static object font_get_name(object?[] args)
        {
            var fontIndex = args[0].Conv<int>();
            var font = TextManager.FontAssets.ElementAtOrDefault(fontIndex);

            return font?.name ?? "";
        }

        [GMLFunction("font_get_info")]
        public static object? font_get_info(object?[] args)
        {
            var fontIndex = args[0].Conv<int>();
            var font = TextManager.FontAssets.ElementAtOrDefault(fontIndex);

            if (font is null)
            {
                return null;
            }

            // TODO: make these properties actually correct
            var result = new GMLObject
            {
                ["ascender"] = 0,
                ["ascenderOffset"] = 0,
                ["size"] = font.Size,
                ["spriteIndex"] = font.spriteIndex,
                ["texture"] = font.texture,
                ["name"] = font.name,
                ["bold"] = false,
                ["italic"] = false
            };

            var glyphs = new GMLObject();

            foreach (var (glyph, info) in font.entriesDict)
            {
                var gmlGlyph = new GMLObject
                {
                    ["char"] = info.characterIndex,
                    ["x"] = info.x,
                    ["y"] = info.y,
                    ["w"] = info.w,
                    ["h"] = info.h,
                    ["shift"] = info.shift,
                    ["offset"] = info.xOffset,
                    ["kerning"] = new List<object?>()
                };

                var character = ((char)glyph).ToString();
                glyphs[character] = gmlGlyph;
            }

            result["glyphs"] = glyphs;

            return result;
        }

        // font_cache_glyph

        [GMLFunction("sprite_get_texture")]
        public static object? sprite_get_texture(object?[] args)
        {
            var spr = args[0].Conv<int>();
            // args[1], not args[0] -- same copy-paste as sprite_get_uvs had.
            var subimg = args[1].Conv<int>();

            if (!SpriteManager.SpriteExists(spr))
            {
                return null;
            }

            var sprite = SpriteManager.GetSpriteAsset(spr);
            return SpriteManager.GetSpritePageItem(spr, subimg);
        }

        [GMLFunction("sprite_get_info")]
        public static object? sprite_get_info(object?[] args)
        {
            var index = args[0].Conv<int>();

            var sprite = SpriteManager.GetSpriteAsset(index);

            if (sprite == null)
            {
                return null;
            }

            var struc = new GMLObject();
            struc["width"] = sprite.Width;
            struc["height"] = sprite.Height;
            struc["xoffset"] = sprite.OriginX;
            struc["yoffset"] = sprite.OriginY;
            // transparent
            // smooth
            // type
            struc["bbox_left"] = sprite.MarginLeft;
            struc["bbox_top"] = sprite.MarginTop;
            struc["bbox_right"] = sprite.MarginRight;
            struc["bbox_bottom"] = sprite.MarginBottom;
            struc["name"] = sprite.Name;
            struc["num_subimages"] = sprite.Textures.Count;
            // use_mask
            // rotated_bounds
            // nineslice
            // messages
            // frame_info
            // frame_speed
            // frame_type

            var frames = new GMLObject[sprite.Textures.Count];
            for (var i = 0; i < sprite.Textures.Count; i++)
            {
                var tex = sprite.Textures[i];

                var frameStruct = new GMLObject();
                frameStruct["x"] = tex.SourceX;
                frameStruct["y"] = tex.SourceY;
                frameStruct["w"] = tex.SourceWidth;
                frameStruct["h"] = tex.SourceHeight;
                frameStruct["texture"] = PageManager.TexturePages.Keys.ToList().IndexOf(tex.Page);
                frameStruct["original_width"] = tex.BoundingWidth;
                frameStruct["original_height"] = tex.BoundingHeight;
                frameStruct["crop_width"] = tex.TargetWidth;
                frameStruct["crop_height"] = tex.TargetHeight;
                frameStruct["x_offset"] = tex.TargetX;
                frameStruct["y_offset"] = tex.TargetY;

                frames[i] = frameStruct;
            }
            struc["frames"] = frames;

            return struc;
        }

        // font_get_texture

        [GMLFunction("texture_get_width")]
        public static object texture_get_width(object?[] args)
        {
            if (args[0] is SpritePageItem item)
            {
                var (page, _) = PageManager.TexturePages[item.Page];
                return (double)item.SourceWidth / page.Width;
            }

            return 0.0;
        }

        [GMLFunction("texture_get_height")]
        public static object texture_get_height(object?[] args)
        {
            if (args[0] is SpritePageItem item)
            {
                var (page, _) = PageManager.TexturePages[item.Page];
                return (double)item.SourceHeight / page.Height;
            }

            return 0.0;
        }

        // texture_preload
        // texture_set_priority
        // texture_global_scale
        // texture_get_uvs

        [GMLFunction("draw_get_font")]
        public static object draw_get_font(object?[] args)
        {
            // What was *chosen*, not what is drawn with: a game that has set no font is told so,
            // exactly as GameMaker 2.3 answers -1 for its own default. TextManager.fontAsset falls
            // back to the game's first font so that drawing works, which is a different question.
            if (TextManager.ChosenFont == null)
            {
                return -1;
            }

            return TextManager.ChosenFont.AssetIndex;
        }

        [GMLFunction("draw_set_font")]
        public static object? draw_set_font(object?[] args)
        {
            var font = args[0].Conv<int>();

            if (font == -1)
            {
                DebugLog.LogVerbose("Font is -1, we'll just use the first font.");
                font = 0;
            }

            var library = TextManager.FontAssets;
            var fontAsset = library.FirstOrDefault(x => x.AssetIndex == font);

            if (fontAsset == null)
            {
                DebugLog.LogError($"draw_set_font: Unknown font index {font}! Listing available fonts:");
                foreach (var item in library)
                {
                    DebugLog.LogError($"- {item.name} (ID: {item.AssetIndex})");
                }
                return null;
            }

            TextManager.fontAsset = fontAsset;
            return null;
        }

        [GMLFunction("draw_get_halign")]
        public static object? draw_get_halign(object?[] args)
        {
            return (int)TextManager.halign;
        }

        [GMLFunction("draw_set_halign")]
        public static object? draw_set_halign(object?[] args)
        {
            var halign = args[0].Conv<int>();
            TextManager.halign = (HAlign)halign;
            return null;
        }

        [GMLFunction("draw_get_valign")]
        public static object? draw_get_valign(object?[] args)
        {
            return (int)TextManager.valign;
        }

        [GMLFunction("draw_set_valign")]
        public static object? draw_set_valign(object?[] args)
        {
            var valign = args[0].Conv<int>();
            TextManager.valign = (VAlign)valign;
            return null;
        }

        [GMLFunction("string_width")]
        public static object string_width(object?[] args)
        {
            // Must match CustomWindow.Draw(GMTextJob)'s own pre-split conversion (pre-GMS2 games
            // treat # as a line break in drawn text), or a string with an unconverted # renders as
            // more actual lines than this measures, silently undercounting -- callers that use
            // string_width/string_height to position multi-line text (draw_text(x, y - string_height(...), ...),
            // e.g. YoYoYo_Engine's objSign) end up shifted into whatever's below the text.
            var str = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());

            return TextManager.StringWidth(str);
        }

        [GMLFunction("string_height")]
        public static object string_height(object?[] args)
        {
            var str = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());

            if (TextManager.fontAsset == null)
            {
                return 1;
            }

            // Must match CustomWindow.Draw(GMTextJob)'s own total-height formula
            // ((lines.Length - 1) * lineSep + LineHeight(lastLine)) -- the two are paced by the
            // same TextManager.FontHeight() for a default separation, and have to stay that way.
            // A mismatch compounds per line, so a many-line sign's yOffset (draw_text(x, y -
            // string_height(signText), ...), e.g. YoYoYo_Engine's objSign) ends up taller than the
            // text actually rendered, pushing the whole block up past where it should sit.
            return TextManager.StringHeight(str);
        }

        /// <summary>
        /// The line separation the draw_text_ext family should actually use. A negative argument
        /// means the font's own default (see <see cref="TextManager.FontHeight"/>), which the
        /// non-ext draw paths already applied -- these passed the raw argument straight through, so
        /// the near-universal `sep = -1` call became a line separation of minus one pixel, stacking
        /// every line on top of the previous one.
        /// </summary>
        private static float LineSeparation(int sep) => sep < 0 ? TextManager.FontHeight() : sep;

        [GMLFunction("string_width_ext")]
        public static object string_width_ext(object?[] args)
        {
            var str = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());
            var w = args[2].Conv<int>();

            return TextManager.StringWidth(string.Join('\n', TextManager.WrapText(str, w)));
        }

        [GMLFunction("string_height_ext")]
        public static object string_height_ext(object?[] args)
        {
            var str = CompatFlags.ApplyHashNewlines(args[0].Conv<string>());
            var sep = args[1].Conv<int>();
            var w = args[2].Conv<int>();

            if (TextManager.fontAsset == null)
            {
                return 1;
            }

            // Same shape as TextManager.StringHeight, but with the caller's line separation rather
            // than the font's own -- that's the whole point of the _ext variant.
            var lines = TextManager.WrapText(str, w);
            return (int)(((lines.Length - 1) * LineSeparation(sep)) + TextManager.LineHeight(lines[^1]));
        }

        [GMLFunction("draw_text")]
        public static object? draw_text(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            TextManager.DrawText(x, y, str);
            return null;
        }

        [GMLFunction("draw_text_ext")]
        public static object? draw_text_ext(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            var sep = args[3].Conv<int>();
            var w = args[4].Conv<int>();

            var c = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);

            CustomWindow.Draw(new GMTextJob()
            {
                Colors = [c, c, c, c],
                angle = 0,
                asset = TextManager.fontAsset,
                halign = TextManager.halign,
                valign = TextManager.valign,
                lineSep = LineSeparation(sep),
                text = str,
                screenPos = new(x, y),
                scale = Vector2d.One,
                wrapWidth = w
            });

            return null;
        }

        [GMLFunction("draw_text_transformed")]
        public static object? draw_text_transformed(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            var xscale = args[3].Conv<double>();
            var yscale = args[4].Conv<double>();
            var angle = args[5].Conv<double>();
            TextManager.DrawTextTransformed(x, y, str, xscale, yscale, angle);
            return null;
        }

        [GMLFunction("draw_text_ext_transformed")]
        public static object? draw_text_ext_transformed(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            var sep = args[3].Conv<int>();
            var w = args[4].Conv<int>();
            var xscale = args[5].Conv<double>();
            var yscale = args[6].Conv<double>();
            var angle = args[7].Conv<double>();

            var c = SpriteManager.DrawColor.ABGRToCol4(SpriteManager.DrawAlpha);

            CustomWindow.Draw(new GMTextJob()
            {
                screenPos = new(x, y),
                asset = TextManager.fontAsset,
                angle = angle,
                Colors = [c, c, c, c],
                halign = TextManager.halign,
                valign = TextManager.valign,
                scale = new(xscale, yscale),
                lineSep = LineSeparation(sep),
                text = str,
                wrapWidth = w
            });

            return null;
        }

        [GMLFunction("draw_text_color")]
        [GMLFunction("draw_text_colour")]
        public static object? draw_text_colour(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            var c1 = args[3].Conv<int>();
            var c2 = args[4].Conv<int>();
            var c3 = args[5].Conv<int>();
            var c4 = args[6].Conv<int>();
            var alpha = args[7].Conv<double>();

            TextManager.DrawTextColor(x, y, str, c1, c2, c3, c4, alpha);

            return null;
        }

        [GMLFunction("draw_text_transformed_color")]
        [GMLFunction("draw_text_transformed_colour")]
        public static object? draw_text_transformed_colour(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            var xscale = args[3].Conv<double>();
            var yscale = args[4].Conv<double>();
            var angle = args[5].Conv<double>();
            var c1 = args[6].Conv<int>();
            var c2 = args[7].Conv<int>();
            var c3 = args[8].Conv<int>();
            var c4 = args[9].Conv<int>();
            var alpha = args[10].Conv<double>();

            CustomWindow.Draw(new GMTextJob()
            {
                text = str,
                screenPos = new(x, y),
                Colors = [c1.ABGRToCol4(alpha), c2.ABGRToCol4(alpha), c3.ABGRToCol4(alpha), c4.ABGRToCol4(alpha)],
                halign = TextManager.halign,
                valign = TextManager.valign,
                scale = new(xscale, yscale),
                angle = angle,
                asset = TextManager.fontAsset,
                // gamemaker is weird
                // "A value of -1 for the line separation argument will default to a separation based on the height of the "M" character in the chosen font."
                lineSep = TextManager.FontHeight()
            });

            return null;
        }

        [GMLFunction("draw_text_ext_color")]
        [GMLFunction("draw_text_ext_colour")]
        public static object? draw_text_ext_color(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            var sep = args[3].Conv<int>();
            var w = args[4].Conv<int>();
            var c1 = args[5].Conv<int>();
            var c2 = args[6].Conv<int>();
            var c3 = args[7].Conv<int>();
            var c4 = args[8].Conv<int>();
            var alpha = args[9].Conv<double>();

            CustomWindow.Draw(new GMTextJob()
            {
                Colors = [c1.ABGRToCol4(alpha), c2.ABGRToCol4(alpha), c3.ABGRToCol4(alpha), c4.ABGRToCol4(alpha)],
                angle = 0,
                asset = TextManager.fontAsset,
                halign = TextManager.halign,
                valign = TextManager.valign,
                lineSep = LineSeparation(sep),
                text = str,
                screenPos = new(x, y),
                scale = Vector2d.One,
                wrapWidth = w
            });

            return null;
        }

        [GMLFunction("draw_text_ext_transformed_color")]
        [GMLFunction("draw_text_ext_transformed_colour")]
        public static object? draw_text_ext_transformed_color(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var str = args[2].Conv<string>();
            var sep = args[3].Conv<int>();
            var w = args[4].Conv<int>();
            var xscale = args[5].Conv<double>();
            var yscale = args[6].Conv<double>();
            var angle = args[7].Conv<double>();
            var c1 = args[8].Conv<int>();
            var c2 = args[9].Conv<int>();
            var c3 = args[10].Conv<int>();
            var c4 = args[11].Conv<int>();
            var alpha = args[12].Conv<double>();

            CustomWindow.Draw(new GMTextJob()
            {
                screenPos = new(x, y),
                asset = TextManager.fontAsset,
                angle = angle,
                Colors = [c1.ABGRToCol4(alpha), c2.ABGRToCol4(alpha), c3.ABGRToCol4(alpha), c4.ABGRToCol4(alpha)],
                halign = TextManager.halign,
                valign = TextManager.valign,
                scale = new(xscale, yscale),
                lineSep = LineSeparation(sep),
                text = str,
                wrapWidth = w
            });

            return null;
        }

        [GMLFunction("draw_self")]
        public static object? draw_self(object?[] args)
        {
            SpriteManager.DrawSelf(VMExecutor.Self.GMSelf);
            return null;
        }

        [GMLFunction("draw_sprite")]
        public static object? draw_sprite(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var x = args[2].Conv<double>();
            var y = args[3].Conv<double>();

            if (subimg == -1)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            SpriteManager.DrawSprite(sprite, subimg, x, y);
            return null;
        }

        [GMLFunction("draw_sprite_ext")]
        public static object? draw_sprite_ext(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var x = args[2].Conv<double>();
            var y = args[3].Conv<double>();
            var xscale = args[4].Conv<double>();
            var yscale = args[5].Conv<double>();
            var rot = args[6].Conv<double>();
            var colour = args[7].Conv<int>();
            var alpha = args[8].Conv<double>();

            if (subimg == -1 && VMExecutor.Self.Self is GamemakerObject)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            SpriteManager.DrawSpriteExt(sprite, subimg, x, y, xscale, yscale, rot, colour, alpha);
            return null;
        }

        [GMLFunction("draw_sprite_pos")]
        public static object? draw_sprite_pos(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var x1 = args[2].Conv<double>();
            var y1 = args[3].Conv<double>();
            var x2 = args[4].Conv<double>();
            var y2 = args[5].Conv<double>();
            var x3 = args[6].Conv<double>();
            var y3 = args[7].Conv<double>();
            var x4 = args[8].Conv<double>();
            var y4 = args[9].Conv<double>();
            var alpha = args[10].Conv<double>();

            if (subimg == -1)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            var col = 16777215.ABGRToCol4(alpha); // c_white
            var pageItem = SpriteManager.GetSpritePageItem(sprite, subimg);
            var (pageTexture, id) = PageManager.TexturePages[pageItem.Page];

            var x = (double)pageItem.SourceX / pageTexture.Width;
            var y = (double)pageItem.SourceY / pageTexture.Height;
            var w = (double)pageItem.SourceWidth / pageTexture.Width;
            var h = (double)pageItem.SourceHeight / pageTexture.Height;

            CustomWindow.Draw(new GMTexturedPolygonJob()
            {
                Texture = pageItem,
                Colors = [col, col, col],
                Vertices = [new(x1, y1, GraphicsManager.GR_Depth), new(x2, y2, GraphicsManager.GR_Depth), new(x3, y3, GraphicsManager.GR_Depth)],
                UVs = [new(x, y), new(x + w, y), new(x + w, y + h)]
            });

            CustomWindow.Draw(new GMTexturedPolygonJob()
            {
                Texture = pageItem,
                Colors = [col, col, col],
                Vertices = [new(x3, y3, GraphicsManager.GR_Depth), new(x4, y4, GraphicsManager.GR_Depth), new(x1, y1, GraphicsManager.GR_Depth)],
                UVs = [new(x + w, y + h), new(x, y + h), new(x, y)]
            });

            return null;
        }

        [GMLFunction("draw_sprite_stretched")]
        public static object? draw_sprite_stretched(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var x = args[2].Conv<double>();
            var y = args[3].Conv<double>();
            var w = args[4].Conv<double>();
            var h = args[5].Conv<double>();

            if (subimg == -1)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            SpriteManager.draw_sprite_stretched(sprite, subimg, x, y, w, h, 0x00FFFFFF, 1);
            return null;
        }

        [GMLFunction("draw_sprite_stretched_ext")]
        public static object? draw_sprite_stretched_ext(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var x = args[2].Conv<double>();
            var y = args[3].Conv<double>();
            var w = args[4].Conv<double>();
            var h = args[5].Conv<double>();
            var colour = args[6].Conv<int>();
            var alpha = args[7].Conv<double>();

            if (subimg == -1)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            SpriteManager.draw_sprite_stretched(sprite, subimg, x, y, w, h, colour, alpha);
            return null;
        }

        [GMLFunction("draw_sprite_part")]
        public static object? draw_sprite_part(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var left = args[2].Conv<double>();
            var top = args[3].Conv<double>();
            var width = args[4].Conv<double>();
            var height = args[5].Conv<double>();
            var x = args[6].Conv<double>();
            var y = args[7].Conv<double>();

            if (subimg == -1)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            SpriteManager.DrawSpritePart(sprite, subimg, left, top, width, height, x, y);

            return null;
        }

        [GMLFunction("draw_sprite_part_ext")]
        public static object? draw_sprite_part_ext(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var left = args[2].Conv<double>();
            var top = args[3].Conv<double>();
            var width = args[4].Conv<double>();
            var height = args[5].Conv<double>();
            var x = args[6].Conv<double>();
            var y = args[7].Conv<double>();
            var xscale = args[8].Conv<double>();
            var yscale = args[9].Conv<double>();
            var colour = args[10].Conv<int>();
            var alpha = args[11].Conv<double>();

            if (subimg == -1)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            SpriteManager.DrawSpritePartExt(sprite, subimg, left, top, width, height, x, y, xscale, yscale, colour, alpha);

            return null;
        }

        [GMLFunction("draw_sprite_general")]
        public static object? draw_sprite_general(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var left = args[2].Conv<double>();
            var top = args[3].Conv<double>();
            var width = args[4].Conv<double>();
            var height = args[5].Conv<double>();
            var x = args[6].Conv<double>();
            var y = args[7].Conv<double>();
            var xscale = args[8].Conv<double>();
            var yscale = args[9].Conv<double>();
            var rot = args[10].Conv<double>();
            var c1 = args[11].Conv<int>();
            var c2 = args[12].Conv<int>();
            var c3 = args[13].Conv<int>();
            var c4 = args[14].Conv<int>();
            var alpha = args[15].Conv<double>();

            CustomWindow.Draw(new GMSpritePartJob()
            {
                texture = SpriteManager.GetSpritePageItem(sprite, subimg),
                screenPos = new(x, y),
                angle = rot,
                scale = new(xscale, yscale),
                Colors = [c1.ABGRToCol4(alpha), c2.ABGRToCol4(alpha), c3.ABGRToCol4(alpha), c4.ABGRToCol4(alpha)],
                origin = Vector2.Zero,
                left = left,
                top = top,
                width = width,
                height = height
            });

            return null;
        }

        [GMLFunction("draw_sprite_tiled")]
        public static object? draw_sprite_tiled(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var x = args[2].Conv<double>();
            var y = args[3].Conv<double>();

            return draw_sprite_tiled_ext(sprite, subimg, x, y, 1, 1, SpriteManager.DrawColor, SpriteManager.DrawAlpha);
        }

        [GMLFunction("draw_sprite_tiled_ext")]
        public static object? draw_sprite_tiled_ext(params object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var subimg = args[1].Conv<int>();
            var x = args[2].Conv<float>();
            var y = args[3].Conv<float>();
            var xscale = args[4].Conv<float>();
            var yscale = args[5].Conv<float>();
            var colour = args[6].Conv<int>();
            var alpha = args[7].Conv<float>();

            if (subimg == -1)
            {
                subimg = (int)VMExecutor.Self.GMSelf.image_index;
            }

            var spriteTex = SpriteManager.GetSpritePageItem(sprite, subimg);
            var spriteAsset = SpriteManager.GetSpriteAsset(sprite)!;

            TextureDrawTiled(spriteTex, spriteAsset.OriginX, spriteAsset.OriginY, x, y, xscale, yscale, true, true, 0, 0, RoomManager.CurrentRoom.SizeX, RoomManager.CurrentRoom.SizeY, colour, alpha);

            return null;
        }

        // Based on https://github.com/YoYoGames/GameMaker-HTML5/blob/68cded9270496ba29768c3e8a279d538fad659ee/scripts/yyWebGL.js#L1145
        // and GR_Texture_Draw_Tiled
        public static void TextureDrawTiled(
            SpritePageItem? pageitem,
            float _xorig, float _yorig,
            float _x, float _y,
            float _xsc, float _ysc,
            bool _htiled, bool _vtiled,
            float _xr, float _yr,
            float _wr, float _hr,
            int _col, float _alpha)
        {
            // TODO : HTML abs-es the x/yscale and uses the signed version later on, C++ just returns straight away if they're negative?

            if (pageitem == null || _xsc < 0.0001 || _ysc < 0.0001)
            {
                return;
            }

            // todo : check if active cam is 2d

            var drawcol = _col.ABGRToCol4(_alpha);

            if (!_htiled && !_vtiled)
            {
                // Only need to draw one sprite, that's nice!

                CustomWindow.Draw(new GMSpriteJob()
                {
                    texture = pageitem,
                    screenPos = new(_x, _y),
                    angle = 0,
                    scale = new(_xsc, _ysc),
                    Colors = [drawcol, drawcol, drawcol, drawcol],
                    origin = new(_xorig, _yorig)
                });

                return;
            }

            var ow = pageitem.BoundingWidth * _xsc;
            var oh = pageitem.BoundingHeight * _ysc;

            if ((ow <= 0) || (oh <= 0))
            {
                // would result in infinite drawing
                return;
            }

            var w = ow;
            var h = oh;
            if (_htiled)
            {
                w = (((_wr + (ow - 1)) / ow) + 2) * ow;
                _x = _xr + CustomMath.FMod(_x - _xr, ow) - ow;
            }

            if (_vtiled)
            {
                h = (((_hr + (oh - 1)) / oh) + 2) * oh;
                _y = _yr + CustomMath.FMod(_y - _yr, oh) - oh;
            }

            var page = PageManager.TexturePages[pageitem.Page];

            var invTWidth = 1.0 / page.image.Width;
            var invTHeight = 1.0 / page.image.Height;

            var u = (pageitem.SourceX * invTWidth);
            var u2 = (pageitem.SourceX + pageitem.SourceWidth) * invTWidth;
            var v = (pageitem.SourceY * invTHeight);
            var v2 = (pageitem.SourceY + pageitem.SourceHeight) * invTHeight;
            var nw = _xsc * pageitem.TargetWidth;
            var nh = _ysc * pageitem.TargetHeight;

            var tx = (w / ow);
            var ty = (h / oh);

            var x1 = -_xsc * _xorig;
            var y1 = -_ysc * _yorig;

            GraphicsManager.BindTexture0(page.id);

            var yy = _y + (pageitem.TargetY * _ysc);
            for (var cy = 0; cy < ty; cy++, yy += oh)
            {
                var xx = _x + (pageitem.TargetX * _xsc);
                var yy2 = yy + nh;

                for (var cx = 0; cx < tx; cx++, xx += ow)
                {
                    var xx2 = xx + nw;

                    GraphicsManager.Draw(PrimitiveType.Triangles, [
                        new(new(xx + x1, yy + y1, GraphicsManager.GR_Depth), drawcol, new(u, v)),
                        new(new(xx2 + x1, yy + y1, GraphicsManager.GR_Depth), drawcol, new(u2, v)),
                        new(new(xx2 + x1, yy2 + y1, GraphicsManager.GR_Depth), drawcol, new(u2, v2)),
                        new(new(xx2 + x1, yy2 + y1, GraphicsManager.GR_Depth), drawcol, new(u2, v2)),
                        new(new(xx + x1, yy2 + y1, GraphicsManager.GR_Depth), drawcol, new(u, v2)),
                        new(new(xx + x1, yy + y1, GraphicsManager.GR_Depth), drawcol, new(u, v))
                        ]);
                }
            }
        }

        public static void TextureDrawTiled(
            int texId, int texWidth, int texHeight,
            float _xorig, float _yorig,
            float _x, float _y,
            float _xsc, float _ysc,
            bool _htiled, bool _vtiled,
            float _xr, float _yr,
            float _wr, float _hr,
            int _col, float _alpha)
        {
            var drawcol = _col.ABGRToCol4(_alpha);

            if (!_htiled && !_vtiled)
            {
                // draw once
                return;
            }

            var ow = texWidth * _xsc;
            var oh = texHeight * _ysc;

            if ((ow <= 0) || (oh <= 0))
            {
                // would result in infinite drawing
                return;
            }

            var w = ow;
            var h = oh;
            if (_htiled)
            {
                w = (((_wr + (ow - 1)) / ow) + 2) * ow;
                _x = _xr + CustomMath.FMod(_x - _xr, ow) - ow;
            }

            if (_vtiled)
            {
                h = (((_hr + (oh - 1)) / oh) + 2) * oh;
                _y = _yr + CustomMath.FMod(_y - _yr, oh) - oh;
            }

            var tx = (w / ow);
            var ty = (h / oh);

            var x1 = -_xsc * _xorig;
            var y1 = -_ysc * _yorig;

            GraphicsManager.BindTexture0(texId);

            var yy = _y;
            for (var cy = 0; cy < ty; cy++, yy += oh)
            {
                var xx = _x;
                var yy2 = yy + oh;

                for (var cx = 0; cx < tx; cx++, xx += ow)
                {
                    var xx2 = xx + ow;

                    GraphicsManager.Draw(PrimitiveType.Triangles, [
                        new(new(xx + x1, yy + y1, GraphicsManager.GR_Depth), drawcol, new(0, 0)),
                        new(new(xx2 + x1, yy + y1, GraphicsManager.GR_Depth), drawcol, new(1, 0)),
                        new(new(xx2 + x1, yy2 + y1, GraphicsManager.GR_Depth), drawcol, new(1, 1)),
                        new(new(xx2 + x1, yy2 + y1, GraphicsManager.GR_Depth), drawcol, new(1, 1)),
                        new(new(xx + x1, yy2 + y1, GraphicsManager.GR_Depth), drawcol, new(0, 1)),
                        new(new(xx + x1, yy + y1, GraphicsManager.GR_Depth), drawcol, new(0, 0))
                    ]);
                }
            }
        }

        // shader_enable_corner_id

        [GMLFunction("surface_create")]
        public static object surface_create(object?[] args)
        {
            var w = args[0].Conv<int>();
            var h = args[1].Conv<int>();

            var format = 0; // TODO : work out if this actually is surface_rgba8unorm

            if (args.Length == 3)
            {
                format = args[2].Conv<int>();
            }

            return SurfaceManager.CreateSurface(w, h, format);
        }

        // surface_create_ext

        [GMLFunction("surface_resize")]
        public static object? surface_resize(object?[] args)
        {
            var surface_id = args[0].Conv<int>();
            var w = args[1].Conv<int>();
            var h = args[2].Conv<int>();

            if (w < 1 || h < 1 || w > 8192 || h > 8192)
            {
                throw new NotImplementedException($"Invalid surface dimensions - w:{w} h:{h}");
            }

            if (surface_id == SurfaceManager.application_surface)
            {
                SurfaceManager.NewApplicationSize = true;
                SurfaceManager.NewApplicationWidth = w;
                SurfaceManager.NewApplicationHeight = h;
                return null;
            }

            SurfaceManager.ResizeSurface(surface_id, w, h);
            return null;
        }

        [GMLFunction("surface_free")]
        public static object? surface_free(object?[] args)
        {
            var surface = args[0].Conv<int>();
            SurfaceManager.FreeSurface(surface, false);
            return null;
        }

        /// <summary>
        /// Writes a surface out as a real PNG, both to the persisted store (so file_exists sees it
        /// and it survives a reload, which is what a save-slot preview needs) and into
        /// SpriteManager's screenshot cache (so the sprite_add that usually follows immediately
        /// doesn't have to go back through the filesystem for it).
        ///
        /// Missing entirely before, which aborted the calling event: tijit engine 2 saves a preview
        /// image from its hud's Draw event, so every frame it saved took the whole HUD down with it.
        /// </summary>
        [GMLFunction("surface_save")]
        public static object? surface_save(object?[] args)
        {
            var surfaceId = args[0].Conv<int>();
            var fname = args[1].Conv<string>();

            if (!SurfaceManager.surface_exists(surfaceId))
            {
                DebugLog.LogWarning($"surface_save on nonexistent surface {surfaceId}.");
                return null;
            }

            var width = SurfaceManager.GetSurfaceWidth(surfaceId);
            var height = SurfaceManager.GetSurfaceHeight(surfaceId);
            var pixels = new byte[width * height * 4];

            GraphicsManager.FlushPolygonBatch(); // capture what has actually been drawn
            GL.GetTextureSubImage(
                SurfaceManager.GetSurfaceTexture(surfaceId), 0, 0, 0, 0, width, height, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels.Length, pixels);

            GMFileSystemExtension.SavePersisted(FileFunctions.PersistedName(fname), PngEncoder.Encode(pixels, width, height));
            SpriteManager._screenshotCache[Entry.ResolveDataWinPath(fname)] =
                new ImageResult { Width = width, Height = height, Data = pixels };
            return null;
        }

        [GMLFunction("surface_exists")]
        public static object surface_exists(object?[] args)
        {
            if (args[0] == null)
            {
                return false;
            }

            var surface = args[0].Conv<int>();
            return SurfaceManager.surface_exists(surface);
        }

        [GMLFunction("surface_get_width")]
        public static object surface_get_width(object?[] args)
        {
            if (args[0] == null)
            {
                return 0;
            }

            var surface_id = args[0].Conv<int>();
            return SurfaceManager.GetSurfaceWidth(surface_id);
        }

        [GMLFunction("surface_get_height")]
        public static object surface_get_height(object?[] args)
        {
            if (args[0] == null)
            {
                return 0;
            }

            var surface_id = args[0].Conv<int>();
            return SurfaceManager.GetSurfaceHeight(surface_id);
        }

        [GMLFunction("surface_get_texture")]
        public static object? surface_get_texture(object?[] args)
        {
            var surface_id = args[0].Conv<int>();

            if (!SurfaceManager.surface_exists(surface_id))
            {
                return -1;
            }

            // Real GM's surface_get_texture returns an opaque "texture pointer" that other
            // functions (texture_set_stage, texture_get_uvs, ...) accept anywhere a sprite's own
            // texture handle would go. Rendering.SurfaceTexture wraps the raw GL handle so
            // texture_set_stage can bind it directly without needing a SpritePageItem.
            return new Rendering.SurfaceTexture(SurfaceManager.GetSurfaceTexture(surface_id));
        }

        [GMLFunction("surface_get_target")]
        public static object surface_get_target(object?[] args)
        {
            return SurfaceManager.surface_get_target();
        }

        [GMLFunction("surface_set_target")]
        public static object surface_set_target(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (args.Length == 2)
            {
                throw new NotImplementedException("depth surface passed uh oh");
            }

            return SurfaceManager.surface_set_target(id);
        }

        // surface_get_target_ext
        // surface_set_target_ext

        [GMLFunction("surface_reset_target")]
        public static object surface_reset_target(object?[] args)
        {
            return SurfaceManager.surface_reset_target();
        }

        [GMLFunction("surface_depth_disable", GMLFunctionFlags.Stub)]
        public static object? surface_depth_disable(object?[] args)
        {
            var disable = args[0].Conv<bool>();

            return null;
        }

        [GMLFunction("draw_surface")]
        public static object? draw_surface(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();

            SurfaceManager.draw_surface(id, x, y);

            return null;
        }

        [GMLFunction("draw_surface_ext")]
        public static object? draw_surface_ext(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();
            var xscale = args[3].Conv<double>();
            var yscale = args[4].Conv<double>();
            var rot = args[5].Conv<double>();
            var col = args[6].Conv<int>();
            var alpha = args[7].Conv<double>();

            SurfaceManager.draw_surface_ext(id, x, y, xscale, yscale, rot, col, alpha);

            return null;
        }

        [GMLFunction("draw_surface_stretched")]
        public static object? draw_surface_stretched(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();
            var w = args[3].Conv<double>();
            var h = args[4].Conv<double>();

            SurfaceManager.draw_surface_stretched(id, x, y, w, h);
            return null;
        }

        [GMLFunction("draw_surface_stretched_ext")]
        public static object? draw_surface_stretched_ext(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();
            var w = args[3].Conv<double>();
            var h = args[4].Conv<double>();
            var col = args[5].Conv<int>();
            var alpha = args[6].Conv<double>();

            var sw = SurfaceManager.GetSurfaceWidth(id);
            var sh = SurfaceManager.GetSurfaceHeight(id);
            if (sw <= 0 || sh <= 0)
            {
                return null;
            }

            // Stretching is a scale, so the requested size divides back out by the surface's own.
            SurfaceManager.draw_surface_ext(id, x, y, w / sw, h / sh, 0, col, alpha);
            return null;
        }


        [GMLFunction("draw_surface_part")]
        public static object? draw_surface_part(object?[] args)
        {
            var id = args[0].Conv<int>();
            var left = args[1].Conv<int>();
            var top = args[2].Conv<int>();
            var w = args[3].Conv<int>();
            var h = args[4].Conv<int>();
            var x = args[5].Conv<double>();
            var y = args[6].Conv<double>();

            SurfaceManager.draw_surface_part(id, left, top, w, h, x, y);
            return null;
        }

        [GMLFunction("draw_surface_part_ext")]
        public static object? draw_surface_part_ext(object?[] args)
        {
            var id = args[0].Conv<int>();
            var left = args[1].Conv<int>();
            var top = args[2].Conv<int>();
            var w = args[3].Conv<int>();
            var h = args[4].Conv<int>();
            var x = args[5].Conv<double>();
            var y = args[6].Conv<double>();
            var xscale = args[7].Conv<double>();
            var yscale = args[8].Conv<double>();
            var colour = args[9].Conv<int>();
            var alpha = args[10].Conv<double>();

            SurfaceManager.draw_surface_part_ext(id, left, top, w, h, x, y, xscale, yscale, colour, alpha);
            return null;
        }

        /// <summary>
        /// A sub-rectangle of the surface, scaled, rotated and blended per corner. The four colour
        /// arguments are top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        [GMLFunction("draw_surface_general")]
        public static object? draw_surface_general(object?[] args)
        {
            var id = args[0].Conv<int>();
            var left = args[1].Conv<int>();
            var top = args[2].Conv<int>();
            var w = args[3].Conv<int>();
            var h = args[4].Conv<int>();
            var x = args[5].Conv<double>();
            var y = args[6].Conv<double>();
            var xscale = args[7].Conv<double>();
            var yscale = args[8].Conv<double>();
            var rot = args[9].Conv<double>();
            var alpha = args[14].Conv<double>();

            // SurfaceManager's part-draw takes one blend colour and no rotation, so those two
            // arguments are honoured only as far as it can. Rather than silently dropping them --
            // the "present but inert" trap this codebase has hit repeatedly -- anything that would
            // be lost says so once, at warning level.
            if (rot != 0)
            {
                DebugLog.LogWarning("draw_surface_general: rotation is not supported yet and is being ignored.");
            }

            var topLeft = args[10].Conv<int>();
            if (args[11].Conv<int>() != topLeft || args[12].Conv<int>() != topLeft || args[13].Conv<int>() != topLeft)
            {
                DebugLog.LogWarning("draw_surface_general: per-corner blending is not supported yet; using the first colour.");
            }

            SurfaceManager.draw_surface_part_ext(id, left, top, w, h, x, y, xscale, yscale, topLeft, alpha);
            return null;
        }


        [GMLFunction("draw_surface_tiled")]
        public static object? draw_surface_tiled(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<float>();
            var y = args[2].Conv<float>();

            var surf = SurfaceManager.GetSurfaceTexture(id);

            TextureDrawTiled(surf, SurfaceManager.GetSurfaceWidth(id), SurfaceManager.GetSurfaceHeight(id),
                0, 0,
                x, y,
                1, 1,
                true, true,
                0, 0,
                RoomManager.CurrentRoom.SizeX, RoomManager.CurrentRoom.SizeY,
                0xFFFFFF, (float)SpriteManager.DrawAlpha);

            return null;
        }

        [GMLFunction("draw_surface_tiled_ext")]
        public static object? draw_surface_tiled_ext(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<float>();
            var y = args[2].Conv<float>();
            var xscale = args[3].Conv<float>();
            var yscale = args[4].Conv<float>();
            var col = args[5].Conv<int>();
            var alpha = args[6].Conv<float>();

            var surf = SurfaceManager.GetSurfaceTexture(id);

            TextureDrawTiled(surf, SurfaceManager.GetSurfaceWidth(id), SurfaceManager.GetSurfaceHeight(id),
                0, 0,
                x, y,
                xscale, yscale,
                true, true,
                0, 0,
                RoomManager.CurrentRoom.SizeX, RoomManager.CurrentRoom.SizeY,
                col, alpha);

            return null;
        }

        // surface_save
        // surface_save_part
        // surface_getpixel

        /// <summary>
        /// The colour at a surface pixel, with no alpha -- surface_getpixel_ext is the variant that
        /// includes it. Captured from GMS 1.4.9999: reading a red pixel gives 255 (0x0000FF), i.e.
        /// a plain BGR colour rather than an ABGR one, so the alpha byte must not leak in.
        /// </summary>
        [GMLFunction("surface_getpixel")]
        public static object surface_getpixel(object?[] args)
        {
            var values = SurfaceManager.ReadPixels(args[0].Conv<int>(), args[1].Conv<int>(), args[2].Conv<int>(), 1, 1);
            return values[0] | (values[1] << 8) | (values[2] << 16);
        }

        /// <summary>
        /// Reads a pixel from whatever is currently being drawn to -- the active surface if one is
        /// set, otherwise the screen.
        /// </summary>
        [GMLFunction("draw_getpixel")]
        public static object draw_getpixel(object?[] args)
        {
            var x = args[0].Conv<int>();
            var y = args[1].Conv<int>();

            var pixels = new byte[4];
            GraphicsManager.FlushPolygonBatch(); // anything still queued has to land before we read
            GL.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            return pixels[0] | (pixels[1] << 8) | (pixels[2] << 16);
        }

        [GMLFunction("draw_ellipse_color")]
        [GMLFunction("draw_ellipse_colour")]
        public static object? draw_ellipse_color(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var col1 = args[4].Conv<int>();
            var col2 = args[5].Conv<int>();
            var outline = args[6].Conv<bool>();

            // col1 is the centre and col2 the rim, the same gradient draw_circle_color uses.
            DrawEllipseFan((x1 + x2) / 2, (y1 + y2) / 2, Math.Abs((x1 - x2) / 2), Math.Abs((y1 - y2) / 2),
                outline, col1, col2);
            return null;
        }

        [GMLFunction("surface_getpixel_ext")]
        public static object? surface_getpixel_ext(object?[] args)
        {
            var surfaceid = args[0].Conv<int>();
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();

            var values = SurfaceManager.ReadPixels(surfaceid, x, y, 1, 1);

            // TODO : check this
            var r = values[0];
            var g = values[1];
            var b = values[2];
            var a = values[3];
            return r | g << 8 | b << 16 | a << 24;
        }

        [GMLFunction("surface_copy")]
        public static object? surface_copy(object?[] args)
        {
            var destination = args[0].Conv<int>();
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();
            var source = args[3].Conv<int>();

            SurfaceManager.Copy(destination, x, y, source, 0, 0, SurfaceManager.GetSurfaceWidth(source), SurfaceManager.GetSurfaceHeight(source));

            return null;
        }

        [GMLFunction("surface_copy_part")]
        public static object? surface_copy_part(object?[] args)
        {
            var destination = args[0].Conv<int>();
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();
            var source = args[3].Conv<int>();
            var xs = args[4].Conv<int>();
            var ys = args[5].Conv<int>();
            var ws = args[6].Conv<int>();
            var hs = args[7].Conv<int>();

            SurfaceManager.Copy(destination, x, y, source, xs, ys, ws, hs);

            return null;
        }

        // skeleton stuff
    }
}
