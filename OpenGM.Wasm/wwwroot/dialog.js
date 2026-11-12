// Backs OpenGM's blocking GML dialogs (show_message / show_question / get_string / get_integer) in
// the worker-hosted engine. These are synchronous in GML -- they block the game and return a value
// inline -- and here the worker thread genuinely blocks while the host shows a real dialog, without
// freezing the UI (the UI runs on a different thread). This is the whole reason OpenGM was
// relocated to a worker; a main-thread engine can't block for a UI answer without deadlock.
//
// The blocking itself is services.js's `dialogs()`: it sends the question, blocks on the handshake
// buffer, and hands back the answer inline, or reports `unsupported-environment` where there is no
// buffer.

import { dialogs } from "./services.js";

const LABEL_SEP = "\u001f"; // matches the separator C# joins button captions with

let lastValue = "";

function buildRequest(text, labels, primary, cancel, inputKind, inputInitial) {
    const parts = labels.length > 0 ? labels.split(LABEL_SEP) : [];
    const buttons = parts.map((label, i) => ({
        label,
        value: i,
        kind: i === primary ? "primary" : undefined,
    }));
    const input = inputKind === 0 ? undefined : { kind: inputKind === 2 ? "integer" : "string", initial: inputInitial };
    // cancel === -1 means not dismissible (show_message); any other value allows Escape/backdrop.
    return { text, buttons, input, dismissible: cancel !== -1 };
}

// Returns the 0-based index of the clicked button (-1 if dismissed); the input text is fetched
// separately via dialogValue().
export function showDialog(text, labels, primary, cancel, inputKind, inputInitial) {
    const result = dialogs().show(
        buildRequest(text, labels, primary, cancel, inputKind, inputInitial),
    );
    // Always a string here, even for a dialog with no input field -- see `BufferedDialogResult`.
    lastValue = result.value;
    return result.button;
}

export function dialogValue() {
    return lastValue;
}
