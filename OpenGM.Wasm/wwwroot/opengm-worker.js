// OpenGM's worker: boots the .NET runtime when the host's init arrives, then hands every message to
// the C# half of the wire (OpenGM.Wasm/Gems.cs). The runtime is fetched from init's assetBase, so it
// cannot start any earlier; messages that arrive while it loads wait for it.
//
// The engine runs in a worker so its blocking GML dialogs (show_message / show_question /
// get_string / ...) can suspend the game with Atomics.wait without freezing the page.

import { dotnet } from "./_framework/dotnet.js";

/**
 * Assets this engine's payload does not serve from beside itself.
 *
 * `dotnet.native.<hash>.wasm` is the AOT runtime, ~52 MiB uncompressed, which Cloudflare Pages
 * refuses to serve (its limit is 25 MiB), so a host may publish it at the plugin's asset base
 * instead. The .NET bootstrap builds its own URLs from its own `_framework/` manifest, so the only
 * place to redirect it is `fetch`. In a host that serves the whole plugin, the base *is* where this
 * file came from and the rewritten URL is the same URL.
 */
const REMOTE_ASSETS = [/\/_framework\/dotnet\.native\.[^/]*\.wasm$/];

const early = [];
let deliver = null;
let booting = false;

self.onmessage = ({ data }) => {
  if (deliver) return deliver(data);
  early.push(data);
  if (!booting && data?.w === "req" && data.body?.kind === "init") {
    booting = true;
    void boot(data.body.init.assetBase);
  }
};

async function boot(assetBase) {
  try {
    redirectRemoteAssets(assetBase);
    const runtime = await dotnet.withApplicationArguments("start").create();
    const { Gems } = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).OpenGM;
    await Gems.Boot();
    deliver = (data) => {
      // Not an envelope, not ours: the port may be shared.
      if (typeof data === "object" && data !== null) Gems.OnMessage(data);
    };
  } catch (error) {
    // Nothing loaded, so every request is answered rather than left to hang.
    const detail = String(error?.message ?? error);
    deliver = (data) => {
      if (data?.w === "req") {
        self.postMessage({ w: "err", id: data.id, error: { engineError: true, code: "internal", detail } });
      }
    };
  }
  early.splice(0).forEach(deliver);
}

function redirectRemoteAssets(base) {
  const root = String(base).replace(/\/$/, "");
  const original = globalThis.fetch.bind(globalThis);
  globalThis.fetch = (input, init) => {
    const raw = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    const { pathname } = new URL(raw, self.location.href);
    if (REMOTE_ASSETS.some((pattern) => pattern.test(pathname))) {
      return original(`${root}/_framework/${pathname.split("/").pop()}`, init);
    }
    return original(input, init);
  };
}
