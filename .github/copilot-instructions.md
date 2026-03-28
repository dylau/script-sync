# Copilot Instructions for script-sync

## Project Overview

**Script-Sync** is a research utility developed by the [IBOIS lab](https://www.epfl.ch/labs/ibois/) at EPFL. It enables developers to run C# and Python scripts (CPython or IronPython) directly from **VSCode** into **Rhino 8** and **Grasshopper** while keeping both environments open simultaneously.

**Current Version:** 1.2.21  
**License:** MIT (IBOIS, EPFL)  
**Supported OS:** Windows 11

---

## Architecture

Script-Sync uses a **client-server TCP architecture**:

- **Server (Rhino):** C# plugin (`CsRhino/`) listens on TCP port `58259`
- **Client (VSCode):** TypeScript extension (`VSCode/scriptsync/`) connects and sends file paths as JSON
- **GH Listener:** VSCode listens on port `58260` for Grasshopper responses

### Language/Platform Support

| Platform     | CPython | IronPython | C# |
|--------------|---------|------------|----|
| Rhino        | ✅      | ✅         | ✅ |
| Grasshopper  | ✅      | ❌         | ❌ |

---

## Repository Structure

```
script-sync/
├── CsRhino/           # Rhino C# plugin (.NET Framework 4.8)
├── GH/
│   ├── PyGH/          # Python Grasshopper components
│   └── CsGH/          # C# Grasshopper component template
├── VSCode/
│   └── scriptsync/    # VSCode TypeScript extension (src/extension.ts)
├── invokes/           # Python build automation scripts
├── yaker/             # Yak packaging tools
├── .github/workflows/ # CI/CD GitHub Actions
├── tasks.py           # Invoke task definitions
└── manifest.yml       # Yak package metadata (version source of truth)
```

---

## Key Technologies

| Component        | Language   | Framework/Tools          |
|------------------|------------|--------------------------|
| Rhino Plugin     | C#         | .NET 4.8, RhinoCommon    |
| VSCode Extension | TypeScript | VSCode API, Node.js      |
| GH Components    | Python     | Grasshopper SDK, ghpythonlib |
| Build/Deploy     | Python     | Invoke, VSCE, Yak        |
| CI/CD            | YAML       | GitHub Actions           |

---

## Build Instructions

### Rhino Plugin (CsRhino)
```bash
cd CsRhino
dotnet restore
dotnet build --configuration Release
# Output: .\bin\Release\net48\ScriptSync.rhp
```

### VSCode Extension
```bash
invoke vscerize
# or manually:
cd VSCode\scriptsync
npm install
npm run compile
vsce package   # creates .vsix
```

### Yak Package (full distribution)
```bash
invoke yakerize
# Builds .rhp, GH components, and packages into .yak
```

### Grasshopper Components
```bash
invoke ghcomponentize
# Converts GH/PyGH/components/ Python scripts to GH components
```

### Version Sync (before release)
```bash
# Update version in manifest.yml, then:
invoke syncv
# Syncs manifest.yml version → VSCode package.json
```

---

## Conventions

### Python Script Headers (required for VSCode to dispatch correctly)
- `#! python3` → CPython interpreter
- `#! python2` → IronPython interpreter (Rhino only)

### GH Component Structure
Each component lives in `GH/PyGH/components/<name>/` and requires:
- `code.py` — implementation logic
- `metadata.json` — inputs/outputs, category, icon path
- `icon.png` — component icon

### Version Management
- **Single source of truth:** `manifest.yml`
- Always run `invoke syncv` after bumping the version

---

## Testing

### Rhino Plugin
- Use `CsRhino/client.py` to send test file paths to the running server
- Test scripts located in `CsRhino/tests/`
- Start plugin in Rhino: `ScriptSyncStart` command
- Stop plugin in Rhino: `ScriptSyncStop` command

### VSCode Extension
```bash
cd VSCode\scriptsync
npm run pretest   # compile + lint
npm test          # run test suite
```

---

## Important Notes

- **Target Framework:** .NET Framework 4.8 (not .NET 6+)
- **RhinoCommon Version:** 7.13.21348.13001
- **Module reloading:** Script-sync reloads all modules on every file save — be aware this can break `pickle` serialization
- **Data Trees:** Python nested lists are automatically converted to Grasshopper data trees via `ghpythonlib.treehelpers`
- **CI/CD:** GitHub Actions run on Windows-latest; push to `main` triggers build workflows; GitHub releases trigger publish to Yak and VSCode Marketplace
