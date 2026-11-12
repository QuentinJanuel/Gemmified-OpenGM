using System.Runtime.InteropServices.JavaScript;

namespace OpenGM;

/// <summary>Backs OpenTKShim's shimmed GameWindow.Title with the real browser tab title.</summary>
internal static partial class BrowserWindow
{
    private const string Module = "browserwindow";

    [JSImport("setTitle", Module)]
    public static partial void SetTitle(string title);

    // Backs window_set_fullscreen/window_get_fullscreen (GraphicFunctions.cs). The fullscreenable
    // element lives in the host's page, not this worker, so SetFullscreen only requests it there;
    // GetFullscreen is the host's last reported state (its "fullscreen-changed" note, see Gems.cs).
    [JSImport("setFullscreen", Module)]
    public static partial void SetFullscreen(bool enabled);

    public static bool GetFullscreen() => Gems.Fullscreen;

    // Backs texture_set_interpolation's real-world visible effect: the browser's own CSS upscale
    // of the <canvas> element (see PageManager.SetInterpolation and browserwindow.js's own comment
    // on setSmoothing for why WebGL-level filtering alone isn't enough).
    [JSImport("setSmoothing", Module)]
    public static partial void SetSmoothing(bool enabled);
}
