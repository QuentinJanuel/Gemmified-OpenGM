# Gemmified-OpenGM

This is a fork of OpenGM that runs GameMaker Studio 1 and 2 games in the browser. It compiles
OpenGM to WebAssembly and packages it as a plugin for any host that speaks the GEMS plugin
protocol. The upstream project's README follows [below](#opengm).

## Install

Give a GEMS host this update feed:

```
https://gemmified-opengm.dappermink.me/latest.json
```

The feed lists only the latest release, and the host uses it to install the engine and keep it
up to date. A release publishes the plugin as
`https://gemmified-opengm.dappermink.me/<version>/opengm-<version>.zip` and makes it the only
version in the feed. Builds of earlier versions are then deleted.

## The plugin

A plugin is a directory with two parts:

- `plugin.json`: the manifest. It holds `id`, `version`, `apiVersion`, `entry`, `formats`,
  `updates` (the feed above) and `capabilities` (the settings the engine reads).
  `scripts/write-manifest.mjs` writes it from `host/plugin.js`, so the two always agree.
- `host.js` (built from `host/plugin.js`): the entry module. Its default export has an
  `accepts(files)` method, which picks a game's `data.win`, and a `create(host)` method, which
  returns a session.

The rest of the directory is the published `OpenGM.Wasm` app. The engine runs in a worker
(`opengm-worker.js`), and `OpenGM.Wasm/Gems.cs` is its side of the message protocol between the
worker and `host.js`. `host.js` owns what a worker cannot have: the `AudioContext` the engine's
sound is replayed on, the `Worker` itself, and the shared buffer that blocking GML dialogs are
answered through. Dialogs need `SharedArrayBuffer`, so they only work on a cross-origin isolated
page; a game that never opens one runs without it.

## Build

You need the .NET 10 SDK with the `wasm-tools` workload, Node 22 and pnpm. Clone with
`--recurse-submodules` (or run `git submodule update --init --recursive`), because
UndertaleModTool is a submodule with a submodule of its own.

```sh
dotnet workload install wasm-tools
pnpm install
pnpm run format:check
pnpm run test                # OpenGM.Tests and the release scripts' tests
pnpm run build               # AOT-compiled publish of OpenGM.Wasm, then dist/
pnpm run package             # the plugin directory, in plugin/
```

The build is a full AOT publish and takes several minutes. `OpenGM.Tests` runs GML bytecode
against the desktop project, so that project still builds. This fork does not run or ship the
desktop build, which the upstream README below describes.

### Releasing

Every pull request is checked by `.github/workflows/pull-request.yml`. A release is started by hand:
Actions, Release, Run workflow, on `gemmified`. It takes the version from the live feed and
increases the part you choose (patch by default), or takes an exact version. It builds the plugin
as that version, uploads it, replaces the feed, and creates the `v<version>` tag and GitHub
release. `package.json`'s version is only a placeholder, so there is never a version-bump commit.

## Relation to OpenGM

This repository is a fork of [OpenGM](https://github.com/misternebula/OpenGM). Its default branch,
`gemmified`, is a single commit on top of upstream commit
[`a23f7ee`](https://github.com/misternebula/OpenGM/commit/a23f7eea78173f46a5f70eb36c0bd0f11004c7b2)
(2026-07-14). `git diff a23f7eea gemmified` lists everything this fork changes. Upstream has newer
commits that are not merged here. Here is a summary:

- `OpenGM.Wasm` is new. It builds OpenGM's sources for `browser-wasm`, with WebGL2 rendering,
  Web Audio, keyboard and mouse input, and a virtual filesystem whose writes the host persists.
- `OpenGM` gains many GML built-in functions, and fixes measured against GameMaker Studio 1.4
  on Windows.
- `OpenGM.Tests` grows to cover them.
- `host/` and `scripts/` are new. They are the plugin's entry module and the release tooling.
- `UndertaleModTool` points at a [fork](https://github.com/QuentinJanuel/UndertaleModTool),
  branch `gemmified`: upstream UndertaleModTool plus one logging change.

## Licence

OpenGM is licensed MIT and so is this fork. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
UndertaleModTool, a submodule, is licensed GPL-3.0, and its Underanalyzer submodule MPL-2.0; see
their own licence files.

---

# OpenGM

## What is this?
This program aims to run Gamemaker Studio games, emulating the original games as closely as possible. The assembly code of the game is parsed and executed in a stack-based system, and the built-in Gamemaker functions are replicated too.

## How do I get this running with a game?
> [!IMPORTANT]  
> A lot of this is temporary, since the project is (relatively) unfinished. A better way of loading games is planned.
1. Build the project.
2. Navigate to (or create) `\OpenGM\OpenGM\bin\game\`
3. Place all files and folders from the game folder into this folder.
5. Run the project.
6. OpenGM should now convert the games `data.win` into a file named `data_OpenGM.win`. *This is not the same file format as the original file.* This file is a compressed file used to store intermediary data that OpenGM use.

If any errors occur, OpenGM probably does not support features of the GameMaker version the game uses.

## Supported Games
Ideally, any non-YYC Gamemaker game should work. However, every engine version has its own bugs and changes. To maintain compatibility, we're trying to cover as many quirks as possible, but it would be impossible to find everything. Here's what we've got so far :

- Prior to GMS2, depth rendering was forced to 0 - the same as using `layer_force_draw_depth`.
- Prior to GMS2, `#` characters were treated as newlines.
- Prior to 2.3, `script_execute` returned `0` instead of `null`.
- Prior to 2.3, script ids started at 0 instead of 10000.
- Prior to 2.3, instances of the same depth were rendered by descending instance id instead of ascending.
- Prior to 2022.1, collision was calculated with integer values instead of floating-point values.

The following games have been tested with OpenGM, and are used to improve functionality and test parity. If you test a different game, please let us know how it goes!

| Game | Engine Version | Notes |
| -------------------------------------------- | -------------------- | --------------------------------------------------------------------------------------------------------------- |
| MINDWAVE Demo								   | 2024.11.0.226		  | Loads, but missing core engine features.																		|
| DELTARUNE                                    | 2022.0.3.104 (LTS-I) |																													|
| - Chapter 1                                  | "					  | Only very minor visual glitches/flashes.																		|
| - Chapter 2                                  | "					  | 																												|
| - Chapter 3                                  | "					  |																													|
| - Chapter 4                                  | "					  |																													|
| DELTARUNE Chapter 1 & 2 DEMO (LTS Branch)    | 2022.0.3.99 (LTS)	  | Works well.																										|
| FAITH: The Unholy Trinity v1.5               | 2022.0.2.49 (LTS)    |	Loads, camera stuck in one place?																				|
| Pizza Tower                                  | 2022.0.1.30 (LTS)	  |	Works surprisingly well. Requires manually creating a `saves` folder in the game directory.						|
| Pizza Tower Eggplant Build                   | 2022.3.0.497		  |																													|
| DELTARUNE Chapter 1 & 2 DEMO                 | 2022.1.0.482		  |	Works well.																										|
| Pizza Tower SAGE 2019 Demo                   | 2.2.3.344			  |	Works well.    																									|
| DELTARUNE Chapter 1 (SURVEY_PROGRAM)         | 2.2.0.258			  |	Works well.																										|
| FAITH Demon Seige							   | 1.4.9999			  | Screen shader effect not working, some text invisible, camera stuck in top left, enemies don't spawn properly.	|
| Undertale                                    | 1.0.0.1539			  | Kind of works, but breaks in a lot of places.																	|

## Dependencies
- [MemoryPack](https://github.com/Cysharp/MemoryPack)
- [NAudio](https://github.com/naudio/NAudio)
- [OpenTK](https://github.com/opentk/opentk)
- [StbImageSharp](https://github.com/StbSharp/StbImageSharp)
- [StbVorbisSharp](https://github.com/StbSharp/StbVorbisSharp)
- [MotionTK](https://github.com/AtomCrafty/MotionTK)
- [UndertaleModLib](https://github.com/UnderminersTeam/UndertaleModTool)

## Legal Stuff
This project is covered by the MIT license. See [the license](LICENSE) for more information.

Parts of the [HTML runner](https://github.com/YoYoGames/GameMaker-HTML5) have been used as reference for how to implement certain functionality.
