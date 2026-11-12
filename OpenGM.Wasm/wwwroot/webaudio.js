// Web Audio API-backed sound engine, wrapping a single AudioContext -- backs OpenTKShim/AL.cs
// (a namespace-shadowed replacement for the real, native-only OpenTK.Audio.OpenAL bindings).
// Architecture mirrors OpenGMK's own wasm build (gm8emulator/src/game/audio_wasm.rs): Web Audio
// has no native pause or seek for an AudioBufferSourceNode once started (once stopped, it's gone
// for good), so pause/resume/seek are all implemented the same way there and here -- stop the
// current node (if any), remember the elapsed offset in plain JS bookkeeping, and start a *new*
// node from that offset whenever playback resumes.

import { audioIfInstalled } from "./services.js";

import { makeProxyAudioContext } from "./audio-proxy.js";

let ctx = null;
let globalGain = null;

const buffers = new Map(); // id -> AudioBuffer | null (reserved but not yet filled)
let nextBufferId = 1;

const sources = new Map(); // id -> per-source state, see genSource()
let nextSourceId = 1;

function ensureContext() {
    if (ctx) {
        return ctx;
    }

    const AudioCtor =
        typeof AudioContext !== "undefined" ? AudioContext
        : (typeof self !== "undefined" && self.webkitAudioContext) ? self.webkitAudioContext
        : null;

    if (!AudioCtor) {
        // No Web Audio API in this context -- e.g. a Web Worker, where AudioContext simply doesn't
        // exist. Use a proxy that sends every operation to a real AudioContext wherever the host is
        // (audio-proxy.js, replayed by host/plugin.js on a real context). Everything below runs UNCHANGED
        // against it; only the sound-producing operations actually cross the boundary.
        ctx = makeProxyAudioContext();
        globalGain = ctx.createGain();
        globalGain.connect(ctx.destination);
        return ctx;
    }

    ctx = new AudioCtor();
    globalGain = ctx.createGain();
    globalGain.connect(ctx.destination);

    // Browsers block audio output until a user gesture occurs on the page. Speculatively resuming
    // here is harmless but often insufficient; this listener resumes again on the first real
    // click/keypress and removes itself. Sounds started while still suspended aren't dropped --
    // the Web Audio spec freezes a suspended context's clock, so anything already scheduled via
    // .start() begins playing from the buffer start the instant .resume() actually succeeds.
    const unlock = () => { ctx.resume(); };
    if (typeof document !== "undefined") {
        document.addEventListener('pointerdown', unlock, { once: true });
        document.addEventListener('keydown', unlock, { once: true });
    }
    ctx.resume();

    return ctx;
}

export function init() {
    ensureContext();
}

// ---- buffers ----

export function genBuffer() {
    ensureContext();
    const id = nextBufferId++;
    buffers.set(id, null);
    return id;
}

export function deleteBuffer(bufferId) {
    const buf = buffers.get(bufferId);
    buffers.delete(bufferId);
    // Proxy buffers from audio-proxy.js carry `_id` for the host-side Map; free that copy too so
    // LRU eviction actually releases decoded PCM in the real AudioContext. Absent on the main
    // thread, where this module already holds the real buffers and there is no proxy.
    const device = audioIfInstalled();
    if (buf && typeof buf._id === "number" && device) {
        device.send({ op: "deleteBuffer", id: buf._id });
    }
}

export function bufferData(bufferId, channels, freq, interleavedFloatBytes) {
    ensureContext();
    const interleaved = new Float32Array(interleavedFloatBytes.buffer, interleavedFloatBytes.byteOffset, interleavedFloatBytes.length / 4);
    const frames = Math.max(1, Math.floor(interleaved.length / channels));
    const buf = ctx.createBuffer(channels, frames, freq);
    const channelData = new Float32Array(frames);
    for (let ch = 0; ch < channels; ch++) {
        for (let i = 0; i < frames; i++) {
            channelData[i] = interleaved[(i * channels) + ch] ?? 0;
        }
        buf.copyToChannel(channelData, ch);
    }
    buffers.set(bufferId, buf);
}

export function bufferGetChannels(bufferId) { return buffers.get(bufferId)?.numberOfChannels ?? 0; }
export function bufferGetFrequency(bufferId) { return buffers.get(bufferId)?.sampleRate ?? 0; }
// Web Audio buffers are always native 32-bit float internally, regardless of the source format.
export function bufferGetBits(bufferId) { return buffers.get(bufferId) ? 32 : 0; }
export function bufferGetSizeBytes(bufferId) {
    const b = buffers.get(bufferId);
    return b ? b.length * b.numberOfChannels * 4 : 0;
}

function bufferDurationSecs(bufferId) {
    const b = buffers.get(bufferId);
    return b ? b.length / b.sampleRate : 0;
}

// ---- sources ----
//
// One persistent GainNode (+ StereoPannerNode) per source handle, matching OpenGMK's WavHandle/
// SsSoundState pattern: sound_volume-style live gain changes must retroactively affect whatever's
// currently playing, so the node has to survive across the many AudioBufferSourceNodes a single
// source handle plays over its lifetime (rebinding a buffer and calling SourcePlay again, e.g. from
// AudioManager's channel-limit source-stealing, is a normal, expected reuse pattern here).

function newSourceState() {
    ensureContext();
    const gainNode = ctx.createGain();
    // Real GM8/GMS engines actually expose two independent, coexisting panning mechanisms: the
    // legacy 2D sound_pan (a flat -1..1 stereo knob, StereoPannerNode's own native model) and real
    // 3D positional audio via audio_emitter_position/audio_falloff_set_model (a PannerNode).
    // Chained rather than picking one: a source with no emitter attached leaves pannerNode at its
    // default (0,0,0) position, coincident with the listener's own default (0,0,0) -- equalpower
    // panning at zero distance is centered with no attenuation, so it's a no-op for plain 2D sounds.
    const panNode = ctx.createStereoPanner();
    const pannerNode = ctx.createPanner();
    pannerNode.panningModel = 'equalpower';
    pannerNode.distanceModel = currentDistanceModel;
    gainNode.connect(panNode);
    panNode.connect(pannerNode);
    pannerNode.connect(globalGain);
    return {
        gainNode,
        panNode,
        pannerNode,
        currentNode: null,
        boundBufferId: 0,
        looping: false,
        gain: 1,
        pitch: 1,
        // Where the *next* Play should start from -- set by SourceSetOffset while stopped.
        pendingOffsetSecs: 0,
        startedAtCtxTime: 0,
        startedAtOffsetSecs: 0,
        pausedAtOffsetSecs: null,
        everStarted: false,
    };
}

export function genSource() {
    const id = nextSourceId++;
    sources.set(id, newSourceState());
    return id;
}

export function deleteSource(sourceId) {
    const s = sources.get(sourceId);
    if (!s) return;
    if (s.currentNode) {
        try { s.currentNode.stop(); } catch { /* already stopped */ }
    }
    s.gainNode.disconnect();
    s.panNode.disconnect();
    s.pannerNode.disconnect();
    sources.delete(sourceId);
}

export function sourceSetBufferi(sourceId, bufferId) {
    const s = sources.get(sourceId);
    if (s) s.boundBufferId = bufferId;
}

export function sourceGetBufferi(sourceId) {
    return sources.get(sourceId)?.boundBufferId ?? 0;
}

export function sourceSetLooping(sourceId, looping) {
    const s = sources.get(sourceId);
    if (!s) return;
    s.looping = !!looping;
    if (s.currentNode) s.currentNode.loop = s.looping;
}

export function sourceSetGain(sourceId, gain) {
    const s = sources.get(sourceId);
    if (!s) return;
    s.gain = gain;
    s.gainNode.gain.value = gain;
}
export function sourceGetGain(sourceId) { return sources.get(sourceId)?.gain ?? 0; }

// GML exposes this as a single scalar (sound_pan / not a stereo-field concept), matching
// StereoPannerNode.pan's own -1 (left) .. 1 (right) range directly, no scaling needed.
export function sourceSetPan(sourceId, pan) {
    const s = sources.get(sourceId);
    if (s) s.panNode.pan.value = pan;
}
export function sourceGetPan(sourceId) { return sources.get(sourceId)?.panNode.pan.value ?? 0; }

function currentOffsetSecs(s) {
    if (s.pausedAtOffsetSecs !== null) {
        return s.pausedAtOffsetSecs;
    }
    if (s.currentNode) {
        const elapsed = (ctx.currentTime - s.startedAtCtxTime) * s.pitch;
        const raw = s.startedAtOffsetSecs + elapsed;
        const duration = bufferDurationSecs(s.boundBufferId);
        if (duration <= 0) return 0;
        // A looping source keeps playing past its own duration (wrapping natively inside Web
        // Audio) -- wrap here too so position queries stay meaningful past the first loop.
        return s.looping ? raw % duration : Math.min(raw, duration);
    }
    return s.startedAtOffsetSecs;
}

// Re-anchors position bookkeeping to "now" before a rate change -- the elapsed-time math above
// assumes a constant pitch since startedAtCtxTime, so it must be re-based before pitch itself
// changes, not after (mirrors OpenGMK's ss_set_sound_freq/fmod_set_playback_rate).
function reanchor(s) {
    if (s.currentNode && s.pausedAtOffsetSecs === null) {
        s.startedAtOffsetSecs = currentOffsetSecs(s);
        s.startedAtCtxTime = ctx.currentTime;
    }
}

export function sourceSetPitch(sourceId, pitch) {
    const s = sources.get(sourceId);
    if (!s) return;
    reanchor(s);
    s.pitch = pitch;
    if (s.currentNode) s.currentNode.playbackRate.value = pitch;
}
export function sourceGetPitch(sourceId) { return sources.get(sourceId)?.pitch ?? 1; }

// AL_SEC_OFFSET doubles as both "seek" (while playing/paused) and "where Play should start from"
// (while stopped) in the real OpenAL semantics AudioManager.cs relies on.
export function sourceSetOffset(sourceId, secs) {
    const s = sources.get(sourceId);
    if (!s) return;
    if (s.pausedAtOffsetSecs !== null) {
        s.pausedAtOffsetSecs = secs;
    } else if (s.currentNode) {
        startNode(sourceId, secs);
    } else {
        s.pendingOffsetSecs = secs;
    }
}
export function sourceGetOffset(sourceId) {
    const s = sources.get(sourceId);
    return s ? currentOffsetSecs(s) : 0;
}

export function sourceSetFalloff(sourceId, which, value) {
    const s = sources.get(sourceId);
    if (!s) return;
    // Real OpenAL member names (see ALEnums.cs/AL.cs) map fairly directly onto PannerNode's own
    // inverse-distance-model parameters.
    if (which === 'ReferenceDistance') s.pannerNode.refDistance = Math.max(0.0001, value);
    else if (which === 'MaxDistance') s.pannerNode.maxDistance = Math.max(0.0001, value);
    else if (which === 'RolloffFactor') s.pannerNode.rolloffFactor = Math.max(0, value);
}
export function sourceSetPosition(sourceId, x, y, z) {
    const s = sources.get(sourceId);
    if (!s) return;
    const p = s.pannerNode;
    if (p.positionX) { p.positionX.value = x; p.positionY.value = y; p.positionZ.value = z; }
    else if (p.setPosition) p.setPosition(x, y, z); // older Safari
}
export function sourceSetVelocity(_sourceId, _x, _y, _z) {
    // Web Audio's PannerNode has no native Doppler/velocity support (removed from the spec) --
    // GM's audio_emitter_velocity is tracked nowhere here, matching that platform limitation.
}
export function sourceSetCone(sourceId, innerAngle, outerAngle, outerGain) {
    const s = sources.get(sourceId);
    if (!s) return;
    const p = s.pannerNode;
    p.coneInnerAngle = innerAngle;
    p.coneOuterAngle = outerAngle;
    p.coneOuterGain = Math.max(0, Math.min(1, outerGain));
}
export function sourceSetOrientation(sourceId, x, y, z) {
    const s = sources.get(sourceId);
    if (!s) return;
    const p = s.pannerNode;
    if (p.orientationX) { p.orientationX.value = x; p.orientationY.value = y; p.orientationZ.value = z; }
    else if (p.setOrientation) p.setOrientation(x, y, z);
}

const DISTANCE_MODEL_MAP = {
    None: 'linear',
    InverseDistance: 'inverse',
    InverseDistanceClamped: 'inverse',
    LinearDistance: 'linear',
    LinearDistanceClamped: 'linear',
    ExponentDistance: 'exponential',
    ExponentDistanceClamped: 'exponential',
};
let currentDistanceModel = 'inverse';
export function setDistanceModel(alModelName) {
    currentDistanceModel = DISTANCE_MODEL_MAP[alModelName] ?? 'inverse';
    for (const s of sources.values()) {
        s.pannerNode.distanceModel = currentDistanceModel;
    }
}

function startNode(sourceId, offsetSecs) {
    const s = sources.get(sourceId);
    if (!s) return;
    if (s.currentNode) {
        try { s.currentNode.stop(); } catch { /* already stopped */ }
        s.currentNode = null;
    }
    const buf = buffers.get(s.boundBufferId);
    if (!buf) return;

    const node = ctx.createBufferSource();
    node.buffer = buf;
    node.loop = s.looping;
    node.playbackRate.value = s.pitch;
    node.connect(s.gainNode);

    const duration = buf.length / buf.sampleRate;
    const startOffset = duration > 0 ? Math.max(0, offsetSecs || 0) % duration : 0;
    node.start(0, startOffset);
    node.onended = () => {
        if (s.currentNode === node) {
            s.currentNode = null;
        }
    };

    s.currentNode = node;
    s.startedAtCtxTime = ctx.currentTime;
    s.startedAtOffsetSecs = startOffset;
    s.pausedAtOffsetSecs = null;
    s.everStarted = true;
}

export function sourcePlay(sourceId) {
    const s = sources.get(sourceId);
    if (!s) return;
    const resumeAt = s.pausedAtOffsetSecs !== null ? s.pausedAtOffsetSecs : s.pendingOffsetSecs;
    startNode(sourceId, resumeAt);
    s.pendingOffsetSecs = 0;
}

export function sourceStop(sourceId) {
    const s = sources.get(sourceId);
    if (!s) return;
    if (s.currentNode) {
        try { s.currentNode.stop(); } catch { /* already stopped */ }
        s.currentNode = null;
    }
    s.pausedAtOffsetSecs = null;
    s.startedAtOffsetSecs = 0;
    s.pendingOffsetSecs = 0;
}

export function sourcePause(sourceId) {
    const s = sources.get(sourceId);
    if (!s || s.pausedAtOffsetSecs !== null || !s.currentNode) return;
    // Must read the elapsed position before tearing down currentNode -- currentOffsetSecs falls
    // back to startedAtOffsetSecs once there's no live node, which would report the wrong (start-
    // of-playback) position instead of wherever it actually was when paused.
    const pos = currentOffsetSecs(s);
    try { s.currentNode.stop(); } catch { /* already stopped */ }
    s.currentNode = null;
    s.pausedAtOffsetSecs = pos;
}

// 0 = Initial (never played), 1 = Playing, 2 = Paused, 3 = Stopped -- matches OpenTKShim/
// ALEnums.cs's ALSourceState ordering exactly (AudioManager.Update() polls this to notice when a
// non-looping sound has finished and reclaim its source).
export function sourceGetState(sourceId) {
    const s = sources.get(sourceId);
    if (!s) return 0;
    if (s.pausedAtOffsetSecs !== null) return 2;
    if (s.currentNode) {
        const duration = bufferDurationSecs(s.boundBufferId);
        if (s.looping || duration <= 0) return 1;
        return currentOffsetSecs(s) < duration ? 1 : 3;
    }
    return s.everStarted ? 3 : 0;
}

export function stopAll() {
    for (const id of sources.keys()) sourceStop(id);
}

// ---- listener ----

export function listenerSetGain(gain) {
    ensureContext();
    globalGain.gain.value = gain;
}
export function listenerGetGain() {
    return globalGain ? globalGain.gain.value : 1;
}
export function listenerSetPosition(x, y, z) {
    ensureContext();
    const l = ctx.listener;
    if (l.positionX) { l.positionX.value = x; l.positionY.value = y; l.positionZ.value = z; }
    else if (l.setPosition) l.setPosition(x, y, z); // older Safari
}
// No native Doppler/velocity support in the Web Audio listener either -- see sourceSetVelocity's
// own doc comment, same platform limitation.
export function listenerSetVelocity(_x, _y, _z) { }
export function listenerSetOrientation(atX, atY, atZ, upX, upY, upZ) {
    ensureContext();
    const l = ctx.listener;
    if (l.forwardX) {
        l.forwardX.value = atX; l.forwardY.value = atY; l.forwardZ.value = atZ;
        l.upX.value = upX; l.upY.value = upY; l.upZ.value = upZ;
    } else if (l.setOrientation) {
        l.setOrientation(atX, atY, atZ, upX, upY, upZ); // older Safari
    }
}

// ---- legacy sound_effect_* (DirectSound-era effect chains) ----
//
// Real DirectSound effect algorithms aren't replicable exactly (closed, hardware/driver-specific
// implementations) -- these are reasonable Web Audio approximations of the same effect *category*
// (a modulated short delay for chorus/flanger, a feedback delay for echo, amplitude modulation for
// gargle, a synthesized decaying-noise impulse response for reverb), not bit-exact ports. Config is
// stored per sound *asset* index (matching sound_volume/sound_pan's own convention) and applied
// once, at the moment a new instance of that asset starts playing (applySoundEffects, called from
// AL.cs's Source(..., ALSourcei.Buffer, ...) -- see AudioManager.audio_play_sound/
// PlaySoundOnEmitter) -- already-playing instances keep whatever chain they started with.

const SE_CHORUS = 1, SE_ECHO = 2, SE_FLANGER = 4, SE_GARGLE = 8, SE_REVERB = 16, SE_COMPRESSOR = 32, SE_EQUALIZER = 64;

const soundEffects = new Map(); // assetIndex -> { mask, chorus, compressor, echo, equalizer, flanger, gargle, reverb }

function effectConfig(assetIndex) {
    let cfg = soundEffects.get(assetIndex);
    if (!cfg) {
        cfg = {
            mask: 0,
            chorus: { wetDryMix: 50, depth: 50, feedback: 0, frequency: 1, waveform: 1 },
            compressor: { gain: 0, attack: 10, release: 100, threshold: -24, ratio: 3 },
            echo: { wetDryMix: 50, feedback: 50, leftDelay: 500, rightDelay: 500 },
            equalizer: { center: 1000, bandwidth: 1000, gain: 0 },
            flanger: { wetDryMix: 50, depth: 50, feedback: 0, frequency: 1, waveform: 1 },
            gargle: { rate: 20, waveShape: 0 },
            reverb: { gain: 0, mix: 30, time: 1000, ratio: 2 },
        };
        soundEffects.set(assetIndex, cfg);
    }
    return cfg;
}

export function effectSetMask(assetIndex, mask) { effectConfig(assetIndex).mask = mask; }
export function effectSetChorus(assetIndex, wetDryMix, depth, feedback, frequency, waveform) {
    effectConfig(assetIndex).chorus = { wetDryMix, depth, feedback, frequency, waveform };
}
export function effectSetFlanger(assetIndex, wetDryMix, depth, feedback, frequency, waveform) {
    effectConfig(assetIndex).flanger = { wetDryMix, depth, feedback, frequency, waveform };
}
export function effectSetEcho(assetIndex, wetDryMix, feedback, leftDelay, rightDelay) {
    effectConfig(assetIndex).echo = { wetDryMix, feedback, leftDelay, rightDelay };
}
export function effectSetCompressor(assetIndex, gain, attack, release, threshold, ratio) {
    effectConfig(assetIndex).compressor = { gain, attack, release, threshold, ratio };
}
export function effectSetEqualizer(assetIndex, center, bandwidth, gain) {
    effectConfig(assetIndex).equalizer = { center, bandwidth, gain };
}
export function effectSetGargle(assetIndex, rate, waveShape) {
    effectConfig(assetIndex).gargle = { rate, waveShape };
}
export function effectSetReverb(assetIndex, gain, mix, time, ratio) {
    effectConfig(assetIndex).reverb = { gain, mix, time, ratio };
}

function appendEqualizer(input, p, chain) {
    const filter = ctx.createBiquadFilter();
    filter.type = 'peaking';
    filter.frequency.value = Math.max(20, p.center);
    filter.Q.value = p.bandwidth > 0 ? p.center / Math.max(1, p.bandwidth) : 1;
    filter.gain.value = p.gain;
    input.connect(filter);
    chain.push(filter);
    return filter;
}

function appendCompressor(input, p, chain) {
    const comp = ctx.createDynamicsCompressor();
    comp.threshold.value = p.threshold;
    comp.ratio.value = Math.max(1, p.ratio);
    comp.attack.value = Math.max(0, p.attack / 1000);
    comp.release.value = Math.max(0, p.release / 1000);
    const makeupGain = ctx.createGain();
    makeupGain.gain.value = Math.pow(10, p.gain / 20);
    input.connect(comp);
    comp.connect(makeupGain);
    chain.push(comp, makeupGain);
    return makeupGain;
}

// Shared by chorus and flanger -- both are, DSP-wise, an LFO-modulated short delay line mixed back
// with the dry signal; they differ only in typical delay depth (flanger uses a much shorter base
// delay) and how much feedback a game typically dials in, not in the underlying technique.
function appendModulatedDelay(input, p, chain, baseDelaySecs) {
    const delayNode = ctx.createDelay(1.0);
    delayNode.delayTime.value = baseDelaySecs;

    const lfo = ctx.createOscillator();
    lfo.type = p.waveform === 0 ? 'triangle' : 'sine';
    lfo.frequency.value = Math.max(0.01, p.frequency);
    const lfoDepth = ctx.createGain();
    lfoDepth.gain.value = baseDelaySecs * Math.max(0, Math.min(100, p.depth)) / 100 * 0.9;
    lfo.connect(lfoDepth);
    lfoDepth.connect(delayNode.delayTime);
    lfo.start();

    const feedback = ctx.createGain();
    feedback.gain.value = Math.max(-0.95, Math.min(0.95, p.feedback / 100));
    delayNode.connect(feedback);
    feedback.connect(delayNode);

    const wet = ctx.createGain();
    const dry = ctx.createGain();
    const mix = Math.max(0, Math.min(100, p.wetDryMix)) / 100;
    wet.gain.value = mix;
    dry.gain.value = 1 - mix;
    const output = ctx.createGain();

    input.connect(dry); dry.connect(output);
    input.connect(delayNode); delayNode.connect(wet); wet.connect(output);

    chain.push(delayNode, lfo, lfoDepth, feedback, wet, dry, output);
    return output;
}

function appendEcho(input, p, chain) {
    const delayNode = ctx.createDelay(2.0);
    delayNode.delayTime.value = Math.max(0.001, ((p.leftDelay + p.rightDelay) / 2) / 1000);

    const feedback = ctx.createGain();
    feedback.gain.value = Math.max(0, Math.min(0.95, p.feedback / 100));
    delayNode.connect(feedback);
    feedback.connect(delayNode);

    const wet = ctx.createGain();
    const dry = ctx.createGain();
    const mix = Math.max(0, Math.min(100, p.wetDryMix)) / 100;
    wet.gain.value = mix;
    dry.gain.value = 1 - mix;
    const output = ctx.createGain();

    input.connect(dry); dry.connect(output);
    input.connect(delayNode); delayNode.connect(wet); wet.connect(output);

    chain.push(delayNode, feedback, wet, dry, output);
    return output;
}

// Amplitude-modulates the signal between 0 and 1 -- an AudioParam's effective value is its own
// baseline plus whatever's connected into it, so a 0.5 baseline GainNode with a +-0.5 oscillator
// signal summed into its own .gain param oscillates cleanly between 0 and 1 with no extra nodes.
function appendGargle(input, p, chain) {
    const osc = ctx.createOscillator();
    osc.type = p.waveShape === 0 ? 'triangle' : 'square';
    osc.frequency.value = Math.max(1, p.rate);

    const depthGain = ctx.createGain();
    depthGain.gain.value = 0.5;

    const modulated = ctx.createGain();
    modulated.gain.value = 0.5;

    osc.connect(depthGain);
    depthGain.connect(modulated.gain);
    osc.start();

    input.connect(modulated);
    chain.push(osc, depthGain, modulated);
    return modulated;
}

function makeReverbImpulse(durationSecs, decay) {
    const rate = ctx.sampleRate;
    const length = Math.max(1, Math.floor(rate * durationSecs));
    const impulse = ctx.createBuffer(2, length, rate);
    for (let ch = 0; ch < 2; ch++) {
        const data = impulse.getChannelData(ch);
        for (let i = 0; i < length; i++) {
            data[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / length, decay);
        }
    }
    return impulse;
}

function appendReverb(input, p, chain) {
    const convolver = ctx.createConvolver();
    convolver.normalize = true;
    const durationSecs = Math.max(0.05, Math.min(5, p.time / 1000));
    convolver.buffer = makeReverbImpulse(durationSecs, Math.max(0.1, p.ratio));

    const makeupGain = ctx.createGain();
    makeupGain.gain.value = Math.pow(10, p.gain / 20);

    const wet = ctx.createGain();
    const dry = ctx.createGain();
    const mix = Math.max(0, Math.min(100, p.mix)) / 100;
    wet.gain.value = mix;
    dry.gain.value = 1 - mix;
    const output = ctx.createGain();

    input.connect(dry); dry.connect(output);
    input.connect(convolver); convolver.connect(makeupGain); makeupGain.connect(wet); wet.connect(output);

    chain.push(convolver, makeupGain, wet, dry, output);
    return output;
}

// Rebuilds this source's effect chain (panNode -> [active effects in fixed order] -> pannerNode)
// from whichever sound_effect_* config is currently set for assetIndex. Called once, right when a
// new instance starts playing (see this section's own doc comment) -- effects already wired into a
// still-playing instance are left alone even if sound_effect_set changes afterward, matching a
// live DirectSound secondary buffer's own effect chain being fixed at creation.
export function applySoundEffects(sourceId, assetIndex) {
    const s = sources.get(sourceId);
    if (!s) return;

    if (s.effectNodes) {
        for (const n of s.effectNodes) {
            try { n.disconnect(); } catch { /* already disconnected */ }
        }
    }
    try { s.panNode.disconnect(); } catch { /* already disconnected */ }

    const cfg = soundEffects.get(assetIndex);
    const chain = [];
    let output = s.panNode;
    if (cfg && cfg.mask !== 0) {
        if (cfg.mask & SE_EQUALIZER) output = appendEqualizer(output, cfg.equalizer, chain);
        if (cfg.mask & SE_COMPRESSOR) output = appendCompressor(output, cfg.compressor, chain);
        if (cfg.mask & SE_CHORUS) output = appendModulatedDelay(output, cfg.chorus, chain, 0.02);
        if (cfg.mask & SE_FLANGER) output = appendModulatedDelay(output, cfg.flanger, chain, 0.003);
        if (cfg.mask & SE_ECHO) output = appendEcho(output, cfg.echo, chain);
        if (cfg.mask & SE_GARGLE) output = appendGargle(output, cfg.gargle, chain);
        if (cfg.mask & SE_REVERB) output = appendReverb(output, cfg.reverb, chain);
    }
    output.connect(s.pannerNode);
    s.effectNodes = chain;
}

// ---- capture/recording ----
//
// Out of scope for this pass (see AL.cs's doc comment) -- reporting zero devices keeps
// SoundFunctions.cs's recorder-count/list functions safely returning "none available" rather than
// ever prompting for microphone access.
export function getCaptureDeviceNames() {
    return [];
}
