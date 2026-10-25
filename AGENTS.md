# AGENTS.md — script-sync

Rhino 8 / Grasshopper plug-in + VSCode extension that runs C# and Python scripts
in Rhino from VSCode. Research utility from IBOIS lab (EPFL). Windows-only.

## Layout

| Path | Role |
|---|---|
| `CsRhino/` | Rhino C# plug-in (net48). `ScriptSyncStart.cs` is the TCP server + script runner. |
| `VSCode/scriptsync/` | VSCode TypeScript extension. `src/extension.ts` is the only real source file. |
| `GH/PyGH/components/<name>/` | Grasshopper Python components (`code.py` + `metadata.json` + `icon.png`). |
| `invokes/` | Python build scripts called by `tasks.py` (Invoke). |
| `manifest.yml` | Yak package metadata. **Version source of truth.** |
| `tickets/` | Numbered issue files (`NNN-slug.md`) with a `## Status` checklist. |

## Ports and flow

| Direction | Host | Port |
|---|---|---|
| VSCode → Rhino (send script path) | `127.0.0.1` | `58258` + instance offset |
| Rhino/GH → VSCode (GH stdout) | `127.0.0.1` | `58260` |

Protocol is **raw TCP, not HTTP**. VSCode writes the script path as plain bytes
(no JSON framing on the send side); Rhino replies with a JSON string
`{"success":bool,"error":string}`.

Rhino-side `.py` run sequence (`CsRhino/ScriptSyncStart.cs`):
1. Delete stale `<script>.py.error`.
2. Strip shebang; hoist `from __future__` imports to the top (CPython requires them first).
3. Write `.__scsy_wrapper__.py` and `py_compile` it → syntax errors go to `<script>.py.error`.
4. Rewrite wrapper with `try/except` + `traceback`, run it via `RhinoApp.RunScript("_-ScriptEditor _Run ...")`.
5. Reply JSON; delete wrapper with exponential-backoff retry (Rhino may still hold the file).

The extension writes its own temp file `.<name>__scsy_send__.py` next to the
script when it injects `sys.path`; it refuses to re-send a file ending in
`__scsy_send__.py` (would chain names forever).

## Multiple Rhino instances

Several Rhino instances can run `ScriptSyncStart` at the same time.

- **Port allocation** — each instance binds the first free port in `58258…58279`
  (`CsRhino/ScriptSyncPortAllocator.cs`). A lone instance keeps port 58258.
  `SO_REUSEADDR` does **not** allow sharing a live port on Windows — the ports
  must differ.
- **Registry** — every instance writes an entry to
  `%LOCALAPPDATA%\script-sync\instances.json` (`CsRhino/ScriptSyncInstanceRegistry.cs`):
  `{Pid, Port, Title, DocumentPath, StartedAt}`. **The keys are PascalCase** — the
  Python and TypeScript readers expect exactly `Pid`/`Port`/`Title`/`DocumentPath`.
  Entries whose process is gone are pruned on read (a crashed Rhino never runs
  `ScriptSyncStop`). Writes are atomic (temp file + swap) and the whole
  read-modify-write happens under a lock file, otherwise concurrent starts lose entries.
- **Selection** — VSCode command `scriptsync.selectInstance` shows a quick pick of the
  running instances. The choice is remembered in `workspaceState` with a
  `globalState` fallback, so it is configured once per folder. `F4` then always
  targets the remembered instance. `scriptsync.listInstances` prints them to the
  output channel; `scriptsync.clearInstance` forgets the selection.
- **Helpers** — `sync_to_rhino.py`, `rhipy_sync.py`, `sync_to_rhino_wsl.py`,
  `test_sync.py` and `CsRhino/client.py` accept `--port N` and `--instance NAME`
  (matched against the title or document path). `scriptsync_instances.py` lists
  the instances and is the shared module they import.
- **`rhipy_sync.py` reads the TCP response**, not the error file: the plug-in
  *deletes* `<script>.py.error` on success and never creates an empty one, so
  polling for the file alone always times out on a successful run.
- The Grasshopper side (port 58260) is **not** yet multi-instance aware: the GH
  component still hardcodes 58260 and the extension drops GH messages whose
  `script_path` basename does not match the active editor.

## Build and verify

```bash
# VSCode extension (TypeScript)
cd VSCode/scriptsync
npm install
npm run compile     # tsc -p ./
npm run lint        # eslint src --ext ts
npm run pretest     # compile + lint (this is what CI-ish local checks run)
npm test            # vscode-test; needs a headless VSCode, often fails locally

# Rhino plug-in (C#)
dotnet restore ./CsRhino/ScriptSync.csproj
dotnet build ./CsRhino/ScriptSync.csproj --configuration Release
# output: CsRhino/bin/Release/net48/ScriptSync.rhp
```

Packaging (Invoke tasks, run from repo root):

```bash
invoke vscerize       # writes VSCode/scriptsync/*.vsix (needs global `vsce`)
invoke yakerize       # builds .rhp + GH components -> yaker/build -> .yak
invoke ghcomponentize # GH/PyGH/components -> build/gh  (needs pythonnet + GH_IO.dll)
invoke syncv          # see versioning below
```

`invokes/ghcomponentize/ghcomponentizer.py` needs `pythonnet` and imports
`GH_IO.dll` via `clr` — it only runs under Windows with .NET interop available.

## Versioning — `syncv` mutates, it does not just sync

`invokes/syncv.py` **increments the patch number in `manifest.yml`** and copies
the result into `VSCode/scriptsync/package.json`. It is not a read-only sync.
It edits any line in either file containing the substring `version` — so adding
another line with "version" in it to `package.json` or `manifest.yml` will get
corrupted. This is a live bug: `manifest.yml` currently says `1.2.21` while
`package.json` says `1.2.29`. Do not "fix" it by hand-editing only one file.

Also note `manifest.yml` has no `Includes`/`Excludes` key; the `.yak` layout is
assembled by `invokes/yakerize.py` (rhp + manifest + logo + `misc/`), not by yak.

## Send a script without VSCode (handy for testing)

Root-level helpers, all speaking to a Rhino instance over TCP:

- `python sync_to_rhino.py <path>` — send and return immediately.
- `python rhipy_sync.py <path>` — send, read the TCP response; exit 1 on error.
- `python sync_to_rhino_wsl.py <path>` — same, but defaults to a **Windows host at `192.168.192.1`**; use from WSL.
- `python test_sync.py <path>` — minimal send.
- `python scriptsync_instances.py` — list the running instances (`--resolve` prints the port that would be used).
- `CsRhino/client.py` — thin wrapper over the same helpers.

All of them accept `--port N`, `--instance NAME` and `--host H`. With no flags
they use the only running instance, or the one on port 58258, and fail with a
clear message when the choice is ambiguous.

`rhipy_sync.py` / `sync_to_rhino_wsl.py` read the **TCP response** to decide
success or failure. The old error-file polling was broken: the plug-in deletes
`<script>.py.error` before each run and never writes an empty one, so a
successful run left no file to find and the helper always timed out.

## Known issues tracked in `tickets/`

Read `tickets/NNN-*.md` before touching the corresponding area. Files marked
`- [x] Open` and `- [ ] Resolved` are still outstanding:
`009` (hardcoded user path), `010` (missing script output), `011` (syntax-check
race), `012` (unused method — **fixed**, the method was removed), `013` (thread
leak), `014` (single-threaded execution), `015` (hardcoded ports — **partly
fixed**: the send path is now configurable, the GH path is not).

Live hardcoding bugs to be aware of:
- The GH component (`GH/PyGH/components/scriptsynccpy/code.py`) still hardcodes
  `vscode_server_port = 58260`, so the Grasshopper side is not multi-instance aware.
- `CsRhino/ScriptSyncStart.cs` reads the active document via
  `RhinoDoc.ActiveDoc` — note it is a static property on `RhinoDoc`, **not** on
  `RhinoApp` (there is no `RhinoApp.ActiveDoc` in RhinoCommon 7.13).

## Rhino plug-in constraints

- Target `net48`. Do **not** use .NET 6+ APIs. RhinoCommon pinned to
  `7.13.21348.13001` in `CsRhino/ScriptSync.csproj`.
- All Rhino UI calls must go through `RhinoApp.InvokeOnUiThread(...)` — the
  TCP worker thread cannot touch RhinoCommon directly.
- The listener binds the first free port in `58258…58279`, so several Rhino
  instances can listen at once. `ScriptSyncStart` stops its own previous
  listener and sets `SO_REUSEADDR`; `ScriptSyncStop` releases the OS socket via
  reflection on the private `_server` field, so it works even if the worker
  thread crashed.
- The listener is **started in `RunCommand`**, not in the worker thread `Run()`.
  `Run()` must not call `_server.Start()` again — it would throw. The port has to
  be allocated before the listener starts.
- `ScriptSyncStop` releases the socket; a leftover bound port means another
  Rhino/process holds it — do not add a "kill the process" fallback
  (an earlier PowerShell approach was deliberately commented out).
- `RhinoDoc.ActiveDoc` is a **static property on `RhinoDoc`**. There is no
  `RhinoApp.ActiveDoc`.
- The registry must never depend on Rhino being alive: `RhinoApp.WriteLine`
  throws outside a Rhino session, so logging goes through a helper that falls
  back to `Debug.WriteLine`.

## Extension constraints

- `tsconfig.json`: `module: Node16`, `target: ES2022`, `strict: true`, `rootDir: src`, `outDir: out`.
- ESLint rules in `.eslintrc.json` are all severity **`warn`**, not `error` —
  `npm run lint` exits 0 with violations. Don't rely on lint to catch breakage.
- The `sys.path` alignment feature (README "Conda Env Alignment") reads
  `python.defaultInterpreterPath` from VSCode settings and the Rhino
  interpreter path (`%USERPROFILE%\.rhinocode\py39-rh8\python.exe`). It
  excludes conda `lib/`, `DLLs/`, and `*.zip` paths because those shadow
  Rhino's bundled Python 3.9.10 stdlib and break cross-version extension
  modules like `_ctypes`.
- `writeTempScript` must run **after** `document.save()` resolves — otherwise
  `fs.readFileSync` reads a stale on-disk copy. This was a real bug; keep the ordering.
- Keybindings: `F4` and `Ctrl+Alt+R` → send to Rhino; `Shift+F4` → toggle the GH listener.
- The registry is read from `%LOCALAPPDATA%\script-sync\instances.json` and the
  keys are **PascalCase** (`Port`, `Pid`, `Title`, `DocumentPath`). A reader
  that looks for `port`/`pid` silently finds nothing — this bit twice already.
- The registry file must be written **without a UTF-8 BOM**. `JSON.parse` in
  node throws on a BOM, and the failure is silent (the reader returns "no
  instances"). Python's `open(..., encoding="utf-8")` writes a BOM on Windows —
  use `encoding="utf-8-sig"` to read, or write with `newline="\n"` and no BOM.
  The C# writer uses `Encoding.UTF8` without a BOM.
- All three readers (C#, node, python) filter out entries whose process is
  gone, and the node and python ones also prune them from the file.
- The GH listener only displays output if the active editor's filename equals
  the basename of the `script_path` in the incoming message.

## GH component constraints

- Component input params are declared in `metadata.json`, but `code.py` must
  still use the same names (`select_file`, `package_2_reload`, `x`).
- All modules under the script directory are `importlib.reload`ed on every
  solve, so `pickle` and any cross-solve class identity break. `package_2_reload`
  is the opt-in extra reload list for editable pip installs.
- Outputs that are lists/tuples are converted with `ghpythonlib.treehelpers.list_to_tree`;
  an explicit `Grasshopper.DataTree[System.Object]` return keeps its structure.
- The component persists the selected script path in the GH document value table
  under the key `script-sync::path::<InstanceGuid>`.

## Other

- `CsRhino/ScriptSync.csproj` excludes `tests/CsVersion.cs` and `tests/cs_cube.cs` from compilation.
- `.gitignore` excludes `*.rhp`, `*.vsix`, `build/`, and `node_modules/`; a
  committed `script-sync-1.2.29.vsix` already exists in `VSCode/scriptsync/`.
- CI (`.github/workflows/`) builds on `windows-latest` for push/PR to `main`;
  releases publish `.yak` and `.vsix`. The GH component CI job pins Python
  3.9.10 and installs `pythonnet`.
- `.github/copilot-instructions.md` and `README.md` are maintained in parallel
  with this file — update them if you change build commands or architecture.
