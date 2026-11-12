// OpenGM as a GEMS plugin: the module a host imports, published as host.js.
//
// The engine runs in a worker (opengm-worker.js, whose wire half is OpenGM.Wasm/Gems.cs). This is
// the half that lives in the host's realm, because only it can have what a worker cannot: the
// AudioContext the engine's sound is replayed on, the shared buffer a blocking dialog is answered
// through, and the Worker itself. It lowers the session onto the wire between the two. Importing
// this module touches nothing; the worker starts in `create`.

const API_VERSION = 5;

/** GameMaker Studio's game data. Its presence is what this engine recognises a game by. */
const DATAWIN_RE = /(^|\/)data\.win$/i;

// `FORM` is the generic IFF magic; `FORM` whose first chunk is `GEN8` is only GameMaker's.
const FORM = 0x464f_524d;
const GEN8 = 0x4745_4e38;
const HEADER_BYTES = 12;

const DIALOG_BUFFER_BYTES = 64 * 1024;
const DIALOG_VALUE_OFFSET = 16;
const DIALOG_DISMISSED = -1;

/** A thrown error a host reads by its brand, never by its class. */
class EngineError extends Error {
  constructor(code, detail) {
    super(detail === undefined ? code : `${code}: ${detail}`);
    this.name = "EngineError";
    this.engineError = true;
    this.code = code;
    this.detail = detail;
  }
}

async function readHeader(file) {
  if (file.size < HEADER_BYTES) return { isForm: false, isGameMaker: false };
  // A range where the host offers one: this runs while the host is still choosing an engine.
  const bytes = (await file.readRange?.(0, HEADER_BYTES)) ?? (await file.read());
  if (bytes.byteLength < HEADER_BYTES) return { isForm: false, isGameMaker: false };
  const view = new DataView(bytes.buffer, bytes.byteOffset, HEADER_BYTES);
  const isForm = view.getUint32(0, false) === FORM;
  return { isForm, isGameMaker: isForm && view.getUint32(8, false) === GEN8 };
}

export default {
  id: "opengm",
  apiVersion: API_VERSION,
  capabilities: {
    // The audio-cache knobs. The defaults are the conservative ones; a host that knows its device says.
    settings: [
      { kind: "number", key: "audio.shortCompressedBytes", default: 256 * 1024, min: 0, unit: "bytes" },
      { kind: "number", key: "audio.shortDurationSecs", default: 5, min: 0, unit: "seconds" },
      { kind: "number", key: "audio.musicBudgetBytes", default: 192 * 1024 * 1024, min: 0, unit: "bytes" },
    ],
  },

  /**
   * Decided by reading the container: a `data.win` that is `FORM` + `GEN8` is "exact", a `FORM` of
   * something else "probable", and anything else is declined rather than failed at load.
   */
  async accepts(files) {
    let fallback = null;
    for (const file of files.files.filter((file) => DATAWIN_RE.test(file.path))) {
      const { isForm, isGameMaker } = await readHeader(file);
      if (isGameMaker) return { confidence: "exact", entry: file.path };
      if (isForm && fallback === null) fallback = { confidence: "probable", entry: file.path };
    }
    return fallback;
  },

  create(host) {
    // Beside this module, not at `assets.base`, which may point at a CDN holding only the payload.
    const worker = new Worker(new URL("opengm-worker.js", import.meta.url), { type: "module" });
    return createSession(host, worker);
  },
};

/** The main half of the wire's envelope: requests by id, and notes. */
function openWire(worker, onNote) {
  let nextId = 1;
  let closed = false;
  const pending = new Map();

  const listener = ({ data }) => {
    if (typeof data !== "object" || data === null || !("w" in data)) return;
    if (data.w === "note") return onNote(data.body);
    if (data.w !== "ok" && data.w !== "err") return;
    const entry = pending.get(data.id);
    pending.delete(data.id);
    if (data.w === "ok") entry?.resolve(data.body);
    else entry?.reject(new EngineError(data.error.code, data.error.detail));
  };
  worker.addEventListener("message", listener);

  return {
    notify(body, transfer) {
      if (!closed) worker.postMessage({ w: "note", body }, transfer);
    },
    request(body, transfer) {
      if (closed) return Promise.reject(new EngineError("internal", "the wire is closed"));
      const id = nextId++;
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        worker.postMessage({ w: "req", id, body }, transfer);
      });
    },
    close() {
      if (closed) return;
      closed = true;
      worker.removeEventListener("message", listener);
      // A request whose answer can no longer arrive is what a terminated worker looks like.
      for (const entry of pending.values()) {
        entry.reject(new EngineError("internal", "the wire closed before the reply arrived"));
      }
      pending.clear();
    },
  };
}

/** Starts the worker's engine on `host` and completes the init handshake. */
async function createSession(host, worker) {
  // The real AudioContext, made in the host's realm so whatever the host has done to it (a master
  // volume patched onto `destination`) applies.
  const AudioCtor = globalThis.AudioContext;
  if (AudioCtor === undefined) {
    worker.terminate();
    throw new EngineError("unsupported-environment", "this realm has no AudioContext");
  }
  const context = new AudioCtor();
  const unsupported = new Set();
  const audio = audioExecutor(context, (id) => wire.notify({ kind: "audio-ended", id }), (reason, command) => {
    // A command that did nothing, never a reason to end the game (Firefox has no
    // AudioListener.positionX). Once per kind, because some arrive every frame.
    const kind = `${command.op} ${"param" in command ? command.param : ""} (${reason})`;
    if (unsupported.has(kind)) return;
    unsupported.add(kind);
    console.warn(`opengm: audio command ${kind} did nothing`);
  });

  // Browsers only let audio start from a user gesture.
  const gestures = new AbortController();
  if (typeof document !== "undefined") {
    const unlock = () => void context.resume().catch(() => {});
    document.addEventListener("pointerdown", unlock, { signal: gestures.signal });
    document.addEventListener("keydown", unlock, { signal: gestures.signal });
  }

  // The engine blocks its worker on this while the host shows the dialog. Needs
  // cross-origin isolation.
  const dialogBuffer = new SharedArrayBuffer(DIALOG_BUFFER_BYTES);
  let dialogs = Promise.resolve();
  const asked = (request) => {
    // Serialised: two overlapping questions would write two answers into one buffer.
    dialogs = dialogs.then(async () => {
      let result;
      try {
        result = await host.dialogs.show(request);
        // An unknown button is a dismissal, and `value` is a string exactly when there is a field.
        const known = request.buttons.some((button) => button.value === result.button);
        result = { button: known ? result.button : DIALOG_DISMISSED, value: request.input ? (result.value ?? "") : null };
      } catch {
        result = { button: DIALOG_DISMISSED, value: request.input ? "" : null };
      }
      // Released before the worker wakes, so a key held when the dialog opened is not still held.
      wire.notify({ kind: "input", input: { kind: "focus-lost" } });
      writeDialogResult(dialogBuffer, result);
    }).catch(() => {});
  };

  let session = null;
  const wire = openWire(worker, (message) => {
    if (message.kind === "dialog-asked") asked(message.request);
    else session?.note(message);
  });

  const release = () => {
    gestures.abort();
    audio.dispose();
    // Tell it, then terminate it: waiting for an acknowledgement would be waiting on an engine
    // already decided against.
    wire.notify({ kind: "unload" });
    wire.close();
    worker.terminate();
    void context.close().catch(() => {});
  };

  // A worker that fails to start answers nothing; closing the wire rejects what is outstanding.
  worker.addEventListener("error", (event) => {
    if (session === null) wire.close();
    else session.fail(`the engine's worker failed: ${event.message}`);
  });

  try {
    await wire.request(
      {
        kind: "init",
        init: {
          apiVersion: API_VERSION,
          canvas: host.surface.canvas,
          assetBase: host.assets.base,
          settings: host.settings,
          gameId: host.game,
          storeSeed: host.replica.seed(),
          dialogBuffer,
          launchArgs: host.launchArgs,
          ports: host.network.ports,
        },
      },
      [host.surface.canvas],
    );
  } catch (error) {
    release();
    throw error;
  }

  session = new WorkerSession(host, wire, audio, release);
  // The host's own changes to the filesystem, straight into the running game's replica.
  host.replica.watch({
    put: (path, bytes) => {
      const buffer = bytes.buffer;
      wire.notify({ kind: "fs-put", path, bytes: buffer }, [buffer]);
    },
    drop: (key) => wire.notify({ kind: "fs-drop", key }),
  });
  return session;
}

/** The node types the engine creates. */
const CREATORS = {
  gain: (ctx) => ctx.createGain(),
  stereoPanner: (ctx) => ctx.createStereoPanner(),
  panner: (ctx) => ctx.createPanner(),
  bufferSource: (ctx) => ctx.createBufferSource(),
  biquadFilter: (ctx) => ctx.createBiquadFilter(),
  dynamicsCompressor: (ctx) => ctx.createDynamicsCompressor(),
  delay: (ctx, args) => ctx.createDelay(typeof args[0] === "number" ? args[0] : 1),
  oscillator: (ctx) => ctx.createOscillator(),
  convolver: (ctx) => ctx.createConvolver(),
};

/**
 * Replays the worker's audio commands on the host's context: the ops
 * wwwroot/audio-proxy.js and webaudio.js send. Nothing here throws: the engine's graph and this
 * mirror of it can disagree (a source started twice, a node already disconnected), and a wrong
 * sound is no reason to end the game, so what was skipped is reported instead.
 */
function audioExecutor(context, onEnded, onUnsupported) {
  const nodes = new Map();
  const buffers = new Map();
  // Read every time, not cached: a host may patch the `destination` getter after this was built.
  const nodeOf = (id) => (id === 0 ? context.destination : nodes.get(id));
  // A mistake on the engine's side is reported; one the browser rejects is "rejected".
  const attempt = (command, reason, action) => {
    try {
      action();
    } catch {
      if (reason) onUnsupported(reason, command);
    }
  };

  const execute = (command) => {
    switch (command.op) {
      case "create": {
        const create = CREATORS[command.type];
        if (!create) return onUnsupported("node-type", command);
        return attempt(command, "rejected", () => nodes.set(command.id, create(context, command.args)));
      }
      case "connect": {
        const from = nodeOf(command.id);
        const to = nodeOf(command.target);
        if (!from || !to) return onUnsupported("unknown-node", command);
        return attempt(command, "rejected", () => from.connect(to));
      }
      case "disconnect":
      case "start":
      case "stop":
      case "onended": {
        const node = nodeOf(command.id);
        if (!node) return onUnsupported("unknown-node", command);
        // Already disconnected, started or stopped: routine for an engine replaying its own graph.
        if (command.op === "disconnect") return attempt(command, null, () => node.disconnect());
        if (command.op === "start") return attempt(command, null, () => node.start(command.when, command.offset));
        if (command.op === "stop") return attempt(command, null, () => node.stop());
        node.onended = () => onEnded(command.id);
        return;
      }
      case "param": {
        const params = command.id === -1 ? context.listener : nodeOf(command.id);
        if (!params) return onUnsupported("unknown-node", command);
        const param = params[command.param];
        if (!param || typeof param !== "object" || !("value" in param)) return onUnsupported("unknown-param", command);
        return attempt(command, "rejected", () => {
          param.value = command.value;
        });
      }
      case "prop": {
        const node = nodeOf(command.id);
        if (!node) return onUnsupported("unknown-node", command);
        return attempt(command, "rejected", () => {
          node[command.prop] = command.value;
        });
      }
      case "createBuffer":
        return attempt(command, "rejected", () =>
          buffers.set(command.id, context.createBuffer(command.channels, command.frames, command.rate)),
        );
      case "bufferData": {
        const buffer = buffers.get(command.id);
        if (!buffer) return onUnsupported("unknown-node", command);
        return attempt(command, "rejected", () => buffer.copyToChannel(command.data, command.ch, command.start ?? 0));
      }
      case "setBuffer": {
        const node = nodeOf(command.id);
        const buffer = buffers.get(command.bufferId);
        if (!node || !buffer) return onUnsupported("unknown-node", command);
        return attempt(command, "rejected", () => {
          node.buffer = buffer;
        });
      }
      case "deleteBuffer":
        buffers.delete(command.id);
        return;
      case "resume":
        return void context.resume().catch(() => {});
      case "suspend":
        return void context.suspend().catch(() => {});
    }
  };

  return {
    execute,
    // Disconnected, not closed: the session closes the context itself.
    dispose() {
      for (const node of nodes.values()) attempt(null, null, () => node.disconnect());
      nodes.clear();
      buffers.clear();
    },
  };
}

/** Writes the player's answer, then wakes the engine. */
function writeDialogResult(buffer, result) {
  const control = new Int32Array(buffer, 0, 4);
  const bytes = new Uint8Array(buffer, DIALOG_VALUE_OFFSET);
  const encoded = new TextEncoder().encode(result.value ?? "");
  const length = Math.min(encoded.length, bytes.length);
  bytes.set(encoded.subarray(0, length));
  Atomics.store(control, 1, result.button);
  Atomics.store(control, 2, length);
  Atomics.store(control, 0, 1);
  Atomics.notify(control, 0);
}

class WorkerSession {
  ended = false;
  /** A frame that has not finished. A frame stopped on a dialog has not finished. */
  ticking = null;
  /** The game's loopback connections, by the engine's id. */
  connections = new Map();
  lastStats = {};

  constructor(host, wire, audio, release) {
    this.host = host;
    this.wire = wire;
    this.audio = audio;
    this.release = release;
    this.fullscreen = host.window.isFullscreen();
  }

  async start(files) {
    // Read here: a host may hold a game as `File` handles, which a worker cannot open. Copied, then
    // transferred: the host's bytes may be a view into something it still owns.
    const wireFiles = [];
    for (const file of files.files) {
      const bytes = (await file.read()).slice();
      wireFiles.push({ path: file.path, bytes: bytes.buffer });
    }
    return await this.wire.request({ kind: "start", files: wireFiles }, wireFiles.map((file) => file.bytes));
  }

  step(now) {
    if (this.ended) return false;
    // A frame suspended on a dialog outlives several of the host's frames; it is still going.
    if (this.ticking !== null) return true;
    this.pushFullscreenChange();
    this.relayFromHost();
    this.ticking = this.wire.request({ kind: "tick", now }).then(
      ({ running }) => {
        this.ticking = null;
        if (!running) this.ended = true;
        return running;
      },
      (error) => {
        // A frame whose answer can never arrive is what a dead worker looks like from here.
        this.ticking = null;
        this.ended = true;
        this.host.events.onError("internal", `the engine stopped answering: ${String(error)}`);
        return false;
      },
    );
    return this.ticking;
  }

  sendInput(input) {
    this.wire.notify({ kind: "input", input });
  }

  setSetting(key, value) {
    this.wire.notify({ kind: "setting", key, value });
  }

  /** Synchronous in the contract and a round trip away here: the previous answer, and ask again. */
  stats() {
    void this.wire
      .request({ kind: "stats" })
      .then((answer) => {
        this.lastStats = answer;
      })
      .catch(() => {});
    return this.lastStats;
  }

  dispose() {
    this.ended = true;
    this.release();
  }

  note(message) {
    switch (message.kind) {
      case "resize":
        return this.host.surface.reportSize(message.width, message.height);
      case "title":
        return this.host.window.setTitle(message.title);
      case "smoothing":
        return this.host.surface.reportSmoothing(message.enabled);
      case "set-fullscreen":
        this.host.window.setFullscreen(message.enabled);
        // Taken as read; the next frame corrects it if the host declined.
        this.fullscreen = message.enabled;
        return;
      case "fs-set":
        return this.host.replica.set(message.key, message.value);
      case "fs-remove":
        return this.host.replica.remove(message.key);
      case "save":
        // The replica's own call, not `events.onSave`, which would count one save twice.
        return this.host.replica.save();
      case "audio":
        return this.audio.execute(message.command);
      case "net-open": {
        const connection = this.host.network.connect(message.port);
        if (connection) this.connections.set(message.id, connection);
        else this.wire.notify({ kind: "net-close", id: message.id });
        return;
      }
      case "net-send":
        return this.connections.get(message.id)?.send(new Uint8Array(message.bytes));
      case "net-end":
        this.connections.get(message.id)?.close();
        this.connections.delete(message.id);
        return;
      case "ended":
        this.ended = true;
        return this.host.events.onEnded();
      case "error":
        return this.host.events.onError(message.code, message.detail);
    }
  }

  /** The worker died without answering. */
  fail(detail) {
    this.ended = true;
    this.host.events.onError("internal", detail);
    this.wire.close();
  }

  /** `window_get_fullscreen` answers mid-frame, so the worker needs the value in hand. */
  pushFullscreenChange() {
    const now = this.host.window.isFullscreen();
    if (now === this.fullscreen) return;
    this.fullscreen = now;
    this.wire.notify({ kind: "fullscreen-changed", enabled: now });
  }

  /** What the host sent on each loopback connection since the last frame. */
  relayFromHost() {
    for (const [id, connection] of this.connections) {
      const bytes = connection.receive();
      if (bytes.length > 0) {
        const buffer = bytes.slice().buffer;
        this.wire.notify({ kind: "net-data", id, bytes: buffer }, [buffer]);
      }
      if (connection.closed) {
        this.wire.notify({ kind: "net-close", id });
        this.connections.delete(id);
      }
    }
  }
}
