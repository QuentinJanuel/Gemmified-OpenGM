// Worker-side proxy AudioContext for the relocated GMS engine. The Web Audio API is main-thread
// only, but the engine runs in a worker -- so this stands in for a real AudioContext and sends
// every node/param/buffer operation to the host, where a real context executes it (host/plugin.js).
// Crucially, webaudio.js is left completely UNCHANGED: all of its bookkeeping,
// timing (via ctx.currentTime, backed here by a worker clock), and effect-graph construction run in
// the worker as before, and its synchronous getters keep reading local state, so C# still gets
// inline answers. Only the actual sound-producing operations cross to the main thread.

import { audio, publishAudioEnded } from "./services.js";

let nextId = 1;
const endedCallbacks = new Map(); // node id -> onended fn (fired when the main-thread node ends)

// AudioParams webaudio.js touches, plus a couple harmless extras. Node ids -1 = listener, 0 = dest.
const PARAMS = [
  "gain", "pan", "delayTime", "frequency", "Q", "threshold", "ratio", "attack", "release", "knee",
  "detune", "playbackRate", "positionX", "positionY", "positionZ",
  "orientationX", "orientationY", "orientationZ", "forwardX", "forwardY", "forwardZ", "upX", "upY", "upZ",
];
// Plain-value node properties (as opposed to AudioParams).
const PROPS = [
  "loop", "type", "normalize", "panningModel", "distanceModel", "refDistance", "maxDistance",
  "rolloffFactor", "coneInnerAngle", "coneOuterAngle", "coneOuterGain",
];

// The host's audio device (services.js): one `send` per operation that actually produces sound,
// fire-and-forget.
function post(cmd) {
  audio().send(cmd);
}

function makeParam(id, name) {
  let value = 0;
  const set = (x) => {
    value = x;
    post({ op: "param", id, param: name, value: x });
  };
  return {
    get value() {
      return value;
    },
    set value(x) {
      set(x);
    },
    setValueAtTime: set,
    linearRampToValueAtTime: set,
    setTargetAtTime() {},
    cancelScheduledValues() {},
  };
}

function makeNode(id) {
  const node = {
    _id: id,
    connect(target) {
      post({ op: "connect", id, target: target?._id ?? 0 });
    },
    disconnect() {
      post({ op: "disconnect", id });
    },
    start(when, offset) {
      post({ op: "start", id, when: when ?? 0, offset: offset ?? 0 });
    },
    stop() {
      post({ op: "stop", id });
    },
  };
  for (const p of PARAMS) node[p] = makeParam(id, p);

  const propStore = {};
  for (const prop of PROPS) {
    Object.defineProperty(node, prop, {
      get: () => propStore[prop],
      set: (v) => {
        propStore[prop] = v;
        post({ op: "prop", id, prop, value: v });
      },
    });
  }
  // Binding a buffer: make sure any getChannelData-written data is flushed, then reference it by id.
  Object.defineProperty(node, "buffer", {
    get: () => propStore.buffer,
    set: (buf) => {
      propStore.buffer = buf;
      if (buf) {
        buf._flush();
        post({ op: "setBuffer", id, bufferId: buf._id });
      }
    },
  });
  // The real node fires onended wherever the host is; the host reports it back and the worker
  // routes it here (see `publishAudioEnded` in services.js), invoking the stored callback.
  Object.defineProperty(node, "onended", {
    get: () => endedCallbacks.get(id),
    set: (cb) => {
      if (cb) {
        endedCallbacks.set(id, cb);
        post({ op: "onended", id });
      } else {
        endedCallbacks.delete(id);
      }
    },
  });
  return node;
}

function makeListener() {
  const l = {};
  // id -1 routes to ctx.listener on the backend; webaudio.js only ever uses the AudioParam form.
  for (const p of ["positionX", "positionY", "positionZ", "forwardX", "forwardY", "forwardZ", "upX", "upY", "upZ"]) {
    l[p] = makeParam(-1, p);
  }
  return l;
}

function makeBuffer(channels, frames, rate) {
  const id = nextId++;
  post({ op: "createBuffer", id, channels, frames, rate });
  const data = new Array(channels);
  const dirty = new Set();
  return {
    _id: id,
    numberOfChannels: channels,
    length: frames,
    sampleRate: rate,
    copyToChannel(src, ch) {
      // webaudio.js reuses one scratch array across channels, so snapshot it now.
      const copy = src.slice();
      data[ch] = copy;
      post({ op: "bufferData", id, ch, data: copy });
    },
    getChannelData(ch) {
      if (!data[ch]) data[ch] = new Float32Array(frames);
      dirty.add(ch);
      return data[ch];
    },
    // Send any channels written via getChannelData (e.g. the reverb impulse) before this buffer is used.
    _flush() {
      for (const ch of dirty) post({ op: "bufferData", id, ch, data: data[ch] });
      dirty.clear();
    },
  };
}

function createNode(type, args) {
  const id = nextId++;
  post({ op: "create", id, type, args: args ?? [] });
  return makeNode(id);
}

export function makeProxyAudioContext() {
  const clock = typeof performance !== "undefined" ? performance : Date;
  const clockStart = clock.now();

  // Routed to by opengm-worker.js when the backend reports a node has ended.
  publishAudioEnded((id) => {
    const cb = endedCallbacks.get(id);
    if (cb) cb();
  });

  return {
    sampleRate: 44100,
    get currentTime() {
      return (clock.now() - clockStart) / 1000;
    },
    destination: makeNode(0),
    listener: makeListener(),
    createGain: () => createNode("gain"),
    createStereoPanner: () => createNode("stereoPanner"),
    createPanner: () => createNode("panner"),
    createBufferSource: () => createNode("bufferSource"),
    createBiquadFilter: () => createNode("biquadFilter"),
    createDynamicsCompressor: () => createNode("dynamicsCompressor"),
    createDelay: (max) => createNode("delay", [max]),
    createOscillator: () => createNode("oscillator"),
    createConvolver: () => createNode("convolver"),
    createBuffer: (channels, frames, rate) => makeBuffer(channels, frames, rate),
    resume: () => post({ op: "resume" }),
    suspend: () => post({ op: "suspend" }),
  };
}
