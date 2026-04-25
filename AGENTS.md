# AGENTS.md - Agentic Coding Guidelines for script-sync

## Overview

**script-sync** is a research utility from IBOIS lab (EPFL) that enables running C# and Python scripts from VSCode into Rhino 8 and Grasshopper. Client-server TCP architecture.

- **Version:** 1.2.21
- **License:** MIT
- **Platform:** Windows 11 only

---

## Project Structure

```
script-sync/
├── CsRhino/                    # Rhino C# plugin (.NET Framework 4.8)
├── GH/PyGH/components/         # Grasshopper Python components
├── VSCode/scriptsync/         # VSCode TypeScript extension
├── tasks.py                    # Invoke task definitions
└── manifest.yml                 # Version source of truth
```

---

## Architecture

| Port | Direction      | Purpose                              |
|-----|----------------|--------------------------------------|
| 58258 | VSCode → Rhino | Send script file path for execution |
| 58260 | Rhino → VSCode  | Grasshopper component response       |

**Shebang selection:** `#! python3` (CPython), `#! python2` (IronPython), `.cs` (C#)

**Error capture:** Python scripts are wrapped in try/except; traceback written to `[scriptname].py.error`

---

## Build, Lint, and Test Commands

### VSCode Extension (TypeScript)

```bash
cd VSCode/scriptsync
npm install          # Install dependencies
npm run compile      # tsc -p ./
npm run watch        # Watch mode
npm run lint         # ESLint
npm run pretest      # compile + lint
npm test             # Full test suite
```

**Running a single test:**
```bash
cd VSCode/scriptsync
npm run pretest
npx mocha out/test/**/*.test.js --grep "test name pattern"
```

### Rhino Plugin (C#)

```bash
cd CsRhino
dotnet restore
dotnet build --configuration Release
# Output: .\bin\Release\net48\ScriptSync.rhp
```

### Build Automation (Invoke)

```bash
invoke vscerize       # Build .vsix
invoke yakerize      # Build .yak
invoke syncv         # Sync version manifest.yml → package.json
invoke ghcomponentize # Convert GH components
```

---

## Code Style Guidelines

### TypeScript (VSCode Extension)

**TypeScript Configuration:**
- Target: ES2022, Module: Node16, Strict mode enabled
- `tsconfig.json` in `VSCode/scriptsync/`

**ESLint Rules** (`VSCode/scriptsync/.eslintrc.json`):
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

**Import Order:** External libraries → Node.js built-ins → Relative project imports

**Error Handling:**
- `try/catch` for all async operations
- Display errors via `vscode.window.showErrorMessage()`
- Handle socket errors gracefully (e.g., `ECONNRESET`)

### C# (Rhino Plugin)

- Target Framework: `.NET 4.8` — do NOT use .NET 6+ APIs
- Inherit command classes from `Rhino.Commands.Command`
- GUI operations must use `RhinoApp.InvokeOnUiThread()`
- TCP listener runs on dedicated background thread

### Python (Grasshopper Components)

**Shebang headers (required):** `#! python3` or `#! python2`
- GH only supports CPython (no IronPython, no C#)

**Module Reloading:**
- Script-sync reloads all modules on every file save
- Avoid `pickle` — breaks on reloading

**GH Component Structure:**
```
GH/PyGH/components/<name>/
├── code.py        # implementation (include shebang)
├── metadata.json # inputs/outputs, category, subcategory
└── icon.png      # 24x24 component icon
```

---

## Testing Guidelines

### VSCode Extension (Mocha)

```typescript
import * as vscode from 'vscode';
import * as assert from 'assert';

suite('Extension Test Suite', () => {
    test('Description', async () => {
        assert.ok(someCondition);
    });
});
```

Test files in `src/test/**/*.test.ts`

### Manual Testing (Rhino)

```bash
python CsRhino/client.py  # Send test scripts to Rhino
```

Lifecycle: Build `.rhp` → Drag into Rhino → Run `ScriptSyncStart` → Send scripts (F4) → Run `ScriptSyncStop`

---

## Common Pitfalls

- Only one Rhino instance can run script-sync (port conflict: `EADDRINUSE`)
- Script editor must be initialized in `ScriptSyncStart.cs`
- Paths sent as ASCII; non-ASCII characters cause errors
- Use exact package versions in `package.json` to avoid CI issues
- Module reloading breaks `pickle` serialization
- Data Trees: Python nested lists auto-convert to Grasshopper data trees via `ghpythonlib.treehelpers`

---

## Version Management

- **Single source of truth:** `manifest.yml`
- `invoke syncv` increments patch digit (e.g., `1.2.21` → `1.2.22`)
- For major/minor: edit `manifest.yml`, then run `invoke syncv`

---

## Additional Resources

- [Copilot Instructions](./.github/copilot-instructions.md)
- [Contributing Guidelines](./CONTRIBUTING.md)
- [README](./README.md)