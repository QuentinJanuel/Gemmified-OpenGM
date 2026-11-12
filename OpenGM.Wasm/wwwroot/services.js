// This engine's wiring inside its worker: what the C# half of the wire (Gems.cs) and the JS glue the
// .NET runtime imports (webgl2.js, webaudio.js, audio-proxy.js, dialog.js, browserwindow.js,
// input.js) share, and the notes both send to the host realm's half.
//
// ONE RULE MAKES IT WORK. The glue is imported by the runtime as "../<name>.js?v=N" (see
// `WasmEntry.AssetVersion`), and Gems.cs imports this file as "../services.js". A relative import
// does not inherit the importer's query string, so "./services.js" resolves to the same URL from
// every one of them: one module, one instance. **Never add a version suffix to this file's
// specifier**: on one side only, it splits the state in two, and what you would see is the "not
// installed" error below from code that plainly runs after init.

let canvasObject = null;
let dialogBuffer = null;
let closed = false;

function post(message, transfer) {
  self.postMessage(message, transfer);
}

/** A note to the host realm's half. Nothing goes out after unload. */
function note(body, transfer) {
  if (!closed) post({ w: "note", body }, transfer);
}

// -- For Gems.cs ---------------------------------------------------------------------------------

/** init's canvas and dialog buffer, before the runtime imports any of the glue that reads them. */
export function install(canvas, buffer) {
  canvasObject = canvas;
  dialogBuffer = buffer;
}

/** A request's answer; `json` is its body, or null for none. */
export function reply(id, json) {
  let body;
  try {
    body = json === null ? undefined : JSON.parse(json);
  } catch {
    // A diagnostic is never worth failing a call over (stats is the only free-form answer).
    body = {};
  }
  post({ w: "ok", id, body });
}

export function fail(id, code, detail) {
  post({ w: "err", id, error: { engineError: true, code, detail } });
}

export const fsSet = (key, value) => note({ kind: "fs-set", key, value });
export const fsRemove = (key) => note({ kind: "fs-remove", key });
export const save = () => note({ kind: "save" });
export const netOpen = (id, port) => note({ kind: "net-open", id, port });
export function netSend(id, bytes) {
  const buffer = bytes.slice().buffer;
  note({ kind: "net-send", id, bytes: buffer }, [buffer]);
}
export const netEnd = (id) => note({ kind: "net-end", id });

/** After unload, the worker closes itself; the host terminates it anyway. */
export function close() {
  closed = true;
  self.close();
}

export const bytes = (buffer) => new Uint8Array(buffer);
export const entries = (record) => Object.entries(record).flat();

// -- For the glue --------------------------------------------------------------------------------

function required(value, name) {
  if (value === null) {
    throw new Error(
      `services.js: ${name} was asked for before init installed it. If this is running after` +
        " init, this module has been loaded twice under two URLs -- see the note at the top.",
    );
  }
  return value;
}

/** The drawing buffer the host handed over. */
export function canvas() {
  return required(canvasObject, "the canvas");
}

/** Where the game's real resolution and its smoothing request are reported. */
export function surface() {
  return {
    reportSize: (width, height) => note({ kind: "resize", width, height }),
    reportSmoothing: (enabled) => note({ kind: "smoothing", enabled }),
  };
}

const device = { send: (command, transfer) => note({ kind: "audio", command }, transfer) };

/** The host's audio device: one `send` per operation that actually produces sound. */
export function audio() {
  return device;
}

/** The audio device once init has run, for code that also runs on a page with a real AudioContext. */
export function audioIfInstalled() {
  return canvasObject === null ? null : device;
}

/** The caption and the fullscreen request, which belong to whoever owns the page. */
export function hostWindow() {
  return {
    setTitle: (title) => note({ kind: "title", title }),
    setFullscreen: (enabled) => note({ kind: "set-fullscreen", enabled }),
  };
}

/**
 * Blocking dialogs: the question goes out as a note, and this thread blocks on the
 * shared buffer until the host writes the answer. Without a buffer (the page is not cross-origin
 * isolated) that is reported rather than refused: a game that never opens a dialog still plays.
 */
export function dialogs() {
  return { show };
}

function show(request) {
  if (dialogBuffer === null) {
    note({ kind: "error", code: "unsupported-environment", detail: "blocking dialogs need a handshake buffer" });
    return { button: -1, value: "" };
  }
  // signal, button, length, reserved; then the answer's UTF-8. Marked waiting *before* the
  // question goes out, so an instant answer cannot be missed.
  const control = new Int32Array(dialogBuffer, 0, 4);
  Atomics.store(control, 0, 0);
  note({ kind: "dialog-asked", request });
  Atomics.wait(control, 0, 0);
  const length = Atomics.load(control, 2);
  const value = new TextDecoder().decode(new Uint8Array(dialogBuffer, 16).slice(0, length));
  return { button: Atomics.load(control, 1), value };
}

// The other direction. Both are published by the glue when the runtime imports it, which is after
// init, so a call that arrives first simply finds nothing to deliver to.

let inputApplier = null;

/** Published by `input.js`: hands one `GameInput` to the engine's own input state. */
export function publishInputApplier(fn) {
  inputApplier = fn;
}

/** Gems.cs: one input event, at the start of the frame after it arrived. */
export function applyInput(input) {
  inputApplier?.(input);
}

let audioEndedHandler = null;

/** Published by `audio-proxy.js`: routes "a node finished" back to the callback webaudio.js set. */
export function publishAudioEnded(fn) {
  audioEndedHandler = fn;
}

/** Gems.cs: the host's audio device finished playing a node. */
export function audioEnded(id) {
  audioEndedHandler?.(id);
}
