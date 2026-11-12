// Backs OpenTKShim/GL.cs's [JSImport] calls with a real WebGL2RenderingContext. OpenGM's own
// rendering code (CustomWindow.cs, SurfaceManager.cs, PageManager.cs, ShaderManager.cs,
// GraphicsManager.cs) never changes to know about this file -- it's compiled unmodified against
// GL.cs, which is the only thing that talks to this module.
//
// GL object handles (textures/buffers/framebuffers/programs/shaders/vaos/uniform locations) are
// all plain WebGL JS objects, but GL.cs needs plain ints to pass around (that's the type OpenGM's
// own code already uses everywhere) -- `handles` is a single shared slot table doing that mapping;
// index 0 is reserved as "null" so that id 0 means "unbound"/"default framebuffer", matching real
// GL's own convention for those cases.

import { canvas as hostCanvas, surface } from "./services.js";

// When the engine runs in a Web Worker (the GMS relocation, so its GML dialogs can block without
// freezing the UI), the worker hands an OffscreenCanvas over through services.js before boot; on
// the main thread we fall back to the DOM canvas as before. Both expose the same WebGL2 surface.
const inWorker = typeof document === "undefined";
const canvas = inWorker ? hostCanvas() : document.getElementById("canvas");
const gl = canvas.getContext("webgl2");
if (!gl) {
    throw new Error("WebGL2 is not available in this browser");
}

// The canvas's real drawing-buffer size (its width/height *attributes*, not the CSS box it's
// displayed at -- those stay independently controlled by index.html's max-width:100%/height:auto)
// starts out at whatever index.html's static width/height attributes say, which is just a
// placeholder. WasmEntry.RunLoadGame calls this once the real game's resolution is known (every
// load, not just the first, since more than one game can be loaded per page session and each can
// have a different native resolution) -- without it, GL.Viewport gets set to the game's true size
// while the canvas's actual backing buffer stays at the placeholder size, silently clipping
// anything outside it (e.g. Indigo Needle's real 800x608 render cropped against an 800x600 canvas).
// Resizing a canvas's width/height after its WebGL context already exists is safe and standard --
// the context keeps working, it just starts drawing into the new size.
export function resizeCanvas(width, height) {
    canvas.width = width;
    canvas.height = height;
    if (inWorker) {
        // The visible element lives wherever the host is; it owns the display box (and its
        // aspect-ratio styling below). Reporting the game's real resolution through the surface is
        // what lets it size itself -- see `Surface.reportSize`.
        surface().reportSize(width, height);
        return;
    }
    // Belt-and-suspenders alongside the width/height attributes above: index.html's #canvas rule
    // (max-width:100%; height:auto) is supposed to derive the displayed box from those attributes'
    // ratio on its own, but in windowed (non-fullscreen) mode that fell through to filling the
    // wrapper's full width while keeping something closer to the *old* height, squashing the game
    // instead of showing it at its real proportions -- fullscreen was unaffected since its own CSS
    // rule (object-fit: contain) doesn't depend on that derivation at all. Setting aspect-ratio
    // directly removes the ambiguity rather than relying on the browser to infer it correctly after
    // a dynamic, post-layout width/height change.
    canvas.style.aspectRatio = `${width} / ${height}`;
}

const handles = [null];
function alloc(obj) {
    handles.push(obj);
    return handles.length - 1;
}
function h(id) {
    return handles[id] ?? null;
}

const textureSize = new Map(); // id -> {w, h}
const fbColorTexture = new Map(); // framebuffer id -> texture id
let currentFramebufferId = 0;

const scratchFbo = gl.createFramebuffer();

// ---- textures ----

export function createTexture() {
    return alloc(gl.createTexture());
}

export function textureStorage2D(id, levels, width, height) {
    gl.bindTexture(gl.TEXTURE_2D, h(id));
    gl.texStorage2D(gl.TEXTURE_2D, levels, gl.RGBA8, width, height);
    textureSize.set(id, { w: width, h: height });
}

export function textureSubImage2DBytes(id, level, xoffset, yoffset, width, height, pixels) {
    gl.bindTexture(gl.TEXTURE_2D, h(id));
    gl.texSubImage2D(gl.TEXTURE_2D, level, xoffset, yoffset, width, height, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(pixels));
}

export function textureParameter(id, pname, param) {
    gl.bindTexture(gl.TEXTURE_2D, h(id));
    // param is the C# enum's ordinal (see OpenTKShim/GLEnums.cs).
    switch (pname) {
        case "TextureMinFilter":
            // 0 = Nearest, 1 = Linear -- backs texture_set_interpolation's texel sampling toggle.
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, param === 1 ? gl.LINEAR : gl.NEAREST);
            break;
        case "TextureMagFilter":
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, param === 1 ? gl.LINEAR : gl.NEAREST);
            break;
        case "TextureWrapS":
        case "TextureWrapT":
            // 0 = Repeat, 1 = ClampToEdge -- gpu_set_tex_repeat. Note WebGL2 only permits REPEAT on
            // power-of-two textures for some formats; texture pages are POT, so this is safe here.
            gl.texParameteri(gl.TEXTURE_2D,
                pname === "TextureWrapS" ? gl.TEXTURE_WRAP_S : gl.TEXTURE_WRAP_T,
                param === 0 ? gl.REPEAT : gl.CLAMP_TO_EDGE);
            break;
    }
}

export function bindTextureUnit(unit, id) {
    gl.activeTexture(gl.TEXTURE0 + unit);
    gl.bindTexture(gl.TEXTURE_2D, h(id));
}

export function bindTexture(id) {
    gl.bindTexture(gl.TEXTURE_2D, h(id));
}

export function deleteTexture(id) {
    gl.deleteTexture(h(id));
    handles[id] = null;
    textureSize.delete(id);
}

export function getTextureWidth(id) {
    return textureSize.get(id)?.w ?? 0;
}

export function getTextureHeight(id) {
    return textureSize.get(id)?.h ?? 0;
}

export function getTextureSubImage(id, x, y, width, height) {
    const prevFb = currentFramebufferId;
    gl.bindFramebuffer(gl.FRAMEBUFFER, scratchFbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, h(id), 0);
    const buf = new Uint8Array(width * height * 4);
    gl.readPixels(x, y, width, height, gl.RGBA, gl.UNSIGNED_BYTE, buf);
    gl.bindFramebuffer(gl.FRAMEBUFFER, h(prevFb));
    return buf;
}

// ---- buffers / vertex arrays ----

export function genVertexArray() {
    return alloc(gl.createVertexArray());
}

export function genBuffer() {
    return alloc(gl.createBuffer());
}

export function bindVertexArray(id) {
    gl.bindVertexArray(h(id));
}

export function bindBuffer(id) {
    gl.bindBuffer(gl.ARRAY_BUFFER, h(id));
}

export function vertexAttribPointer(index, size, normalized, stride, offset) {
    gl.vertexAttribPointer(index, size, gl.FLOAT, normalized, stride, offset);
}

export function enableVertexAttribArray(index) {
    gl.enableVertexAttribArray(index);
}

export function bufferDataBytes(data) {
    gl.bufferData(gl.ARRAY_BUFFER, new Uint8Array(data), gl.STREAM_DRAW);
}

export function drawArrays(mode, first, count) {
    // `mode` is already the real GL enum int (see GL.cs's PrimitiveType/DrawArrays).
    gl.drawArrays(mode, first, count);
}

// ---- viewport / clear ----

export function viewport(x, y, w, h2) {
    gl.viewport(x, y, w, h2);
}

export function clearColor(r, g, b, a) {
    gl.clearColor(r, g, b, a);
}

export function clear(_mask) {
    gl.clear(gl.COLOR_BUFFER_BIT);
}

// ---- state ----

// Capability names come across as the C# enum's ToString(), so this table is the whole mapping.
// Anything not listed is a desktop-GL-only capability with no WebGL2 equivalent (the debug-output
// ones), and is silently ignored rather than throwing.
const caps = {
    Blend: 0x0BE2,      // GL_BLEND
    DepthTest: 0x0B71,  // GL_DEPTH_TEST
    CullFace: 0x0B44,   // GL_CULL_FACE
};

export function enable(cap) {
    const c = caps[cap];
    if (c !== undefined) gl.enable(c);
}

export function disable(cap) {
    const c = caps[cap];
    if (c !== undefined) gl.disable(c);
}

// ---- depth / culling ----
// Needed by the d3d_* family and gpu_set_ztestenable/_zwriteenable/_zfunc/_cullmode. WebGL2 has all
// of these natively; they were simply never wired up while the renderer was 2D-only.

export function depthMask(flag) {
    gl.depthMask(flag);
}

export function depthFunc(func) {
    // Already the real GL enum int -- see GL.cs's DepthFunction, whose members carry their GL values.
    gl.depthFunc(func);
}

export function cullFace(mode) {
    gl.cullFace(mode);
}

export function frontFace(mode) {
    gl.frontFace(mode);
}

const blendFactors = {
    Zero: 0, One: 1,
    SrcColor: 0x0300, OneMinusSrcColor: 0x0301, SrcAlpha: 0x0302, OneMinusSrcAlpha: 0x0303,
    DstAlpha: 0x0304, OneMinusDstAlpha: 0x0305, DstColor: 0x0306, OneMinusDstColor: 0x0307,
    SrcAlphaSaturate: 0x0308,
};
export function blendFunc(src, dst) {
    gl.blendFunc(blendFactors[src] ?? gl.ONE, blendFactors[dst] ?? gl.ZERO);
}

export function blendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha) {
    gl.blendFuncSeparate(
        blendFactors[srcRgb] ?? gl.ONE, blendFactors[dstRgb] ?? gl.ZERO,
        blendFactors[srcAlpha] ?? gl.ONE, blendFactors[dstAlpha] ?? gl.ZERO);
}

const blendEquations = { FuncAdd: 0x8006, FuncSubtract: 0x800a, FuncReverseSubtract: 0x800b, Min: 0x8007, Max: 0x8008 };
export function blendEquation(mode) {
    gl.blendEquation(blendEquations[mode] ?? gl.FUNC_ADD);
}

export function colorMask(r, g, b, a) {
    gl.colorMask(r, g, b, a);
}

export function getFramebufferBinding() {
    return currentFramebufferId;
}

export function getGlString(name) {
    return name === "ShadingLanguageVersion" ? gl.getParameter(gl.SHADING_LANGUAGE_VERSION) : gl.getParameter(gl.VERSION);
}

// ---- shaders / programs ----

export function createShader(type) {
    return alloc(gl.createShader(type === "VertexShader" ? gl.VERTEX_SHADER : gl.FRAGMENT_SHADER));
}

export function shaderSource(id, source) {
    gl.shaderSource(h(id), source);
}

export function compileShader(id) {
    gl.compileShader(h(id));
}

export function getShaderParam(id, pname) {
    if (pname === "CompileStatus") return gl.getShaderParameter(h(id), gl.COMPILE_STATUS) ? 1 : 0;
    return 0;
}

export function getShaderInfoLog(id) {
    return gl.getShaderInfoLog(h(id)) ?? "";
}

export function createProgram() {
    return alloc(gl.createProgram());
}

export function attachShader(programId, shaderId) {
    gl.attachShader(h(programId), h(shaderId));
}

export function linkProgram(id) {
    gl.linkProgram(h(id));
}

export function bindAttribLocation(programId, index, name) {
    gl.bindAttribLocation(h(programId), index, name);
}

export function getProgramParam(id, pname) {
    if (pname === "LinkStatus") return gl.getProgramParameter(h(id), gl.LINK_STATUS) ? 1 : 0;
    if (pname === "ActiveUniforms") return gl.getProgramParameter(h(id), gl.ACTIVE_UNIFORMS);
    if (pname === "ActiveUniformMaxLength") return 256; // unused by GL.cs's GetActiveUniform, any positive value is fine
    return 0;
}

export function getProgramInfoLog(id) {
    return gl.getProgramInfoLog(h(id)) ?? "";
}

export function deleteShader(id) {
    gl.deleteShader(h(id));
    handles[id] = null;
}

export function useProgram(id) {
    gl.useProgram(h(id));
}

export function getUniformLocation(programId, name) {
    const loc = gl.getUniformLocation(h(programId), name);
    return loc === null ? -1 : alloc(loc);
}

export function getActiveUniformName(programId, index) {
    return gl.getActiveUniform(h(programId), index)?.name ?? "";
}

export function getActiveUniformSize(programId, index) {
    return gl.getActiveUniform(h(programId), index)?.size ?? 0;
}

export function getActiveUniformType(programId, index) {
    const type = gl.getActiveUniform(h(programId), index)?.type;
    if (type === gl.SAMPLER_2D) return "sampler2D";
    if (type === gl.SAMPLER_CUBE) return "samplerCube";
    return "other";
}

export function uniform1f(loc, v) { if (loc >= 0) gl.uniform1f(h(loc), v); }
export function uniform1i(loc, v) { if (loc >= 0) gl.uniform1i(h(loc), v); }
export function uniform2f(loc, x, y) { if (loc >= 0) gl.uniform2f(h(loc), x, y); }
export function uniform2i(loc, x, y) { if (loc >= 0) gl.uniform2i(h(loc), x, y); }
export function uniform3f(loc, x, y, z) { if (loc >= 0) gl.uniform3f(h(loc), x, y, z); }
export function uniform3i(loc, x, y, z) { if (loc >= 0) gl.uniform3i(h(loc), x, y, z); }
export function uniform4f(loc, x, y, z, w) { if (loc >= 0) gl.uniform4f(h(loc), x, y, z, w); }
export function uniform4i(loc, x, y, z, w) { if (loc >= 0) gl.uniform4i(h(loc), x, y, z, w); }

export function uniform1fv(loc, bytes) {
    if (loc < 0) return;
    const arr = new Uint8Array(bytes);
    gl.uniform1fv(h(loc), new Float32Array(arr.buffer, arr.byteOffset, arr.byteLength / 4));
}

export function uniform1iv(loc, bytes) {
    if (loc < 0) return;
    const arr = new Uint8Array(bytes);
    gl.uniform1iv(h(loc), new Int32Array(arr.buffer, arr.byteOffset, arr.byteLength / 4));
}

export function uniformMatrix4fvBytes(loc, _count, transpose, bytes) {
    if (loc < 0) return;
    const arr = new Uint8Array(bytes);
    gl.uniformMatrix4fv(h(loc), transpose, new Float32Array(arr.buffer, arr.byteOffset, arr.byteLength / 4));
}

// ---- framebuffers ----

export function bindFramebuffer(id) {
    currentFramebufferId = id;
    gl.bindFramebuffer(gl.FRAMEBUFFER, h(id));
}

export function createFramebuffer() {
    return alloc(gl.createFramebuffer());
}

export function namedFramebufferTexture(fbId, texId, level) {
    const prevFb = currentFramebufferId;
    gl.bindFramebuffer(gl.FRAMEBUFFER, h(fbId));
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, h(texId), level);
    gl.bindFramebuffer(gl.FRAMEBUFFER, h(prevFb));
    currentFramebufferId = prevFb;
    fbColorTexture.set(fbId, texId);
}

export function checkNamedFramebufferStatus(fbId) {
    const prevFb = currentFramebufferId;
    gl.bindFramebuffer(gl.FRAMEBUFFER, h(fbId));
    const ok = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
    gl.bindFramebuffer(gl.FRAMEBUFFER, h(prevFb));
    currentFramebufferId = prevFb;
    return ok;
}

export function deleteFramebuffer(id) {
    gl.deleteFramebuffer(h(id));
    handles[id] = null;
    fbColorTexture.delete(id);
}

export function getFramebufferColorTexture(fbId) {
    return fbColorTexture.get(fbId) ?? 0;
}

export function blitNamedFramebuffer(readFb, drawFb, sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, _mask, filter) {
    const prevFb = currentFramebufferId;
    gl.bindFramebuffer(gl.READ_FRAMEBUFFER, h(readFb));
    gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, h(drawFb));
    gl.blitFramebuffer(sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, gl.COLOR_BUFFER_BIT, filter === 1 ? gl.LINEAR : gl.NEAREST);
    gl.bindFramebuffer(gl.FRAMEBUFFER, h(prevFb));
    currentFramebufferId = prevFb;
}

export function readPixels(x, y, width, height) {
    const buf = new Uint8Array(width * height * 4);
    gl.readPixels(x, y, width, height, gl.RGBA, gl.UNSIGNED_BYTE, buf);
    return buf;
}
