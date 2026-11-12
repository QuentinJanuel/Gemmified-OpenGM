// Backs OpenTKShim/BrowserInput.cs -- real keyboard/mouse state for the browser-wasm build.
// Attached at window level (not the canvas) so key events work regardless of focus, matching how
// most browser games handle input.

// When the engine runs in a Web Worker (GMS relocation) there's no DOM to listen to -- the host
// captures input and pushes it in as GEMS's `GameInput`, which applyInputEvent() applies. On the
// main thread we attach real listeners as before.
import { publishInputApplier } from "./services.js";

const inWorker = typeof window === "undefined";

const keysDown = new Set();
const keysJustPressed = new Set();

if (!inWorker) {
    window.addEventListener("keydown", (e) => {
        if (!keysDown.has(e.code)) {
            keysJustPressed.add(e.code);
        }
        keysDown.add(e.code);
        // stop the page from scrolling on arrow keys / space, common in platformers
        if (e.code.startsWith("Arrow") || e.code === "Space") {
            e.preventDefault();
        }
    });

    window.addEventListener("keyup", (e) => {
        keysDown.delete(e.code);
    });
}

export function isKeyDown(code) {
    return keysDown.has(code);
}

export function isKeyPressed(code) {
    return keysJustPressed.has(code);
}

export function isAnyKeyDown() {
    return keysDown.size > 0;
}

export function clearJustPressed() {
    keysJustPressed.clear();
}

const mouseButtonsDown = new Set();
let mouseX = 0;
let mouseY = 0;

const canvas = inWorker ? null : document.getElementById("canvas");

function updateMousePos(e) {
    const rect = canvas.getBoundingClientRect();

    // In fullscreen the canvas is styled with object-fit: contain (see index.html), so the
    // rendered image is letterboxed inside rect rather than filling it -- rect itself stays the
    // full 100vw x 100vh box. Compute the actual letterboxed content box from the canvas's
    // intrinsic aspect ratio vs rect's aspect ratio before mapping client coords. Outside
    // fullscreen the canvas has no object-fit and rect's aspect ratio already matches the
    // canvas's own (height: auto), so this reduces to the old plain rect-relative math with zero
    // offset either way.
    const canvasAspect = canvas.width / canvas.height;
    const rectAspect = rect.width / rect.height;

    let contentWidth = rect.width;
    let contentHeight = rect.height;
    let offsetX = 0;
    let offsetY = 0;

    if (rectAspect > canvasAspect) {
        contentWidth = rect.height * canvasAspect;
        offsetX = (rect.width - contentWidth) / 2;
    } else if (rectAspect < canvasAspect) {
        contentHeight = rect.width / canvasAspect;
        offsetY = (rect.height - contentHeight) / 2;
    }

    mouseX = ((e.clientX - rect.left - offsetX) / contentWidth) * canvas.width;
    mouseY = ((e.clientY - rect.top - offsetY) / contentHeight) * canvas.height;
}

if (!inWorker) {
    canvas.addEventListener("mousemove", updateMousePos);
    canvas.addEventListener("mousedown", (e) => { updateMousePos(e); mouseButtonsDown.add(e.button); });
    window.addEventListener("mouseup", (e) => mouseButtonsDown.delete(e.button));
    canvas.addEventListener("contextmenu", (e) => e.preventDefault());
}

export function isMouseButtonDown(button) {
    return mouseButtonsDown.has(button);
}

export function getMouseX() {
    return mouseX;
}

export function getMouseY() {
    return mouseY;
}

// Accumulated since the last clearWheelDelta() (called once per frame from InputHandler, mirroring
// clearJustPressed's own per-frame reset) -- WheelEvent's own deltaY convention (negative = wheel
// scrolled up/away from the user, positive = scrolled down/toward the user).
let wheelDeltaY = 0;

if (!inWorker) {
    canvas.addEventListener("wheel", (e) => {
        wheelDeltaY += e.deltaY;
        // stop the page itself from scrolling while the pointer is over the game canvas
        e.preventDefault();
    }, { passive: false });
}

export function getWheelDeltaY() {
    return wheelDeltaY;
}

export function clearWheelDelta() {
    wheelDeltaY = 0;
}

// GEMS names buttons; the DOM numbers them, and so does everything below and in BrowserInput.cs.
const BUTTONS = { left: 0, middle: 1, right: 2 };

// Applies one `GameInput` the host pushed in -- already normalized, and already in game pixels
// where it has coordinates, because the host is the side that knows the surface's layout. No-op on
// the main thread, which keeps its own live state from the listeners above.
//
// Published through services.js so the worker reaches THIS instance -- the one C#'s
// [JSImport("input")] reads its state from -- rather than a second, disconnected copy from importing
// the module again (the module-instance hazard services.js documents).
export function applyInputEvent(input) {
    switch (input.kind) {
        case "key-down":
            // Unconditionally a fresh press: the host drops auto-repeat, so one of these on a key
            // that is already down is a host that *means* a second press (an on-screen control, a
            // test) and GameMaker's keyboard_check_pressed should see it.
            keysJustPressed.add(input.code);
            keysDown.add(input.code);
            break;
        case "key-up":
            keysDown.delete(input.code);
            break;
        case "focus-lost":
            // Everything held is let go. The host cannot send the releases themselves -- they went
            // to whatever took the focus -- and without this a key held when a dialog opened stays
            // down forever. See `GameInput`.
            keysDown.clear();
            break;
        case "pointer-move":
            mouseX = input.x;
            mouseY = input.y;
            break;
        case "pointer-down":
            mouseX = input.x;
            mouseY = input.y;
            mouseButtonsDown.add(BUTTONS[input.button]);
            break;
        case "pointer-up":
            mouseButtonsDown.delete(BUTTONS[input.button]);
            break;
        case "wheel":
            wheelDeltaY += input.delta;
            break;
    }
}

if (inWorker) {
    publishInputApplier(applyInputEvent);
}
