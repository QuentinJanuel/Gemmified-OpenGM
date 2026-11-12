// Backs OpenGM.Wasm/BrowserWindow.cs -- window_set_caption's real effect under browser-wasm is
// setting the browser tab's title, since there's no real OS window titlebar to update.

import { hostWindow, surface } from "./services.js";

export function setTitle(title) {
    if (typeof document !== "undefined") {
        document.title = title;
    } else {
        // In a Web Worker there's no document -- the host owns the caption. See `WindowServices`.
        hostWindow().setTitle(title);
    }
}

// Backs window_set_fullscreen. The fullscreenable element lives wherever the host is, not here, so
// the request is forwarded; window_get_fullscreen is answered in C# from the host's last reported
// state (Gems.Fullscreen), including for changes no game asked for (a player pressing Escape).

export function setFullscreen(enabled) {
    hostWindow().setFullscreen(enabled);
}

// Backs texture_set_interpolation (3DFunctions.cs -> PageManager.SetInterpolation). WebGL-level
// texel filtering alone doesn't make sprites look smoothed in practice -- the real, dominant
// upscale most players actually see is the browser's own CSS scale of the <canvas> element up to
// its displayed size (a low-res game room shown in a much bigger window), which is hardcoded to
// `image-rendering: pixelated` in player.html regardless of any in-game setting. The actual DOM
// canvas lives wherever the host is, not in this worker, so this only reports the request through
// the surface (mirroring setFullscreen above) and the host applies it.
export function setSmoothing(enabled) {
    surface().reportSmoothing(enabled);
}
