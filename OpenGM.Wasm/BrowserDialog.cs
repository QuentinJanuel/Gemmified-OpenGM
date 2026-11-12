using System.Runtime.InteropServices.JavaScript;

namespace OpenGM;

/// <summary>
/// Backs the browser-wasm implementations of GML's blocking dialogs (show_message / show_question /
/// get_string / get_integer) with the styled in-page React dialog. The engine runs in a Web Worker,
/// so <see cref="ShowDialog"/> genuinely blocks this (worker) thread via Atomics.wait while the main
/// thread shows the dialog and writes the answer back -- see wwwroot/dialog.js. That off-main-thread
/// blocking is the whole reason OpenGM was moved to a worker.
/// </summary>
internal static partial class BrowserDialog
{
    private const string Module = "dialog";

    // text: the message. labels: button captions joined by U+001F. primary/cancel: indices into the
    // buttons for the Enter/Escape defaults (cancel -1 = not dismissible). inputKind: 0 none, 1
    // string, 2 integer. Returns the 0-based index of the clicked button, or -1 if dismissed.
    [JSImport("showDialog", Module)]
    public static partial int ShowDialog(string text, string labels, int primary, int cancel, int inputKind, string inputInitial);

    // The text the user entered (for get_string / get_integer), valid right after ShowDialog returns.
    [JSImport("dialogValue", Module)]
    public static partial string DialogValue();
}
