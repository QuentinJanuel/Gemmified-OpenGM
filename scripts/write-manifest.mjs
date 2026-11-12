// Writes dist/plugin.json from the plugin entry's own object, so the manifest and the engine cannot
// disagree. Run by `pnpm build`.
import { readFile, writeFile } from "node:fs/promises";
import plugin from "../host/plugin.js";

const { version } = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));

const manifest = {
  id: plugin.id,
  version,
  apiVersion: plugin.apiVersion,
  entry: "host.js",
  name: "OpenGM",
  formats: ["gms1", "gms2"],
  description:
    "Runs GameMaker Studio 1 and 2 games in the browser, on a WebAssembly build of OpenGM, a reimplementation of the GameMaker runner in C#.",
  author: "Dapper Mink",
  updates: "https://gemmified-opengm.dappermink.me/latest.json",
  capabilities: plugin.capabilities,
};

await writeFile(new URL("../dist/plugin.json", import.meta.url), `${JSON.stringify(manifest, null, 2)}\n`);
console.log(`write-manifest: wrote dist/plugin.json for ${plugin.id}@${version}`);
