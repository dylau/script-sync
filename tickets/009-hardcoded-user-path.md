# 009 - HARDCODED USER-SPECIFIC PATH

## Severity
🔴 Critical

## Description
The Python initialization script path is hardcoded to a specific user's Rhinocode directory:

```csharp
string initScriptPath = @"C:\Users\uk083720\.rhinocode\py39-rh8\lib\importlib\__init__.py";
```

This will fail for any user other than `uk083720`.

## Affected Files
- `CsRhino/ScriptSyncStart.cs` (line ~115)

## Expected Behavior
The Python init script path should be resolved programmatically based on the current user's environment.

## Questions
- Is there a RhinoCommon API to locate the Rhinocode/py39-rh8 directory?
- Or should this default to `%USERPROFILE%\.rhinocode\py39-rh8\lib\importlib\__init__.py`?

## Status
- [x] Open
- [ ] In Progress
- [x] Resolved

## Resolution
Replaced the hardcoded `C:\Users\uk083720\.rhinocode\py39-rh8\lib\importlib\__init__.py` warm-up path with a freshly generated `%TEMP%\__scriptsync_init__.py` containing `#! python3\nimport sys; print('ScriptSync: Python', sys.version.split()[0], 'ready', flush=True)` (see `CsRhino/ScriptSyncStart.cs:141-158`). Also added `File.Exists` guards before each `RunScript` call site (warm-up, syntax-check runner, wrapped script, fallback) so a missing/bad path produces a clean log message instead of Rhino's `ScriptEditor` `Path of script to run ( Browse )` prompt. Rebuilt with `dotnet build -c Release` -> `CsRhino/bin/Release/net48/ScriptSync.rhp`.