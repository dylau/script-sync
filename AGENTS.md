# AGENTS.md - Agentic Coding Guidelines for script-sync

## Overview

**script-sync** is a research utility from IBOIS lab (EPFL) that enables running C# and Python scripts from VSCode into Rhino 8 and Grasshopper. The project uses a client-server TCP architecture.

- **License:** MIT
- **Platform:** Windows 11 only
- **Repository:** https://github.com/ibois-epfl/script-sync

---

## Project Structure

```
script-sync/
├── CsRhino/              # Rhino C# plugin (.NET Framework 4.8)
│   ├── ScriptSyncStart.cs    # Core TCP server + script execution logic
│   ├── ScriptSyncStop.cs     # Shutdown command
│   ├── ScriptSyncPlugin.cs   # Plugin entry point
│   └── tests/                # Test scripts (.py, .cs)
├── GH/
│   ├── PyGH/components/      # Grasshopper Python components
│   │   └── scriptsynccpy/    # Main GH component (code.py, metadata.json, icon.png)
│   └── CsGH/                 # C# Grasshopper component template
├── VSCode/scriptsync/
│   └── src/extension.ts      # VSCode extension entry point (TCP client + GH server)
├── invokes/              # Python build automation scripts
├── yaker/                # Yak packaging tools
├── .github/workflows/    # CI/CD GitHub Actions
├── tasks.py              # Invoke task definitions
└── manifest.yml          # Yak package metadata (version source of truth)
```

---

## Architecture

Script-sync uses **TCP sockets** for all communication:

| Port  | Direction        | Purpose                              |
|-------|------------------|--------------------------------------|
| 58259 | VSCode → Rhino   | Send script file path for execution  |
| 58260 | Rhino → VSCode   | Grasshopper component response       |

**Protocol flow:**
1. User presses **F4** in VSCode → extension sends active file path (ASCII) over TCP to port 58259
2. Rhino server receives path, wraps Python scripts with try/except, executes via ScriptEditor
3. Error traceback written to `{scriptName}.py.error` alongside the script
4. Response sent back to VSCode

**Script interpreter selection** (shebang in first line):
- `#! python3` → CPython
- `#! python2` → IronPython (Rhino only)
- `.cs` extension → C# (Rhino only)

**Error capture:**
- For Python scripts, the plugin automatically injects try/except wrapper
- On error: full traceback written to `[scriptname].py.error` file
- Error also displays in Rhino console (after wrapper catches it, re-raises)
- Agent can read `.error` file to understand what failed

**Grasshopper** (Shift+F4): VSCode acts as TCP server on port 58260; the GH component connects to it.

---

## Build, Lint, and Test Commands

### VSCode Extension (TypeScript)

```bash
cd VSCode/scriptsync
npm install          # Install dependencies
npm run compile      # tsc -p ./
npm run watch        # Watch mode for development
npm run lint         # ESLint
npm run pretest      # compile + lint
npm test             # Full test suite
```

**Running a single test:**

```bash
cd VSCode/scriptsync
npm run pretest  # compile + lint first
npx mocha out/test/**/*.test.js --grep "test name pattern"
```

Or modify `.vscode-test.mjs` to target specific tests.

### Rhino Plugin (C#)

```bash
cd CsRhino
dotnet restore
dotnet build --configuration Release
# Output: .\bin\Release\net48\ScriptSync.rhp
```

### Build Automation (Invoke Tasks)

```bash
invoke vscerize       # Build .vsix (VSCode extension package)
invoke yakerize       # Build .yak (full Rhino+GH distribution)
invoke syncv          # Bump patch version: manifest.yml → package.json
invoke ghcomponentize # Convert GH/PyGH/components/ scripts to GH components
```

### Version Management

- **Single source of truth:** `manifest.yml`
- `invoke syncv` only increments the **patch** digit (e.g., `1.2.21` → `1.2.22`)
- For major/minor bumps: manually edit `manifest.yml`, then run `invoke syncv`
- Files modified by `syncv`: `manifest.yml` and `VSCode/scriptsync/package.json`

---

## Code Style Guidelines

### TypeScript (VSCode Extension)

**TypeScript Configuration:**
- Target: ES2022, Module: Node16, Strict mode enabled

**ESLint Rules** (see `VSCode/scriptsync/.eslintrc.json`):
- Semicolons: required
- Curly braces: required for all blocks
- Equality: `===` / `!==` only
- Never throw literals — throw `Error` objects

**Formatting:**
- 4 spaces indentation, max 100 chars per line
- Template literals over string concatenation
- `const` over `let`; never `var`
- Always declare explicit return types

**Naming Conventions:**
- Variables/functions: `camelCase`
- Classes/interfaces/types: `PascalCase`
- Constants: `UPPER_SNAKE_CASE`
- File names: `kebab-case.ts` or `camelCase.ts`

**Error Handling:**
- `try/catch` for all async operations
- Display errors via `vscode.window.showErrorMessage()`
- Handle socket errors gracefully (e.g., `ECONNRESET`)

### C# (Rhino Plugin)

- Target Framework: `.NET 4.8` — do **not** use .NET 6+ APIs
- Command classes inherit from `Rhino.Commands.Command`
- All GUI operations must use `RhinoApp.InvokeOnUiThread()`
- TCP listener runs on a dedicated background thread
- JSON response format: `{"success": bool, "error": ""}`

### Python (Grasshopper Components)

**Shebang headers (required in all scripts):**
- `#! python3` — CPython interpreter
- `#! python2` — IronPython (Rhino only)

**Data Trees:**
- Python nested lists auto-convert to GH data trees
- Use `ghpythonlib.treehelpers` for manual conversion

**Module Reloading:**
- Script-sync reloads all modules on every file save
- ⚠️ Avoid `pickle` — reloading breaks pickle (de)serialization

### Grasshopper Component Structure

Each component lives in `GH/PyGH/components/<name>/` and requires exactly:
- `code.py` — implementation logic (include shebang)
- `metadata.json` — inputs/outputs, category, subcategory, icon path
- `icon.png` — 24×24 component icon

---

## Import Guidelines

### TypeScript

```typescript
// Good — namespace imports
import * as vscode from 'vscode';
import * as net from 'net';
import { activate, deactivate } from './extension';
```

**Import order:**
1. External libraries (`vscode`, Node.js built-ins)
2. Relative project imports

---

## Testing Guidelines

### VSCode Extension Tests

- Uses Mocha + VSCode Test Runner
- Test files: `src/test/**/*.test.ts`
- Always run `npm run pretest` before testing

**Test pattern:**

```typescript
import * as vscode from 'vscode';
import * as assert from 'assert';

suite('Extension Test Suite', () => {
    test('Description', async () => {
        assert.ok(someCondition);
    });
});
```

### Manual Testing (Rhino)

Use `CsRhino/client.py` to send test file paths to the running server:
```bash
python CsRhino/client.py
```
Test scripts are located in `CsRhino/tests/`.

**Plugin lifecycle for manual testing:**
1. Build `.rhp` and drag into Rhino (or copy to Rhino plugins folder)
2. Run `ScriptSyncStart` command in Rhino to start TCP server
3. Use F4 in VSCode (or `client.py`) to send scripts
4. Run `ScriptSyncStop` to shut down

---

## Common Pitfalls

- **Only one instance**: Only one Rhino can run script-sync per machine (port conflict: `EADDRINUSE`)
- **Script editor init**: `ScriptSyncStart.cs` must initialize the Rhino ScriptEditor on startup — do not remove this step
- **Grasshopper limitations**: GH only supports CPython (no IronPython, no C#)
- **Path encoding**: Paths are sent as ASCII; non-ASCII characters in paths will cause errors
- **No pickle**: Module reloads break pickle serialization — use other formats (JSON, etc.)
- **`.NET 4.8` only**: Do not use .NET 6+ APIs — Rhino 8 embeds its own runtime
- **Exact package versions**: Use exact versions in `package.json` to avoid breaking CI
- **Error files**: After F4, check `[scriptname].py.error` for full traceback

---

## CI/CD

GitHub Actions run on `windows-latest`. Workflows in `.github/workflows/`:

| Workflow | Trigger | Output |
|----------|---------|--------|
| `rhinoplugin.yml` | Push/PR to main | `ScriptSync.rhp` artifact |
| `vscodeext.yml` | Push/PR to main | `.vsix` artifact |
| `yakbuild.yml` | Push/PR to main | `.yak` artifact |
| `publish.yml` | GitHub Release | Publish to Yak + VSCode Marketplace |

**Secrets required for publishing:** `ADMIN_PAT_TOKEN`, `YAK_IBOIS_TOKEN`, `AZURE_OP_TOKEN`

---

## Running Scripts from Agents (Outside VSCode)

Agents can run scripts in Rhino without VSCode:

### Prerequisites

1. Rhino open with `ScriptSyncStart` command run (starts TCP server on port 58259)
2. Python installed to run the sync script

### Using sync_to_rhino.py

```bash
python C:\path\to\script-sync\sync_to_rhino.py C:\path\to\your_project\script.py
```

The script is at: `script-sync/sync_to_rhino.py`

### Workflow

1. Agent runs the sync script to send code to Rhino
2. Rhino executes the script (auto-wrapped with try/except for error capture)
3. Check for `[scriptname].py.error` for full traceback if errors occur
4. Error also shows in Rhino console

### Key Points

- VSCode extension NOT required - only the `.rhp` plugin needed
- Works from any folder - agent just needs path to the sync script
- Error capture: `[scriptname].py.error` file created automatically
- C# scripts also work (no error file for .cs)

---

## Important Notes

- **TCP Ports:** Rhino server = `58259`; GH listener = `58260`
- **Publishing:** `.yak` and `.vsix` published automatically on GitHub release
- **Dependencies:** Use exact versions in `package.json`

---

## Additional Resources

- [Copilot Instructions](./.github/copilot-instructions.md)
- [Contributing Guidelines](./CONTRIBUTING.md)
- [README](./README.md)
